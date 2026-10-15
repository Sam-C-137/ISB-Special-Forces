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
        private void PrepareLabsFacilityControlDecisionAuthority()
        {
            if (!IsEventAuthority || !IsLaboratoryPresentationAllowed())
                return;
            if (!_labsPreBlackoutEventActive)
                _labsPreBlackoutEventActive = LoadLabsPreBlackoutEventStatus();
            if (_labsPreBlackoutEventActive)
                _facilityControlMode = LabsFacilityControlMode.Takeover;
            SyncTakeoverFireLocal();
            ResolveBlackoutChanceOnce();
            if (!_labsPreBlackoutEventActive && !_blackoutApplied)
                ActivateManualShutdownControls();
        }

        private void ResolveBlackoutChanceOnce()
        {
            UpdateSpawnProgress(-1);
            if (_blackoutChanceResolved)
                return;
            if (!IsLaboratoryPresentationAllowed())
                return;
            if (_labsPreBlackoutEventActive || _selectedLabsVariant == "blackout")
            {
                _blackoutChanceResolved = true;
                _blackoutSelectedForRaid = true;
                return;
            }

            _blackoutChanceResolved = UpdateSpawnProgress(Mathf.Clamp(_laboratoryBlackoutChance.Value, 0, 100));
        }

        private IEnumerator DisableLaboratoryBlackout(bool networkForced = false, bool networkRestrictExfils = false, bool networkDisableDoors = false, bool networkManualBlackoutTransition = false)
        {
            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutRoutine = null;
                yield break;
            }

            if (_blackoutApplied)
            {
                _blackoutRoutine = null;
                yield break;
            }

            GameWorld world = _activeRaidWorld;
            _blackoutTransitionInProgress = true;
            if (CanPresentLocally)
            {
                if (_labsPreBlackoutEventActive)
                    yield return _emergencyLighting.Prepare();
                yield return LabsBlackout.PrepareMaterials();
            }

            if (!_raidisActive || world == null || !ReferenceEquals(world, _activeRaidWorld) || !ReferenceEquals(world, Singleton<GameWorld>.Instance))
            {
                _blackoutTransitionInProgress = false;
                _blackoutRoutine = null;
                yield break;
            }

            bool restrictExfils = networkForced ? networkRestrictExfils : _restrictExfilsDuringBlackout.Value;
            bool disableDoors = true;
            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutRoutine = null;
                yield break;
            }

            _networkRestrictExfils = restrictExfils;
            _networkDisableAutomaticDoors = disableDoors;
            _blackoutTransitionInProgress = true;
            _blackoutApplied = true;
            UpdateSpawnProgress(-1);
            _takeoverFacilityRestored = false;
            if (IsEventAuthority)
                _emergencyElevatorMask = 0;
            SyncBlackoutSafetyLocal();
            bool manualBlackoutTransition = networkForced ? networkManualBlackoutTransition : _manualShutdownCompleted && IsShutdownControlMode(_facilityControlMode);
            if (!networkForced && IsFikaServer)
            {
                bool blackoutSent = NetworkBridge.Broadcast("B|" + (restrictExfils ? "1" : "0") + "|" + (disableDoors ? "1" : "0") + "|" + (manualBlackoutTransition ? "1" : "0"));
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Fika: Blackout packet sent=" + blackoutSent + ", restrictExfils=" + restrictExfils + ", disableDoors=" + disableDoors + ".");
                }
            }

            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Blackout: Starting Laboratory Blackout Protocol.");
            }

            if (CanPresentLocally && (!_labsPreBlackoutEventActive || manualBlackoutTransition))
                StartCoroutine(PlayLaboratoryBlackoutSound());
            StartNativeSwitchLockLocal();
            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutApplied = false;
                _blackoutTransitionInProgress = false;
                _blackoutRoutine = null;
                yield break;
            }

            if (restrictExfils)
            {
                LabsBlackout.DisableExfils();
                BlackoutBridge.IndexDisabledExfilsAndTransits(Log);
            }

            if (disableDoors)
                LabsBlackout.DisableAutomaticDoors(_automaticDoors);
            if (CanPresentLocally)
            {
                yield return LabsBlackout.ApplyBlackoutCadence(() => _raidisActive && _blackoutApplied && !_takeoverFacilityRestored && ReferenceEquals(world, _activeRaidWorld) && ReferenceEquals(world, Singleton<GameWorld>.Instance));
                if (!_raidisActive || !_blackoutApplied || _takeoverFacilityRestored || !ReferenceEquals(world, _activeRaidWorld) || !ReferenceEquals(world, Singleton<GameWorld>.Instance))
                {
                    _blackoutTransitionInProgress = false;
                    _blackoutRoutine = null;
                    yield break;
                }
            }

            _blackoutTransitionInProgress = false;
            SyncBlackoutSafetyLocal();
            if (_labsPreBlackoutEventActive && _takeoverInfrastructure != null && _takeoverInfrastructure.enabled)
            {
                if (manualBlackoutTransition)
                    ActivateNormalBlackoutRestoreControls(true);
                if (CanPresentLocally)
                    EnsureTakeoverPanelsLocal();
                if (IsEventAuthority)
                {
                    _takeoverEmergencyLightsStarted = true;
                    if (_takeoverInfrastructure.alarm != null && _takeoverInfrastructure.alarm.enabled)
                    {
                        ChooseTakeoverAlarm();
                        _takeoverAlarmStarted = true;
                    }

                    if (CanPresentLocally)
                    {
                        if (!_takeoverAlarmDisabled)
                            StartTakeoverAlarmLocal();
                        ApplyFacilityPanelStateLocal();
                    }

                    if (IsFikaServer)
                        BroadcastTakeoverState();
                }
                else
                {
                    if (_takeoverFacilityRestored)
                        RestoreTakeoverFacilityLocal();
                    if (_takeoverGatesOpened)
                        OpenTakeoverGatesLocal();
                    if (_takeoverAlarmStarted && !_takeoverAlarmDisabled)
                        StartTakeoverAlarmLocal();
                    ApplyFacilityPanelStateLocal();
                }
            }
            else if (_takeoverInfrastructure != null && _takeoverInfrastructure.enabled)
            {
                ActivateNormalBlackoutRestoreControls(manualBlackoutTransition);
            }

            if (!_labsPreBlackoutEventActive && IsEventAuthority)
                ScheduleLabsBlackoutSpawnEvent();
            SyncTakeoverFireLocal();
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Blackout: Laboratory Blackout Protocol completed; emissive light planes are included.");
            }

            _blackoutRoutine = null;
        }
    }
}
