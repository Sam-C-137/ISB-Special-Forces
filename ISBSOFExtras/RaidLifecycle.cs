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
        private string ResolveRaidLocation(GameWorld gameWorld)
        {
            if (gameWorld == null)
                return string.Empty;
            if (IsHeadless && ReferenceEquals(gameWorld, _activeRaidWorld) && !string.IsNullOrWhiteSpace(_fikaReportedRaidLocation))
                return _fikaReportedRaidLocation;
            try
            {
                if (gameWorld.MainPlayer != null && !string.IsNullOrWhiteSpace(gameWorld.MainPlayer.Location))
                {
                    return gameWorld.MainPlayer.Location;
                }
            }
            catch
            {
            }

            try
            {
                Type worldType = gameWorld.GetType();
                string[] memberNames =
                {
                    "LocationId",
                    "Location",
                    "locationId",
                    "location"
                };
                foreach (string memberName in memberNames)
                {
                    PropertyInfo property = worldType.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null && property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(gameWorld, null) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }

                    FieldInfo field = worldType.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(gameWorld) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (gameWorld.AllAlivePlayersList != null)
                {
                    foreach (Player player in gameWorld.AllAlivePlayersList)
                    {
                        if (player != null && !string.IsNullOrWhiteSpace(player.Location))
                            return player.Location;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsLaboratoryLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return false;
            return string.Equals(location.Trim(), targetmap, StringComparison.OrdinalIgnoreCase)
                || string.Equals(location.Trim(), "5b0fc42d86f7744a585f9105", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveHeadlessRaidLocation(GameWorld gameWorld, string initial)
        {
            if (!string.IsNullOrWhiteSpace(_fikaReportedRaidLocation)) return _fikaReportedRaidLocation;
            string live = ResolveRaidLocation(gameWorld);
            if (!string.IsNullOrWhiteSpace(live)) return live;
            return !string.IsNullOrWhiteSpace(_fikaReportedRaidLocation) ? _fikaReportedRaidLocation : initial;
        }

        private bool IsLaboratoryWorld(GameWorld gameWorld, string resolvedLocation)
        {
            if (!string.IsNullOrWhiteSpace(resolvedLocation))
                return IsLaboratoryLocation(resolvedLocation);
            return IsHeadless && IsLaboratoryLocation(_fikaReportedRaidLocation);
        }

        private bool IsActiveRaidWorldCurrent()
        {
            if (!_raidisActive || _activeRaidWorld == null)
                return false;
            return Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null && ReferenceEquals(Singleton<GameWorld>.Instance, _activeRaidWorld);
        }

        private void InvalidateStaleRaidWorldIfNeeded()
        {
            if (!_raidisActive || IsActiveRaidWorldCurrent())
                return;
            Log.LogWarning("ISB SOF Extras: Active GameWorld changed or was destroyed; clearing the previous raid context before handling the next map. " + "previousLocation='" + (_currentRaidLocation ?? string.Empty) + "'.");
            ShutdownRaid();
        }

        private bool IsLaboratoryPresentationAllowed()
        {
            if (!_raidisActive || !_fakeblackoutisEnabled || !IsActiveRaidWorldCurrent())
                return false;
            string liveLocation = ResolveRaidLocation(_activeRaidWorld);
            if (!string.IsNullOrWhiteSpace(liveLocation))
            {
                _currentRaidLocation = liveLocation.Trim();
                if (!IsLaboratoryLocation(_currentRaidLocation))
                {
                    _fakeblackoutisEnabled = false;
                    return false;
                }

                return true;
            }

            return IsLaboratoryLocation(_currentRaidLocation) || (IsHeadless && IsLaboratoryLocation(_fikaReportedRaidLocation));
        }

        private void RejectLaboratoryPresentationOutsideLabs(string source)
        {
            _fakeblackoutisEnabled = false;
            ClearNetworkPresentationState();
            Log.LogWarning("ISB SOF Extras: Ignored Labs-only presentation outside Laboratory. source=" + source + ", location='" + (_currentRaidLocation ?? string.Empty) + "'.");
        }

        private void ClearRaidCollections()
        {
            ResetLaboratoryPaChannels();
            _botAudioPaths.Clear();
            _deathAudioPaths.Clear();
            _heavyDamageAudioPaths.Clear();
            _AishiBotAudioPaths.Clear();
            _AishiDeathAudioPaths.Clear();
            _AishiHeavyDamageAudioPaths.Clear();
            _whiteTusksCommonBotAudioPaths.Clear();
            _whiteTusksCommonDeathAudioPaths.Clear();
            _whiteTusksCommonHeavyDamageAudioPaths.Clear();
            _whiteTuskAishiBotAudioPaths.Clear();
            _whiteTuskAishiDeathAudioPaths.Clear();
            _whiteTuskAishiHeavyDamageAudioPaths.Clear();
            _wavsofBots.Clear();
            _wavsofDeaths.Clear();
            _wavsofHeavyDamage.Clear();
            _wavsofAishiBots.Clear();
            _wavsofAishiDeaths.Clear();
            _wavsofAishiHeavyDamage.Clear();
            _wavsofWhiteTusksCommonBots.Clear();
            _wavsofWhiteTusksCommonDeaths.Clear();
            _wavsofWhiteTusksCommonHeavyDamage.Clear();
            _wavsofWhiteTuskAishiBots.Clear();
            _wavsofWhiteTuskAishiDeaths.Clear();
            _wavsofWhiteTuskAishiHeavyDamage.Clear();
            _lastRadioPathByCategory.Clear();
            _listofactiveBots.Clear();
            _deathSubscribedBots.Clear();
            _lastKnownHealth.Clear();
        }

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine == null)
                return;
            StopCoroutine(routine);
            routine = null;
        }

        private IEnumerator WaitForRaidInitialization(GameWorld gameWorld, string location)
        {
            float readyAt = Time.realtimeSinceStartup + (FikaActive ? 2f : 20f);
            bool checkedStatus = false;
            bool isLabs = IsLaboratoryWorld(gameWorld, location);
            var wait = new WaitForSecondsRealtime(0.25f);
            while (Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld) && Time.realtimeSinceStartup < readyAt)
            {
                if (!checkedStatus && isLabs && (!FikaActive || NetworkBridge.Ready) && SceneManager.GetSceneByName("Laboratory_Collector_Exit").isLoaded)
                {
                    checkedStatus = true;
                    if (IsEventAuthority && LoadLabsPreBlackoutEventStatus())
                        PrepareTakeoverScenery(gameWorld);
                }

                yield return wait;
            }
        }

        private IEnumerator RaidMonitor()
        {
            while (true)
            {
                _takeoverFire.Dispose();
                _takeoverSirens.Dispose();
                _takeoverSceneryWorld = null;
                GameWorld gameWorld = null;
                string resolvedLocation = string.Empty;
                float nextWaitingLog = 0f;
                while (true)
                {
                    if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null)
                    {
                        gameWorld = Singleton<GameWorld>.Instance;
                        if (FikaRequired && !NetworkBridge.Active)
                        {
                            if (Time.realtimeSinceStartup >= nextWaitingLog)
                            {
                                nextWaitingLog = float.PositiveInfinity;
                                Log.LogError("ISB: Fika event synchronization is unavailable. Install the complete matching ISB client package; local event authority is disabled.");
                            }
                            yield return new WaitForSecondsRealtime(1f);
                            continue;
                        }
                        resolvedLocation = ResolveRaidLocation(gameWorld);
                        bool headlessAuthorityWaitingForClient = IsFikaServer && IsHeadless;
                        if (!string.IsNullOrWhiteSpace(resolvedLocation) || headlessAuthorityWaitingForClient)
                        {
                            break;
                        }

                        if (Time.realtimeSinceStartup >= nextWaitingLog)
                        {
                            nextWaitingLog = Time.realtimeSinceStartup + 10f;
                            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                            {
                                Log.LogInfo("ISB SOF Extras: GameWorld detected, waiting for raid location. " + "mainPlayer=" + (gameWorld.MainPlayer != null) + ", fikaActive=" + FikaActive + ", headless=" + IsHeadless + ".");
                            }
                        }
                    }

                    yield return null;
                }

                yield return WaitForRaidInitialization(gameWorld, resolvedLocation);
                if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null || !ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld))
                {
                    continue;
                }

                string refreshedLocation = ResolveRaidLocation(gameWorld);
                if (!string.IsNullOrWhiteSpace(refreshedLocation))
                    resolvedLocation = refreshedLocation;
                if (FikaActive)
                {
                    float nextNetworkLog = 0f;
                    while (FikaActive && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld) && !NetworkBridge.Ready)
                    {
                        if (Time.realtimeSinceStartup >= nextNetworkLog)
                        {
                            nextNetworkLog = Time.realtimeSinceStartup + 10f;
                            Log.LogWarning("ISB SOF Extras: Waiting for IFikaNetworkManager before starting synchronized raid logic. " + "server=" + NetworkBridge.IsServer + ", headless=" + IsHeadless + ", raidStarted=" + NetworkBridge.RaidStarted + ".");
                        }

                        yield return new WaitForSeconds(0.5f);
                    }

                    if (!Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld))
                        continue;
                }

                bool isLabs = IsLaboratoryWorld(gameWorld, resolvedLocation);
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Extras: Initial raid context resolved. location='" + resolvedLocation + "', labs=" + isLabs + ", authority=" + IsEventAuthority + ", server=" + IsFikaServer + ", headless=" + IsHeadless + ", networkReady=" + IsFikaNetworkSession + ".");
                }

                InitializeRaid(gameWorld, resolvedLocation, isLabs);
                if (IsFikaNetworkSession && IsFikaServer)
                {
                    yield return StartCoroutine(WaitForFikaPresentationReadiness());
                    if (_fikaStartupFailed)
                    {
                        ShutdownRaid();
                        while (Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld))
                            yield return null;
                        continue;
                    }
                    if (IsHeadless)
                    {
                        string clientReportedLocation = ResolveHeadlessRaidLocation(gameWorld, resolvedLocation);
                        if (!string.IsNullOrWhiteSpace(clientReportedLocation))
                            resolvedLocation = clientReportedLocation;
                        isLabs = IsLaboratoryWorld(gameWorld, resolvedLocation);
                        _currentRaidLocation = resolvedLocation?.Trim() ?? string.Empty;
                        _fakeblackoutisEnabled = isLabs;
                        if (isLabs)
                        {
                            LoadLabsAnnouncements();
                            LoadPreBlackoutSequences();
                            _labsPreBlackoutEventActive = LoadLabsPreBlackoutEventStatus();
                            TakeoverFlameTrigger.EventActive = _labsPreBlackoutEventActive;
                            if (_labsPreBlackoutEventActive)
                                PrepareTakeoverScenery(gameWorld);
                            LabsKeycardLockdown.Prepare();
                            LabsKeycardLockdown.ConfigureBatteryNetwork(() => IsEventAuthority, payload => NetworkBridge.SendToServer(payload), payload =>
                            {
                                if (IsFikaServer)
                                    NetworkBridge.Broadcast(payload);
                            }, () => ConfirmSharedAuthorizationEffect("READER_REPAIR"));
                            if (_decontamination == null)
                                InitializeDecontamination();
                        }

                        if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                        {
                            Log.LogInfo("ISB SOF Extras: Headless raid context finalized after post-raid-start client READY. " + "location='" + resolvedLocation + "', labs=" + isLabs + ".");
                        }
                    }
                }

                if (!Singleton<GameWorld>.Instantiated || !_raidisActive)
                    continue;
                if (IsEventAuthority && IsLaboratoryPresentationAllowed())
                    PrepareLabsFacilityControlDecisionAuthority();
                if (IsFikaNetworkSession && IsFikaServer)
                    NetworkBridge.Broadcast(BuildFikaSnapshot());
                if (IsEventAuthority)
                {
                    _radiorout = StartCoroutine(ISBSpecialForces_RadiosLoop());
                    _portableRadioLoop = StartCoroutine(PortableRadioLoop());
                    _heavyDamageWatcher = StartCoroutine(HeavyDamageWatcherLoop());
                    if (IsLaboratoryPresentationAllowed())
                    {
                        if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                        {
                            Log.LogInfo("ISB SOF Audio Plugin: The current raid is in the Lab, enabling the authoritative announcement module via the intercom system. ^-^");
                        }

                        _scanner = StartCoroutine(ScanForMainSequence());
                    }
                }

                while (_raidisActive && Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null && ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld))
                {
                    yield return null;
                }

                if (_raidisActive)
                    ShutdownRaid();
            }
        }

        private void InitializeRaid(GameWorld gameWorld, string resolvedLocation, bool isLabs)
        {
            DestroyAudioClipCache();
            _activeRaidWorld = gameWorld;
            _raidisActive = true;
            _currentRaidLocation = resolvedLocation?.Trim() ?? string.Empty;
            _fakeblackoutisEnabled = isLabs;
            _sequenceStarted = false;
            _deathChatterOnCooldown = false;
            _lastBotCacheTime = 0f;
            _heavyDamageChatterOnCooldown = false;
            _heavyDamagePending = false;
            _heavyDamageTarget = null;
            _blackoutApplied = false;
            _gateBClosed = false;
            _blackoutChanceResolved = false;
            _blackoutSelectedForRaid = false;
            if (isLabs)
            {
                LoadLabsAnnouncements();
                LoadPreBlackoutSequences();
            }

            _labsPreBlackoutEventActive = isLabs && IsEventAuthority && (ReferenceEquals(_takeoverSceneryWorld, gameWorld) || LoadLabsPreBlackoutEventStatus());
            if (_labsPreBlackoutEventActive)
                PrepareTakeoverScenery(gameWorld);
            _takeoverAlarmChoice = -1;
            _takeoverAlarmStarted = false;
            _takeoverAlarmDisabled = false;
            _announcementSwitches.Reset();
            LabsNativeAnnouncements.SetUserMuted(false);
            _takeoverEmergencyLightsStarted = false;
            TakeoverFlameTrigger.EventActive = isLabs && _labsPreBlackoutEventActive;
            _takeoverEmergencyLightsDisabled = false;
            _takeoverGatesOpened = false;
            _takeoverPowerA = false;
            _takeoverPowerB = false;
            _takeoverPowerC = false;
            _takeoverPowerD = false;
            _takeoverGateA = false;
            _takeoverGateB = false;
            _takeoverGateC = false;
            _takeoverGatesAppliedLocally = false;
            _blackoutTransitionInProgress = false;
            _takeoverFacilityRestored = false;
            _takeoverPanelsInitialized = false;
            _facilityControlMode = _labsPreBlackoutEventActive ? LabsFacilityControlMode.Takeover : LabsFacilityControlMode.None;
            _manualShutdownA = false;
            _manualShutdownB = false;
            _manualShutdownC = false;
            _manualShutdownD = false;
            _manualShutdownCompleted = false;
            _labsBlackoutSpawnEventTriggered = false;
            _labsPowerRestoredSpawnEventTriggered = false;
            _labsChaosScheduled = false;
            StopRoutine(ref _labsChaosRoutine);
            StopRoutine(ref _cameraHuntRoutine);
            StopRoutine(ref _labsBlackoutSpawnEventRoutine);
            StopRoutine(ref _labsPowerRestoredSpawnEventRoutine);
            StopRoutine(ref _facilityPanelEnsureRoutine);
            StopTakeoverAlarmLocal();
            LabsTakeoverInfrastructure.Reset();
            LabsKeycardLockdown.Reset();
            LabsNativeAnnouncements.SetActive(false);
            _emergencyElevators.Reset();
            _emergencyElevatorMask = 0;
            _emergencyLighting.Dispose();
            _corpseLights.Clear();
            LabsBlackout.ResetRestorationState();
            if (isLabs)
            {
                LabsKeycardLockdown.Prepare();
                LabsKeycardLockdown.ConfigureBatteryNetwork(() => IsEventAuthority, payload => NetworkBridge.SendToServer(payload), payload =>
                {
                    if (IsFikaServer)
                        NetworkBridge.Broadcast(payload);
                }, () => ConfirmSharedAuthorizationEffect("READER_REPAIR"));
                if (CanPresentLocally)
                {
                    StartCoroutine(_emergencyLighting.Prepare());
                    StartCoroutine(LabsBlackout.PrepareMaterials());
                }
            }

            ClearNetworkPresentationState();
            ResetFikaHandshake(FikaActive && !NetworkBridge.IsSinglePlayer);
            BlackoutBridge.ResetHiddenExfils();
            StopRoutine(ref _nativeSwitchLockRoutine);
            StartNativeSwitchLockLocal();
            _audioCacheStarted = false;
            _announcementCacheReady = false;
            ClearRaidCollections();
            if (_subtitleSystem != null)
            {
                _subtitleSystem.Reload();
                _subtitleSystem.Prewarm();
            }

            IndexAudioPaths();
            CacheEligibleBots(gameWorld);
            StopRoutine(ref _audioCacheRoutine);
            InitializeDecontamination();
            _audioCacheRoutine = StartCoroutine(PreloadAudioCacheGradually());
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Audio Plugin: Raid initialized.");
            }
        }

        private void ShutdownRaid()
        {
            ResetOfficeAccess();
            DisposeDecontamination();
            StopRoutine(ref _labsChaosRoutine);
            StopRoutine(ref _cameraHuntRoutine);
            _labsChaosScheduled = false;
            _labsChaosEnabled = false;
            _disableTakeoverFire = false;
            _disableTakeoverSirens = false;
            _disableTakeoverDestroyedVehicles = false;
            _takeoverFire.Dispose();
            _takeoverSirens.Dispose();
            _takeoverSceneryWorld = null;
            _raidisActive = false;
            _activeRaidWorld = null;
            _sequenceStarted = false;
            _fakeblackoutisEnabled = false;
            _deathChatterOnCooldown = false;
            _heavyDamageChatterOnCooldown = false;
            _heavyDamagePending = false;
            _heavyDamageTarget = null;
            _audioCacheStarted = false;
            _announcementCacheReady = false;
            _labsPreBlackoutEventActive = false;
            _selectedLabsVariant = "legacy";
            _takeoverAlarmChoice = -1;
            _takeoverAlarmStarted = false;
            _takeoverAlarmDisabled = false;
            _announcementSwitches.Reset();
            LabsNativeAnnouncements.SetUserMuted(false);
            _takeoverEmergencyLightsStarted = false;
            TakeoverFlameTrigger.EventActive = false;
            _takeoverEmergencyLightsDisabled = false;
            _takeoverGatesOpened = false;
            _takeoverPowerA = false;
            _takeoverPowerB = false;
            _takeoverPowerC = false;
            _takeoverPowerD = false;
            _takeoverGateA = false;
            _takeoverGateB = false;
            _takeoverGateC = false;
            _takeoverGatesAppliedLocally = false;
            _blackoutTransitionInProgress = false;
            _takeoverFacilityRestored = false;
            _takeoverPanelsInitialized = false;
            _facilityControlMode = LabsFacilityControlMode.None;
            _manualShutdownA = false;
            _manualShutdownB = false;
            _manualShutdownC = false;
            _manualShutdownD = false;
            _manualShutdownCompleted = false;
            _labsBlackoutSpawnEventTriggered = false;
            _labsPowerRestoredSpawnEventTriggered = false;
            StopRoutine(ref _labsBlackoutSpawnEventRoutine);
            StopRoutine(ref _labsPowerRestoredSpawnEventRoutine);
            StopRoutine(ref _facilityPanelEnsureRoutine);
            StopTakeoverAlarmLocal();
            LabsTakeoverInfrastructure.Reset();
            LabsKeycardLockdown.Reset();
            LabsNativeAnnouncements.SetActive(false);
            _emergencyElevators.Reset();
            _emergencyElevatorMask = 0;
            _emergencyLighting.Dispose();
            _corpseLights.Clear();
            LabsBlackout.ResetRestorationState();
            StopRoutine(ref _scanner);
            StopRoutine(ref _radiorout);
            ResetPortableRadios();
            StopRoutine(ref _announcementRout);
            StopRoutine(ref _deathRout);
            StopRoutine(ref _heavyDamageRout);
            StopRoutine(ref _heavyDamageWatcher);
            StopRoutine(ref _audioCacheRoutine);
            StopRoutine(ref _blackoutRoutine);
            StopRoutine(ref _nativeSwitchLockRoutine);
            _blackoutApplied = false;
            _gateBClosed = false;
            _blackoutChanceResolved = false;
            _blackoutSelectedForRaid = false;
            ClearNetworkPresentationState();
            ResetFikaHandshake(false);
            _currentRaidLocation = string.Empty;
            BlackoutBridge.ResetHiddenExfils();
            if (_subtitleSystem != null)
                _subtitleSystem.Stop();
            DestroyActiveAudioObjects();
            RestoreLaboratoryPaSources();
            DestroyAudioClipCache();
            ClearRaidCollections();
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Audio Plugin: Raid ended - Disabling all modules");
            }
        }
    }
}
