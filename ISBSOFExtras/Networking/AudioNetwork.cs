using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Text;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private EventContentCheck _eventContentCheck;
        private readonly HashSet<string> _contentPeersReported = new HashSet<string>();
        private bool _contentWarningDisplayed;
        private string LocalEventContent => _eventContentCheck?.Signature ?? "pending";

        private void CheckEventContent(string remote)
        {
            string local = LocalEventContent;
            if (local == "pending" || remote == "pending" || !_contentPeersReported.Add(remote))
                return;
            if (EventContentCheck.Compatible(local, remote))
            {
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Fika: Peer event DLLs and assets match this installation.");
                }

                return;
            }

            Log.LogWarning("ISB SOF Fika: Event package mismatch or incomplete installation. local=" + local + ", peer=" + remote + ". Every player must install the same complete ISB client package; automatic bundle downloads are insufficient.");
            if (CanPresentLocally && !_contentWarningDisplayed)
            {
                _contentWarningDisplayed = true;
                EFT.Communications.NotificationManager.DisplayMessageNotification("ISB: Host/client event files differ or are missing. Install the same complete ISB client package on every PC, then restart the game.", EFT.Communications.ENotificationDurationType.Long);
            }
        }

        private void TryInitializeOptionalFikaBridge()
        {
            if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid))
                return;
            string pluginDirectory = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Log.LogError("ISB SOF Extras: Fika is installed, but the Extras plugin directory could not be resolved.");
                return;
            }

            string bridgePath = Path.Combine(pluginDirectory, FikaBridgeAssemblyFileName);
            if (!File.Exists(bridgePath))
            {
                Log.LogError("ISB SOF Extras: Fika is installed, but " + FikaBridgeAssemblyFileName + " was not found next to the normal Extras DLL. Fika synchronization is disabled.");
                return;
            }

            try
            {
                Assembly bridgeAssembly = FindLoadedAssembly(FikaBridgeAssemblyName) ?? Assembly.LoadFrom(bridgePath);
                Type bootstrapType = bridgeAssembly.GetType(FikaBridgeBootstrapTypeName, true);
                MethodInfo initializeMethod = bootstrapType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                _fikaBridgeShutdownMethod = bootstrapType.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static);
                if (initializeMethod == null || _fikaBridgeShutdownMethod == null)
                    throw new MissingMethodException(FikaBridgeBootstrapTypeName, "Initialize/Shutdown");
                initializeMethod.Invoke(null, new object[] { Log });
                _fikaBridgeInitialized = NetworkBridge.Active;
                if (!_fikaBridgeInitialized)
                {
                    Log.LogError("ISB SOF Extras: the optional Fika bridge loaded, but did not become active.");
                }
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Log.LogError("ISB SOF Extras: optional Fika bridge initialization failed: " + cause.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF Extras: optional Fika bridge initialization failed: " + ex.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Assembly assembly = loadedAssemblies[i];
                if (assembly == null)
                    continue;
                AssemblyName name;
                try
                {
                    name = assembly.GetName();
                }
                catch
                {
                    continue;
                }

                if (string.Equals(name.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return assembly;
                }
            }

            return null;
        }

        private void ShutdownOptionalFikaBridge()
        {
            if (!_fikaBridgeInitialized || _fikaBridgeShutdownMethod == null)
                return;
            try
            {
                _fikaBridgeShutdownMethod.Invoke(null, null);
            }
            catch (TargetInvocationException)
            {
            }
            catch (Exception ex)
            {
                Log.LogWarning("ISB SOF Extras: optional Fika bridge shutdown failed: " + ex.Message);
            }
            finally
            {
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        private void HandleFikaPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return;
            if (_scp173?.Receive(payload) == true)
                return;
            string[] parts = payload.Split('|');
            if (parts.Length == 0)
                return;
            if (parts[0] == "OP" || parts[0] == "OPR" || parts[0] == "OQ" || parts[0] == "OC" || parts[0] == "OT" || parts[0] == "OU" || parts[0] == "OR" || parts[0] == "OA" || parts[0] == "ON")
            {
                if (payload.Length <= 40000)
                    ReceiveOfficePacket(parts);
                return;
            }

            if (parts[0] == "FS" && parts.Length == 2)
            {
                ReceiveFacilitySequence(parts[1]);
                return;
            }

            if (parts[0] == "CV")
            {
                if (_raidisActive && !IsEventAuthority && parts.Length == 3 && parts[1] == LocalEventContent)
                    CheckEventContent(parts[2]);
                return;
            }

            if (parts[0] == "Q")
            {
                if (IsFikaNetworkSession && IsFikaServer)
                    HandleFikaClientReady(parts);
                return;
            }

            if (parts[0] == "K")
            {
                if (!IsEventAuthority)
                    _fikaClientReadyAcknowledged = true;
                return;
            }

            if (parts[0] == "W")
            {
                if (_raidisActive && IsLaboratoryPresentationAllowed())
                    LabsKeycardLockdown.ReceiveBattery(parts);
                return;
            }

            if (parts[0] == "QE")
            {
                if (parts.Length == 2)
                    ReceiveSharedAuthorizationEffects(parts[1]);
                return;
            }

            if (parts[0] == "T")
            {
                if (IsFikaNetworkSession && IsFikaServer && parts.Length >= 2)
                    ApplyTakeoverActionAuthority(parts[1]);
                return;
            }

            if (IsEventAuthority || !_raidisActive)
                return;
            switch (parts[0])
            {
                case "DV":
                    if (parts.Length == 2)
                        ReceiveDecontaminationVideo(parts[1]);
                    break;
                case "X":
                    if (parts.Length == 2)
                        ReceiveDecontaminationEventState(parts[1]);
                    break;
                case "D":
                    if (parts.Length == 2)
                        ReceiveDecontamination(parts[1]);
                    break;
                case "A":
                    if (!IsLaboratoryPresentationAllowed())
                    {
                        RejectLaboratoryPresentationOutsideLabs("network announcement packet");
                        break;
                    };
                    HandleNetworkAnnouncement(parts);
                    break;
                case "P":
                    if (!IsLaboratoryPresentationAllowed())
                    {
                        RejectLaboratoryPresentationOutsideLabs("network pre-blackout packet");
                        break;
                    }

                    HandleNetworkPreBlackout(parts);
                    break;
                case "R":
                    HandleNetworkRadio(parts);
                    break;
                case "B":
                    if (!IsLaboratoryPresentationAllowed())
                    {
                        RejectLaboratoryPresentationOutsideLabs("network blackout packet");
                        break;
                    };
                    if (parts.Length >= 3)
                    {
                        bool restrict = parts[1] == "1";
                        bool disableDoors = parts[2] == "1";
                        bool manualTransition = parts.Length >= 4 && parts[3] == "1";
                        _networkRestrictExfils = restrict;
                        _networkDisableAutomaticDoors = disableDoors;
                        _pendingNetworkManualBlackoutTransition = manualTransition;
                        _pendingNetworkBlackout = true;
                        TryApplyPendingNetworkBlackout();
                    }

                    break;
                case "E":
                    if (IsLaboratoryPresentationAllowed())
                    {
                        _blackoutApplied = false;
                        _takeoverFacilityRestored = true;
                        _manualShutdownCompleted = false;
                        if (CanPresentLocally)
                        {
                            StartCoroutine(PlayTakeoverPowerRestorationSound());
                            RestoreTakeoverFacilityLocal();
                        }

                        ActivateManualShutdownControls();
                    }
                    else
                    {
                        RejectLaboratoryPresentationOutsideLabs("network power restoration sound packet");
                    }

                    break;
                case "J":
                    if (_raidisActive && IsLaboratoryPresentationAllowed() && parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int emergencyMask) && emergencyMask >= 0 && emergencyMask <= 7)
                    {
                        _emergencyElevatorMask = emergencyMask;
                        SyncBlackoutSafetyLocal();
                    }

                    break;
                case "G":
                    if (IsLaboratoryPresentationAllowed())
                        ApplyNetworkGateBClosed();
                    else
                        RejectLaboratoryPresentationOutsideLabs("network Gate B packet");
                    break;
                case "S":
                    HandleFikaSnapshot(parts);
                    break;
                case "U":
                    HandleNetworkTakeoverState(parts);
                    break;
                case "V":
                    HandleNetworkFacilityControlState(parts);
                    break;
            }
        }

        private void TryApplyPendingNetworkBlackout()
        {
            if (!_pendingNetworkBlackout || _blackoutApplied || !IsLaboratoryPresentationAllowed())
            {
                return;
            }

            if (IsFikaNetworkSession && !IsFikaPresentationWindowOpen())
                return;
            _pendingNetworkBlackout = false;
            bool manualTransition = _pendingNetworkManualBlackoutTransition;
            _pendingNetworkManualBlackoutTransition = false;
            _blackoutRoutine = StartCoroutine(DisableLaboratoryBlackout(true, _networkRestrictExfils, _networkDisableAutomaticDoors, manualTransition));
            if (!_blackoutTransitionInProgress)
                _blackoutRoutine = null;
        }

        private void HandleNetworkAnnouncement(string[] parts)
        {
            if (parts.Length < 4 || !IsLaboratoryPresentationAllowed())
                return;
            string clipName = DecodeNetworkString(parts[1]);
            if (!TryParseDouble(parts[2], out double startTime) || !TryParseFloat(parts[3], out float clipLength))
            {
                return;
            }

            StartNetworkAnnouncement(clipName, startTime, clipLength);
        }

        private void HandleNetworkPreBlackout(string[] parts)
        {
            if (parts.Length < 4 || !IsLaboratoryPresentationAllowed())
                return;
            string clipName = DecodeNetworkString(parts[1]);
            if (!TryParseDouble(parts[2], out double startTime) || !TryParseFloat(parts[3], out float clipLength))
            {
                return;
            }

            double elapsed = Math.Max(0d, NetworkBridge.NetworkTime - startTime);
            if (clipLength > 0f && elapsed >= clipLength)
                return;
            StartCoroutine(PlayNetworkPreBlackout(clipName, (float)elapsed));
        }

        private void HandleNetworkRadio(string[] parts)
        {
            if (parts.Length < 8 || !_raidisActive)
                return;
            string category = parts[1];
            if (!IsRadioCategoryEnabled(category))
                return;
            string clipName = DecodeNetworkString(parts[2]);
            string profileId = DecodeNetworkString(parts[3]);
            if (!TryParseFloat(parts[4], out float x) || !TryParseFloat(parts[5], out float y) || !TryParseFloat(parts[6], out float z) || !TryParseDouble(parts[7], out double startTime))
            {
                return;
            }

            StartCoroutine(PlayNetworkRadio(category, clipName, profileId, new Vector3(x, y, z), startTime));
        }

        private bool _sceneryRequestLogged;
        private void TrySendFikaSceneryRequest()
        {
            if (!IsFikaNetworkSession || IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed() || Time.realtimeSinceStartup < _nextFikaSceneryRequest)
                return;
            _nextFikaSceneryRequest = Time.realtimeSinceStartup + (_fikaSceneryStateReceived ? 10f : 2f);
            if (!_sceneryRequestLogged)
            {
                _sceneryRequestLogged = true;
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Fika: Requesting host scenery state; retrying until the first snapshot arrives.");
                }
            }

            NetworkBridge.SendToServer("Q|SCENERY|" + EncodeNetworkString(_currentRaidLocation) + "|" + LocalEventContent);
        }

        private void TrySendFikaClientReady()
        {
            if (!IsFikaNetworkSession || IsEventAuthority || !CanPresentLocally || _fikaClientReadyAcknowledged || !_raidisActive || !_announcementCacheReady || !IsFikaPresentationWindowOpen())
            {
                return;
            }

            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null || Singleton<GameWorld>.Instance.MainPlayer == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup < _nextFikaReadySendTime)
                return;
            _nextFikaReadySendTime = Time.realtimeSinceStartup + 2f;
            string clientLocation = ResolveRaidLocation(Singleton<GameWorld>.Instance);
            string readyPayload = "Q|READY|" + EncodeNetworkString(clientLocation) + "|" + LocalEventContent;
            NetworkBridge.SendToServer(readyPayload);
        }

        private bool IsFikaPresentationWindowOpen()
        {
            if (!IsFikaNetworkSession)
                return true;
            if (!NetworkBridge.RaidStarted && Singleton<IBotGame>.Instance?.Status != GameStatus.Started)
                return false;
            if (_fikaRaidStartedObservedAt < 0f)
                _fikaRaidStartedObservedAt = Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - _fikaRaidStartedObservedAt >= FIKA_PRESENTATION_GRACE_SECONDS;
        }

        private bool IsLocalPresentationReadyForAuthority()
        {
            if (IsHeadless)
                return true;
            if (!CanPresentLocally || !_announcementCacheReady || !IsFikaPresentationWindowOpen())
            {
                return false;
            }

            return Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null && Singleton<GameWorld>.Instance.MainPlayer != null;
        }

        private void HandleFikaClientReady(string[] parts)
        {
            if (!_raidisActive || parts.Length < 2)
                return;
            bool sceneryOnly = string.Equals(parts[1], "SCENERY", StringComparison.Ordinal);
            if (!sceneryOnly && !string.Equals(parts[1], "READY", StringComparison.Ordinal))
                return;
            string peerContent = parts.Length >= 4 ? parts[3] : "legacy-client";
            CheckEventContent(peerContent);
            if (LocalEventContent != "pending" && peerContent != "pending")
                NetworkBridge.Broadcast("CV|" + peerContent + "|" + LocalEventContent);
            string reportedLocation = parts.Length >= 3 ? DecodeNetworkString(parts[2]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(reportedLocation))
            {
                _fikaReportedRaidLocation = reportedLocation.Trim();
                if (IsHeadless)
                {
                    _currentRaidLocation = _fikaReportedRaidLocation;
                    _fakeblackoutisEnabled = IsLaboratoryLocation(_currentRaidLocation);
                }
            }

            if (sceneryOnly)
            {
                if (IsLaboratoryPresentationAllowed())
                {
                    if (!_blackoutChanceResolved)
                        PrepareLabsFacilityControlDecisionAuthority();
                }

                NetworkBridge.Broadcast(BuildFikaSnapshot());
                return;
            }

            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Fika: Client READY received; sending current event state. location=" + reportedLocation);
            }

            _fikaPresentationClientReady = true;
            NetworkBridge.Broadcast("K|READY");
            NetworkBridge.Broadcast(BuildFikaSnapshot());
        }

        private IEnumerator WaitForFikaPresentationReadiness()
        {
            if (!IsFikaNetworkSession || !IsFikaServer)
                yield break;
            while (_raidisActive)
            {
                bool localReady = IsLocalPresentationReadyForAuthority();
                bool remotePlayerExpected = _fikaRemotePeerSeen || !NetworkBridge.IsSinglePlayer;
                if (localReady && !IsHeadless && remotePlayerExpected && !_fikaPresentationClientReady && _fikaRemoteReadyFallbackAt < 0f)
                {
                    _fikaRemoteReadyFallbackAt = Time.realtimeSinceStartup + FIKA_REMOTE_READY_TIMEOUT_SECONDS;
                }

                bool remoteTimeoutOpen = !IsHeadless && _fikaRemoteReadyFallbackAt >= 0f && Time.realtimeSinceStartup >= _fikaRemoteReadyFallbackAt;
                bool remoteReady = IsHeadless ? _fikaPresentationClientReady : (!remotePlayerExpected || _fikaPresentationClientReady || remoteTimeoutOpen);
                if (remoteTimeoutOpen && !_fikaPresentationClientReady)
                {
                    Log.LogWarning("ISB SOF Extras: Remote presentation READY timed out; " + "the player-host will continue locally and late clients will receive a snapshot. Check that every client installed the complete ISB client package, including DLLs and event assets.");
                    _fikaRemotePeerSeen = false;
                    _fikaRemoteReadyFallbackAt = -1f;
                    remoteReady = true;
                }

                if (localReady && remoteReady)
                {
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        Log.LogInfo("ISB SOF Fika: Presentation start gate opened. localReady=" + localReady + ", remoteReady=" + remoteReady + ".");
                    }

                    yield break;
                }

                yield return new WaitForSeconds(0.25f);
            }
        }

        private void HandleFikaSnapshot(string[] parts)
        {
            if (parts.Length < 8 || !_raidisActive)
                return;
            bool blackout = parts[1] == "1";
            bool gateB = parts[2] == "1";
            bool restrict = parts[3] == "1";
            bool disableDoors = parts[4] == "1";
            string clipName = DecodeNetworkString(parts[5]);
            if (!IsLaboratoryPresentationAllowed())
            {
                return;
            }

            if (!TryParseDouble(parts[6], out double startTime) || !TryParseFloat(parts[7], out float clipLength))
            {
                return;
            }

            _networkRestrictExfils = restrict;
            _networkDisableAutomaticDoors = disableDoors;
            if (blackout && !_blackoutApplied && _fakeblackoutisEnabled)
            {
                _pendingNetworkBlackout = true;
                TryApplyPendingNetworkBlackout();
            }

            if (gateB)
                ApplyNetworkGateBClosed();
            if (parts.Length >= 15)
            {
                if (!_fikaSceneryStateReceived)
                {
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        Log.LogInfo("ISB SOF Fika: First scenery snapshot received; periodic recovery snapshots remain enabled.");
                    }
                }

                _fikaSceneryStateReceived = true;
                int alarmChoice;
                if (parts.Length > 41 && int.TryParse(parts[41], NumberStyles.Integer, CultureInfo.InvariantCulture, out alarmChoice))
                    ReceiveTakeoverAlarmChoice(alarmChoice);
                else if (!TakeoverAlarmChoice.Valid(_takeoverAlarmChoice))
                    ReceiveTakeoverAlarmChoice(0);
                bool takeoverActive = parts[8] == "1";
                bool alarmStarted = parts[9] == "1";
                bool alarmDisabled = parts[10] == "1";
                bool gatesOpened = parts[11] == "1";
                bool powerA = parts[12] == "1";
                bool powerB = parts[13] == "1";
                bool facilityRestored = parts[14] == "1";
                ApplyNetworkTakeoverState(takeoverActive, alarmStarted, alarmDisabled, gatesOpened, powerA, powerB, facilityRestored, parts.Length > 19 && parts[19] == "1", parts.Length > 20 && parts[20] == "1", parts.Length > 21 && parts[21] == "1", parts.Length > 22 && parts[22] == "1", parts.Length > 23 && parts[23] == "1", parts.Length > 27 && parts[27] == "1", parts.Length > 28 && parts[28] == "1", parts.Length > 29 && parts[29] == "1", parts.Length > 30 ? parts[30] == "1" : alarmStarted, parts.Length > 31 && parts[31] == "1");
            }

            if (parts.Length >= 19 && int.TryParse(parts[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int modeValue))
            {
                ApplyNetworkFacilityControlState((LabsFacilityControlMode)modeValue, parts[16] == "1", parts[17] == "1", parts[18] == "1", IsShutdownControlMode((LabsFacilityControlMode)modeValue) ? parts.Length > 24 && parts[24] == "1" : parts.Length > 19 && parts[19] == "1", IsShutdownControlMode((LabsFacilityControlMode)modeValue) ? parts.Length > 25 && parts[25] == "1" : parts.Length > 20 && parts[20] == "1");
            }

            if (parts.Length > 26 && int.TryParse(parts[26], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mask) && mask >= 0 && mask <= 7)
                _emergencyElevatorMask = mask;
            SyncBlackoutSafetyLocal();
            if (parts.Length > 37)
                ReceiveOfficeSnapshot(parts[37]);
            ApplyOfficePause();
            if (parts.Length > 32)
                ReceiveDecontamination(parts[32]);
            if (parts.Length > 33)
                ReceiveDecontaminationEventState(parts[33]);
            if (parts.Length > 34)
                LabsKeycardLockdown.ApplyBatterySnapshot(DecodeNetworkString(parts[34]));
            if (parts.Length > 35)
                ReceiveDecontaminationVideo(parts[35]);
            if (parts.Length > 36)
                ReceiveSharedAuthorizationEffects(parts[36]);
            if (parts.Length > 38)
                ReceiveFacilitySequence(parts[38]);
            SetAnnouncementMutes(parts.Length > 39 && parts[39] == "1", parts.Length > 40 && parts[40] == "1");
            if (!string.IsNullOrEmpty(clipName))
                StartNetworkAnnouncement(clipName, startTime, clipLength);
        }

        private void StartNetworkAnnouncement(string clipName, double startTime, float clipLength)
        {
            if (DeadManActive)
                return;
            if (!_enableAnnouncements.Value || string.IsNullOrEmpty(clipName) || !IsLaboratoryPresentationAllowed())
            {
                return;
            }

            bool nightmare = IsNightmareAnnouncement(clipName);
            bool clockReady = !nightmare || _scp173?.ClockReady == true;
            double elapsed = clockReady ? Math.Max(0d, AnnouncementNetworkClock(clipName) - startTime) : 0d;
            if (clockReady && clipLength > 0f && elapsed >= clipLength)
                return;
            string eventKey = IsDecontaminationClip(clipName) ? clipName : clipName + "@" + startTime.ToString("R", CultureInfo.InvariantCulture);
            if (string.Equals(eventKey, _lastReceivedAnnouncementKey, StringComparison.Ordinal))
                return;
            _lastReceivedAnnouncementKey = eventKey;
            StartCoroutine(PlayNetworkAnnouncement(clipName, (float)elapsed, nightmare ? startTime : -1d));
        }

        private IEnumerator PlayNetworkAnnouncement(string clipName, float startOffset, double nightmareStart = -1d)
        {
            if (DeadManActive)
                yield break;
            if (nightmareStart >= 0d)
            {
                float deadline = Time.realtimeSinceStartup + 15f;
                while (_raidisActive && !DeadManActive && _scp173?.ClockReady != true && Time.realtimeSinceStartup < deadline) yield return null;
                if (!_raidisActive || DeadManActive) yield break;
                if (_scp173?.ClockReady != true)
                {
                    Log.LogError("ISB Nightmare announcement unavailable: authoritative clock was not received.");
                    yield break;
                }
            }
            double resolveStarted = AnnouncementClock(clipName);
            AudioClip clip = null;
            yield return StartCoroutine(ResolveNetworkClip(clipName, null, value => clip = value));
            if (DeadManActive)
                yield break;
            while (_raidisActive && !DeadManActive && IsDecontaminationClip(clipName) && DecontaminationPaused)
                yield return null;
            if (!_raidisActive || DeadManActive)
                yield break;
            startOffset = nightmareStart >= 0d ? (float)Math.Max(0d, NightmareClock - nightmareStart) : startOffset + (float)Math.Max(0d, AnnouncementClock(clipName) - resolveStarted);
            if (!IsAudioClipReady(clip) || startOffset >= clip.length)
            {
                Log.LogWarning("ISB SOF Fika: Could not resolve synchronized announcement clip '" + clipName + "'.");
                yield break;
            }

            if (ShouldRouteThroughLaboratoryPa(clip.name))
            {
                yield return RouteBlackoutClipIntoLaboratoryPa(clip, startOffset);
                yield break;
            }

            yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), AnnouncementMinDistance, AnnouncementMaxDistance, AnnouncementBaseVolume, _announcementsVolume, true, startOffset, false, false, AnnouncementSpatialBlend, AnnouncementRolloffMode);
        }

        private IEnumerator PlayNetworkRadio(string category, string clipName, string profileId, Vector3 fallbackPosition, double startTime)
        {
            AudioClip clip = null;
            yield return StartCoroutine(ResolveNetworkClip(clipName, category, value => clip = value));
            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning("ISB SOF Fika: Could not resolve synchronized radio clip '" + clipName + "' for category '" + category + "'.");
                yield break;
            }

            float startOffset = (float)Math.Max(0d, NetworkBridge.NetworkTime - startTime);
            if (startOffset >= clip.length)
                yield break;
            Player player = FindPlayerByProfileId(profileId);
            bool portable = PortableRadioTiming.IsPortableCategory(category);
            if (portable && !HasEquippedPortableRadio(player))
                yield break;
            Transform parent = null;
            Vector3 position = fallbackPosition;
            if (player != null && player.PlayerBones != null && player.PlayerBones.RootJoint != null)
            {
                parent = player.PlayerBones.RootJoint;
                position = Vector3.up * (portable ? 1.2f : 1.5f);
            }

            yield return PlayISBSoldiersRadio(clip, parent, position, portable ? PortableRadioMinDistance : RADIO_MIN_DISTANCE, portable ? PortableRadioMaxDistance : RADIO_MAX_DISTANCE, portable ? PortableRadioVolume : RADIO_BASE_VOLUME, _radioVolume, false, startOffset, false, false, RADIO_SPATIAL_BLEND, RADIO_ROLLOFF_MODE, portable ? (Func<bool>)(() => _raidisActive && HasEquippedPortableRadio(player) && IsRadioCategoryEnabled(category)) : null);
        }

        private IEnumerator ResolveNetworkClip(string clipName, string category, Action<AudioClip> onResolved)
        {
            AudioClip clip = string.IsNullOrEmpty(category) ? FindCachedClipByName(clipName) : FindCachedRadioClip(category, clipName);
            if (!IsAudioClipReady(clip))
            {
                string path = string.IsNullOrEmpty(category) ? Path.Combine(_dataFolder, clipName + ".wav") : FindAudioPathByClipName(GetAudioPathsForCategory(category), clipName);
                if (string.IsNullOrEmpty(category) && !File.Exists(path))
                    path = Path.Combine(_dataFolder, clipName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    yield return StartCoroutine(LoadAudioClip(path, value => clip = value));
                    if (!string.IsNullOrEmpty(category))
                        AddClipToCategoryList(category, clip);
                }
            }

            onResolved?.Invoke(clip);
        }

        private void ApplyNetworkGateBClosed()
        {
            if (_gateBClosed || _takeoverGatesOpened || !IsLaboratoryPresentationAllowed())
                return;
            _gateBClosed = BlackoutBridge.DisableAndHideExfil(BlackoutBridge.GateBExfilName, Log);
        }

        private double _deconAnnouncementStart = -1d;
        private double SnapshotAnnouncementStart => IsDecontaminationClip(_currentNetworkAnnouncement) && _deconAnnouncementStart >= 0d ? SynchronizedTime - Math.Max(0d, DecontaminationClock - _deconAnnouncementStart) : _currentNetworkAnnouncementStart;

        private string BuildFikaSnapshot()
        {
            bool labsPresentation = IsLaboratoryPresentationAllowed();
            return string.Join("|", new[] { "S", labsPresentation && _blackoutApplied ? "1" : "0", labsPresentation && _gateBClosed ? "1" : "0", labsPresentation && _networkRestrictExfils ? "1" : "0", labsPresentation && _networkDisableAutomaticDoors ? "1" : "0", EncodeNetworkString(labsPresentation ? _currentNetworkAnnouncement : string.Empty), (labsPresentation ? SnapshotAnnouncementStart : 0d).ToString("R", CultureInfo.InvariantCulture), (labsPresentation ? _currentNetworkAnnouncementLength : 0f).ToString("R", CultureInfo.InvariantCulture), labsPresentation && _labsPreBlackoutEventActive ? "1" : "0", labsPresentation && _takeoverAlarmStarted ? "1" : "0", labsPresentation && _takeoverAlarmDisabled ? "1" : "0", labsPresentation && _takeoverGatesOpened ? "1" : "0", labsPresentation && _takeoverPowerA ? "1" : "0", labsPresentation && _takeoverPowerB ? "1" : "0", labsPresentation && _takeoverFacilityRestored ? "1" : "0", labsPresentation ? ((int)_facilityControlMode).ToString(CultureInfo.InvariantCulture) : "0", labsPresentation && (IsShutdownControlMode(_facilityControlMode) ? _manualShutdownA : _takeoverPowerA) ? "1" : "0", labsPresentation && (IsShutdownControlMode(_facilityControlMode) ? _manualShutdownB : _takeoverPowerB) ? "1" : "0", labsPresentation && (IsShutdownControlMode(_facilityControlMode) ? _manualShutdownCompleted : _takeoverFacilityRestored) ? "1" : "0", labsPresentation && _takeoverPowerC ? "1" : "0", labsPresentation && _takeoverPowerD ? "1" : "0", labsPresentation && _takeoverGateA ? "1" : "0", labsPresentation && _takeoverGateB ? "1" : "0", labsPresentation && _takeoverGateC ? "1" : "0", labsPresentation && _manualShutdownC ? "1" : "0", labsPresentation && _manualShutdownD ? "1" : "0", (labsPresentation ? _emergencyElevatorMask : 0).ToString(CultureInfo.InvariantCulture), _disableTakeoverFire ? "1" : "0", _disableTakeoverSirens ? "1" : "0", _disableTakeoverDestroyedVehicles ? "1" : "0", labsPresentation && _takeoverEmergencyLightsStarted ? "1" : "0", labsPresentation && _takeoverEmergencyLightsDisabled ? "1" : "0", labsPresentation ? GetDecontaminationSnapshot() : string.Empty, labsPresentation ? GetDecontaminationEventState() : "00", labsPresentation ? EncodeNetworkString(LabsKeycardLockdown.BatterySnapshot()) : string.Empty, labsPresentation ? GetDecontaminationVideoSnapshot() : "-1", (labsPresentation ? _authorizationQuestActions.Mask : 0).ToString(CultureInfo.InvariantCulture), labsPresentation ? GetOfficeSnapshot() : string.Empty, labsPresentation ? GetFacilitySequenceSnapshot() : string.Empty, labsPresentation && _announcementSwitches.MapMuted ? "1" : "0", labsPresentation && _announcementSwitches.DecontaminationMuted ? "1" : "0", (labsPresentation ? _takeoverAlarmChoice : -1).ToString(CultureInfo.InvariantCulture) });
        }

        private void BroadcastFikaPreBlackout(string clipName, float clipLength)
        {
            if (!IsFikaServer || string.IsNullOrEmpty(clipName) || !IsLaboratoryPresentationAllowed())
                return;
            double startTime = SynchronizedTime;
            NetworkBridge.Broadcast(string.Join("|", new[] { "P", EncodeNetworkString(clipName), startTime.ToString("R", CultureInfo.InvariantCulture), Mathf.Max(clipLength, 0.05f).ToString("R", CultureInfo.InvariantCulture) }));
        }

        private void BroadcastFikaAnnouncement(string clipName, float clipLength, double scheduledStart = -1d)
        {
            if (string.IsNullOrEmpty(clipName) || !IsLaboratoryPresentationAllowed())
                return;
            NightmareAnnouncementStarted(clipName, clipLength);
            _currentNetworkAnnouncement = clipName;
            _currentNetworkAnnouncementStart = IsNightmareAnnouncement(clipName) ? NightmareState.announcementStart : DecontaminationVideoTimeline.ValidStart(scheduledStart) ? scheduledStart : SynchronizedTime;
            _currentNetworkAnnouncementLength = Mathf.Max(clipLength, 0.05f);
            _deconAnnouncementStart = IsDecontaminationClip(clipName) ? _currentNetworkAnnouncementStart - (_officeAccess?.pausedSeconds ?? 0d) - NightmareDecontaminationDelay : -1d;
            if (!IsFikaServer)
                return;
            bool sent = NetworkBridge.Broadcast(string.Join("|", new[] { "A", EncodeNetworkString(clipName), _currentNetworkAnnouncementStart.ToString("R", CultureInfo.InvariantCulture), _currentNetworkAnnouncementLength.ToString("R", CultureInfo.InvariantCulture) }));
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Fika: Announcement broadcast '" + clipName + "' sent=" + sent + ", start=" + _currentNetworkAnnouncementStart.ToString("F3", CultureInfo.InvariantCulture) + ", duration=" + _currentNetworkAnnouncementLength.ToString("F2", CultureInfo.InvariantCulture) + ".");
            }
        }

        private void BroadcastFikaRadio(string clipName, Player bot, string category)
        {
            if (!IsFikaServer || string.IsNullOrEmpty(clipName) || bot == null || string.IsNullOrEmpty(category))
            {
                return;
            }

            Vector3 position = bot.Position;
            double startTime = SynchronizedTime;
            bool sent = NetworkBridge.Broadcast(string.Join("|", new[] { "R", category, EncodeNetworkString(clipName), EncodeNetworkString(bot.ProfileId), position.x.ToString("R", CultureInfo.InvariantCulture), position.y.ToString("R", CultureInfo.InvariantCulture), position.z.ToString("R", CultureInfo.InvariantCulture), startTime.ToString("R", CultureInfo.InvariantCulture) }));
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Fika: Radio broadcast category=" + category + ", clip='" + clipName + "', sent=" + sent + ", profile='" + bot.ProfileId + "'.");
            }
        }

        private Player FindPlayerByProfileId(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null || Singleton<GameWorld>.Instance.AllAlivePlayersList == null)
            {
                return null;
            }

            foreach (Player player in Singleton<GameWorld>.Instance.AllAlivePlayersList)
            {
                if (player != null && string.Equals(player.ProfileId, profileId, StringComparison.Ordinal))
                    return player;
            }

            return null;
        }

        private static string EncodeNetworkString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string DecodeNetworkString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private void ClearNetworkPresentationState()
        {
            _authorizationQuestActions.Clear();
            _authorizationQuestErrorReported = false;
            _networkRestrictExfils = false;
            _networkDisableAutomaticDoors = false;
            _pendingNetworkBlackout = false;
            _pendingNetworkManualBlackoutTransition = false;
            _deconAnnouncementStart = -1d;
            _currentNetworkAnnouncement = string.Empty;
            _currentNetworkAnnouncementStart = 0d;
            _currentNetworkAnnouncementLength = 0f;
            _lastReceivedAnnouncementKey = string.Empty;
        }

        private void ResetFikaHandshake(bool expectRemotePeer)
        {
            _fikaSceneryStateReceived = false;
            _sceneryRequestLogged = false;
            _contentPeersReported.Clear();
            _contentWarningDisplayed = false;
            _nextFikaSceneryRequest = 0f;
            _fikaClientReadyAcknowledged = false;
            _fikaPresentationClientReady = false;
            _nextFikaReadySendTime = 0f;
            _fikaReportedRaidLocation = string.Empty;
            _fikaRaidStartedObservedAt = -1f;
            _fikaRemoteReadyFallbackAt = -1f;
            _fikaRemotePeerSeen = expectRemotePeer;
        }
    }
}
