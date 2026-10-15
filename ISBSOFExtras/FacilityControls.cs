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
        private void RequestTakeoverAction(string action)
        {
            if (!_raidisActive || !IsLaboratoryPresentationAllowed() || string.IsNullOrEmpty(action))
                return;
            if (IsEventAuthority)
            {
                ApplyTakeoverActionAuthority(action);
                return;
            }

            if (IsFikaNetworkSession)
                NetworkBridge.SendToServer("T|" + action);
        }

        private bool ApplyTakeoverActionAuthority(string action, bool terminalOperation = false)
        {
            if (!IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed())
                return false;
            int elevator = LabsEmergencyElevators.ActionIndex(action);
            if (elevator >= 0)
            {
                if (_blackoutApplied && !_takeoverFacilityRestored && !_blackoutTransitionInProgress && !_decontaminationExfilsLocked)
                {
                    SyncBlackoutSafetyLocal();
                    if (_emergencyElevators.Activate(elevator, Log))
                    {
                        _emergencyElevatorMask = _emergencyElevators.Mask;
                        if (IsFikaServer)
                            NetworkBridge.Broadcast("J|" + _emergencyElevatorMask.ToString(CultureInfo.InvariantCulture));
                        return true;
                    }
                }

                return false;
            }

            if (!_raidisActive || !IsLaboratoryPresentationAllowed() || _takeoverInfrastructure == null || !_takeoverInfrastructure.enabled)
                return false;
            if (action == LabsTakeoverInfrastructure.MapAnnouncementsAction || action == LabsTakeoverInfrastructure.DecontaminationAnnouncementsAction)
            {
                if (!_labsPreBlackoutEventActive || DeadManActive)
                    return false;
                bool map = _announcementSwitches.MapMuted, decon = _announcementSwitches.DecontaminationMuted;
                if (action == LabsTakeoverInfrastructure.MapAnnouncementsAction)
                    map = !map;
                else
                    decon = !decon;
                SetAnnouncementMutes(map, decon);
                BroadcastTakeoverState();
                if (action == LabsTakeoverInfrastructure.MapAnnouncementsAction ? map : decon)
                    ConfirmSharedAuthorizationEffect(action);
                return true;
            }

            bool changed = false;
            if (string.Equals(action, LabsTakeoverInfrastructure.AlarmAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && !DeadManActive && _takeoverAlarmStarted && _takeoverInfrastructure.alarm != null && _takeoverInfrastructure.alarm.enabled)
                {
                    _takeoverAlarmDisabled = !_takeoverAlarmDisabled;
                    StartTakeoverAlarmLocal();
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.EmergencyLightsAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && _takeoverEmergencyLightsStarted && !_disableTakeoverSirens)
                {
                    _takeoverEmergencyLightsDisabled = !_takeoverEmergencyLightsDisabled;
                    StartTakeoverAlarmLocal();
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.GatesAction, StringComparison.Ordinal))
            {
                if (CanUseGateOverride() && (!_labsPreBlackoutEventActive || (_takeoverGateA && _takeoverGateB && _takeoverGateC)))
                {
                    _takeoverGatesOpened = true;
                    _gateBClosed = false;
                    OpenTakeoverGatesLocal();
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.GateAAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanUseGateOverride() && !_takeoverGateA)
                {
                    _takeoverGateA = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.GateBAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanUseGateOverride() && !_takeoverGateB)
                {
                    _takeoverGateB = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.GateCAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanUseGateOverride() && !_takeoverGateC && (terminalOperation || (_takeoverGateA && _takeoverGateB)))
                {
                    _takeoverGateC = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.PowerAAction, StringComparison.Ordinal))
            {
                if (CanAuthorizeFacilityPower() && !_takeoverPowerA)
                {
                    _takeoverPowerA = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.PowerBAction, StringComparison.Ordinal))
            {
                if (CanAuthorizeFacilityPower() && !_takeoverPowerB)
                {
                    _takeoverPowerB = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.PowerCAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanAuthorizeFacilityPower() && !_takeoverPowerC)
                {
                    _takeoverPowerC = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.PowerDAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanAuthorizeFacilityPower() && !_takeoverPowerD && (terminalOperation || (_takeoverPowerA && _takeoverPowerB && _takeoverPowerC)))
                {
                    _takeoverPowerD = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.MasterAction, StringComparison.Ordinal))
            {
                if (CanAuthorizeFacilityPower() && _takeoverPowerA && _takeoverPowerB && (!_labsPreBlackoutEventActive || (_takeoverPowerC && _takeoverPowerD)) && !_takeoverFacilityRestored)
                {
                    if (_labsPreBlackoutEventActive && !_facilitySequence.FreeToggle)
                    {
                        if (_facilitySequence.masterStarted)
                            return false;
                        _facilitySequence.masterStarted = true;
                    }
                    else
                        CompleteFacilityRestoration();
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.ShutdownAAction, StringComparison.Ordinal))
            {
                if (CanAuthorizeFacilityShutdown() && !_manualShutdownA)
                {
                    _manualShutdownA = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.ShutdownBAction, StringComparison.Ordinal))
            {
                if (CanAuthorizeFacilityShutdown() && !_manualShutdownB)
                {
                    _manualShutdownB = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.ShutdownCAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanAuthorizeFacilityShutdown() && !_manualShutdownC)
                {
                    _manualShutdownC = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.ShutdownDAction, StringComparison.Ordinal))
            {
                if (_labsPreBlackoutEventActive && CanAuthorizeFacilityShutdown() && !_manualShutdownD && (terminalOperation || (_manualShutdownA && _manualShutdownB && _manualShutdownC)))
                {
                    _manualShutdownD = true;
                    changed = true;
                }
            }
            else if (string.Equals(action, LabsTakeoverInfrastructure.ShutdownMasterAction, StringComparison.Ordinal))
            {
                if (CanAuthorizeFacilityShutdown() && _manualShutdownA && _manualShutdownB && (!_labsPreBlackoutEventActive || (_manualShutdownC && _manualShutdownD)))
                {
                    if (_labsPreBlackoutEventActive && _facilitySequence.powerCommissioned)
                        _facilitySequence.shutdownCommissioned = true;
                    _manualShutdownCompleted = true;
                    _takeoverFacilityRestored = false;
                    changed = true;
                    StartCoroutine(ApplyManualBlackoutAuthority());
                }
            }

            if (!changed)
                return false;
            _facilitySequence.CancelForManualAction(action);
            if (_labsPreBlackoutEventActive && (action.StartsWith("POWER_", StringComparison.Ordinal) || action == LabsTakeoverInfrastructure.MasterAction))
                QueueGeneratorAnnouncement();
            ApplyFacilityPanelStateLocal();
            BroadcastFacilitySequence();
            if (IsFikaServer)
            {
                BroadcastTakeoverState();
                BroadcastFacilityControlState();
            }

            if (action != LabsTakeoverInfrastructure.MasterAction || !_labsPreBlackoutEventActive)
                ConfirmSharedAuthorizationEffect(action);
            return true;
        }

        private void CompleteFacilityRestoration()
        {
            if (_labsPreBlackoutEventActive)
                _facilitySequence.powerCommissioned = true;
            _takeoverFacilityRestored = true;
            _manualShutdownCompleted = false;
            _manualShutdownA = _manualShutdownB = _manualShutdownC = _manualShutdownD = _labsPreBlackoutEventActive && _facilitySequence.shutdownCommissioned;
            _blackoutApplied = false;
            if (CanPresentLocally)
            {
                StartCoroutine(PlayTakeoverPowerRestorationSound());
            }

            RestoreTakeoverFacilityLocal();
            if (IsFikaServer)
                NetworkBridge.Broadcast("E");
            if (!_labsPreBlackoutEventActive)
                ScheduleLabsPowerRestoredSpawnEvent();
            ActivateManualShutdownControls();
            ConfirmSharedAuthorizationEffect(LabsTakeoverInfrastructure.MasterAction);
        }

        private bool CanUseGateOverride()
        {
            return _takeoverInfrastructure?.gateOverride != null && _takeoverInfrastructure.gateOverride.enabled && !_takeoverGatesOpened && (_labsPreBlackoutEventActive ? _blackoutApplied : (_blackoutApplied || _takeoverFacilityRestored));
        }

        private bool CanAuthorizeFacilityPower()
        {
            return !DeadManActive && _blackoutApplied && !_blackoutTransitionInProgress && IsRestoreControlMode(_facilityControlMode) && _takeoverInfrastructure?.facilityPower != null && _takeoverInfrastructure.facilityPower.enabled && !_takeoverFacilityRestored && !_facilitySequence.masterStarted;
        }

        private bool CanAuthorizeFacilityShutdown()
        {
            return !DeadManActive && !_blackoutApplied && IsShutdownControlMode(_facilityControlMode) && _takeoverInfrastructure?.manualBlackout != null && _takeoverInfrastructure.manualBlackout.enabled && !_manualShutdownCompleted;
        }

        private readonly LabsEmergencyLighting _emergencyLighting = new LabsEmergencyLighting();
        private void SyncBlackoutSafetyLocal()
        {
            bool active = _raidisActive && IsLaboratoryPresentationAllowed() && _blackoutApplied && !_takeoverFacilityRestored;
            _emergencyLighting.SetActive((active || NightmareEmergencyMode) && _labsPreBlackoutEventActive && (_takeoverEmergencyLightsStarted || NightmareEmergencyMode) && (!_takeoverEmergencyLightsDisabled || NightmareWarningLights) && CanPresentLocally && !_blackoutTransitionInProgress && !NightmareBlackout);
            LabsKeycardLockdown.SetActive(active);
            LabsNativeAnnouncements.SetActive(active);
            if (active)
                BlackoutBridge.LockNativeSwitches();
            active = active && !_decontaminationExfilsLocked;
            _emergencyElevators.SetActive(active, CanPresentLocally, RequestTakeoverAction, Log);
            if (active)
                _emergencyElevators.ApplyMask(_emergencyElevatorMask, Log);
        }

        private void ApplyFacilityPanelStateLocal()
        {
            SyncBlackoutSafetyLocal();
            SyncTakeoverFireLocal();
            if (!CanPresentLocally)
                return;
            LabsTakeoverInfrastructure.ApplyAnnouncementMutes(_announcementSwitches.MapMuted, _announcementSwitches.DecontaminationMuted);
            LabsTakeoverInfrastructure.ApplyState(_takeoverAlarmDisabled, _takeoverGatesOpened, _takeoverPowerA, _takeoverPowerB, _takeoverFacilityRestored, _takeoverPowerC, _takeoverPowerD, _takeoverGateA, _takeoverGateB, _takeoverGateC, _takeoverEmergencyLightsDisabled);
            if (IsShutdownControlMode(_facilityControlMode))
                LabsTakeoverInfrastructure.ApplyShutdownState(_manualShutdownA, _manualShutdownB, _manualShutdownCompleted, _manualShutdownC, _manualShutdownD);
        }

        private static bool IsShutdownControlMode(LabsFacilityControlMode mode)
        {
            return mode == LabsFacilityControlMode.ShutdownOnly || mode == LabsFacilityControlMode.TakeoverShutdown;
        }

        private static bool IsRestoreControlMode(LabsFacilityControlMode mode)
        {
            return mode == LabsFacilityControlMode.RestoreOnly || mode == LabsFacilityControlMode.Takeover;
        }

        private void StartNativeSwitchLockLocal()
        {
            if (!_blackoutApplied)
                return;
            if (_nativeSwitchLockRoutine != null)
                return;
            _nativeSwitchLockRoutine = StartCoroutine(NativeSwitchLockRoutine());
        }

        private IEnumerator NativeSwitchLockRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(0.5f);
            while (_raidisActive && _blackoutApplied)
            {
                BlackoutBridge.LockNativeSwitches();
                yield return wait;
            }

            _nativeSwitchLockRoutine = null;
        }

        private void EnsureTakeoverPanelsLocal()
        {
            if (!_labsPreBlackoutEventActive)
                return;
            LabsFacilityControlMode mode = _facilityControlMode == LabsFacilityControlMode.TakeoverShutdown ? LabsFacilityControlMode.TakeoverShutdown : LabsFacilityControlMode.Takeover;
            EnsureFacilityPanelsLocal(mode);
            if (!_takeoverPanelsInitialized)
                StartFacilityPanelEnsureRoutine(mode);
        }

        private void EnsureFacilityPanelsLocal(LabsFacilityControlMode mode)
        {
            if (!CanPresentLocally || _blackoutTransitionInProgress || _takeoverInfrastructure == null || !_takeoverInfrastructure.enabled || mode == LabsFacilityControlMode.None)
                return;
            if (_takeoverPanelsInitialized && _facilityControlMode == mode)
                return;
            if (_facilityControlMode != mode || _takeoverPanelsInitialized)
            {
                LabsTakeoverInfrastructure.Reset();
                _takeoverPanelsInitialized = false;
            }

            _facilityControlMode = mode;
            _takeoverPanelsInitialized = LabsTakeoverInfrastructure.Initialize(_takeoverInfrastructure, mode, Log, RequestTakeoverAction, !_disableTakeoverSirens);
            if (!_takeoverPanelsInitialized)
                return;
            ApplyFacilityPanelStateLocal();
        }

        private void StartFacilityPanelEnsureRoutine(LabsFacilityControlMode mode)
        {
            if (!CanPresentLocally || mode == LabsFacilityControlMode.None)
                return;
            if (_takeoverPanelsInitialized && _facilityControlMode == mode)
                return;
            StopRoutine(ref _facilityPanelEnsureRoutine);
            _facilityPanelEnsureRoutine = StartCoroutine(FacilityPanelEnsureRoutine(mode));
        }

        private IEnumerator FacilityPanelEnsureRoutine(LabsFacilityControlMode mode)
        {
            while (_raidisActive && IsLaboratoryPresentationAllowed() && _facilityControlMode == mode && !_takeoverPanelsInitialized)
            {
                EnsureFacilityPanelsLocal(mode);
                if (_takeoverPanelsInitialized)
                    break;
                yield return new WaitForSeconds(0.5f);
            }

            _facilityPanelEnsureRoutine = null;
        }

        private void OpenTakeoverGatesLocal()
        {
            if (_takeoverGatesAppliedLocally || (!_decontaminationGatesOrdered && (_takeoverInfrastructure?.gateOverride == null || !_takeoverInfrastructure.gateOverride.enabled)))
                return;
            int activated = BlackoutBridge.ActivateGateExfils(_decontaminationGatesOrdered ? EvacuationGates : _takeoverInfrastructure.gateOverride.gates, Log, _blackoutApplied, _takeoverInfrastructure?.gateOverride?.alarmMaxDistance ?? 120, _takeoverInfrastructure?.gateOverride?.alarmVolumeMultiplier ?? 2.5f);
            _takeoverGatesAppliedLocally = activated >= (_decontaminationGatesOrdered ? 2 : (_takeoverInfrastructure.gateOverride.gates?.Length ?? 0));
        }

        private void RestoreTakeoverFacilityLocal()
        {
            _emergencyLighting.SetActive(false);
            LabsKeycardLockdown.SetActive(false);
            LabsNativeAnnouncements.SetActive(false);
            _emergencyElevators.Reset();
            _emergencyElevatorMask = 0;
            SyncTakeoverSirensLocal();
            StopRoutine(ref _blackoutRoutine);
            _blackoutTransitionInProgress = false;
            _pendingNetworkBlackout = false;
            _pendingNetworkManualBlackoutTransition = false;
            StopRoutine(ref _nativeSwitchLockRoutine);
            BlackoutBridge.RestoreNativeSwitches(Log);
            TakeoverFacilityPowerDefinition power = _takeoverInfrastructure?.facilityPower;
            if (power == null || !power.enabled)
                return;
            if (CanPresentLocally && power.restoreLights)
                LabsBlackout.RestoreFacilityPower();
            if (power.restoreElevators && !_decontaminationExfilsLocked)
                BlackoutBridge.RestoreElevatorExfils(Log);
        }

        private void ActivateManualShutdownControls()
        {
            ManualBlackoutDefinition manual = _takeoverInfrastructure?.manualBlackout;
            if (manual == null || !manual.enabled || _blackoutApplied)
                return;
            LabsFacilityControlMode targetMode = _labsPreBlackoutEventActive ? LabsFacilityControlMode.TakeoverShutdown : LabsFacilityControlMode.ShutdownOnly;
            if (_facilityControlMode != targetMode)
            {
                _facilityControlMode = targetMode;
                if (CanPresentLocally)
                {
                    LabsTakeoverInfrastructure.Reset();
                    _takeoverPanelsInitialized = false;
                }
            }

            if (CanPresentLocally)
            {
                EnsureFacilityPanelsLocal(targetMode);
                if (!_takeoverPanelsInitialized)
                    StartFacilityPanelEnsureRoutine(targetMode);
            }

            if (IsFikaServer)
                BroadcastFacilityControlState();
        }

        private void ActivateNormalBlackoutRestoreControls(bool manualBlackoutTransition)
        {
            NormalBlackoutPowerDefinition normalPower = _takeoverInfrastructure?.normalBlackoutPower;
            TakeoverFacilityPowerDefinition power = _takeoverInfrastructure?.facilityPower;
            if (power == null || !power.enabled)
                return;
            if (!_labsPreBlackoutEventActive && (normalPower == null || !normalPower.enabled))
                return;
            LabsFacilityControlMode targetMode = _labsPreBlackoutEventActive ? LabsFacilityControlMode.Takeover : LabsFacilityControlMode.RestoreOnly;
            bool modeChanged = _facilityControlMode != targetMode;
            if (modeChanged && IsEventAuthority)
            {
                _takeoverPowerA = _takeoverPowerB = _takeoverPowerC = _takeoverPowerD = _labsPreBlackoutEventActive && _facilitySequence.powerCommissioned;
                _facilitySequence.masterStarted = false;
                _facilitySequence.restoreAt = -1d;
                _facilitySequence.countdownFrom = -1d;
                _facilitySequence.cues.Clear();
                BroadcastFacilitySequence();
            }

            _takeoverFacilityRestored = false;
            _facilityControlMode = targetMode;
            if (CanPresentLocally)
            {
                if (modeChanged)
                {
                    LabsTakeoverInfrastructure.Reset();
                    _takeoverPanelsInitialized = false;
                }

                EnsureFacilityPanelsLocal(targetMode);
                if (!_takeoverPanelsInitialized)
                    StartFacilityPanelEnsureRoutine(targetMode);
            }

            if (IsFikaServer)
                BroadcastFacilityControlState();
        }

        private IEnumerator ApplyManualBlackoutAuthority()
        {
            if (!IsEventAuthority || _blackoutApplied || _blackoutRoutine != null)
                yield break;
            Coroutine activeBlackoutRoutine = StartCoroutine(DisableLaboratoryBlackout());
            _blackoutRoutine = _blackoutTransitionInProgress ? activeBlackoutRoutine : null;
            yield return activeBlackoutRoutine;
        }

        private IEnumerator DisableGateBAtSynchronizedTime(double closeAt)
        {
            if (!IsLaboratoryPresentationAllowed())
                yield break;
            GameWorld world = _activeRaidWorld;
            while (_raidisActive && _blackoutApplied && world != null && ReferenceEquals(world, _activeRaidWorld) && !_gateBClosed && !_takeoverGatesOpened && IsLaboratoryPresentationAllowed() && SynchronizedTime < closeAt)
            {
                yield return null;
            }

            if (!_raidisActive || !IsEventAuthority || !_blackoutApplied || world == null || !ReferenceEquals(world, _activeRaidWorld) || !ReferenceEquals(world, Singleton<GameWorld>.Instance) || _gateBClosed || _takeoverGatesOpened || !IsLaboratoryPresentationAllowed())
                yield break;
            _gateBClosed = BlackoutBridge.DisableAndHideExfil(BlackoutBridge.GateBExfilName, Log);
            if (_gateBClosed && IsFikaServer)
                NetworkBridge.Broadcast("G");
            if (_gateBClosed)
                ISB.Compatibility.ISBEffectNotifications.Publish("gate_b_closed", Log);
        }
    }
}
