using System;
using System.Collections;
using System.Globalization;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private Coroutine _decontaminationGateRoutine, _decontaminationLockRoutine;
        private bool _decontaminationGatesOrdered, _decontaminationExfilsLocked;
        private double _decontaminationClosingStarted = -1d;
        private static readonly string[] EvacuationGates =
        {
            "lab_Parking_Gate",
            "lab_Hangar_Gate"
        };
        private void ScheduleDecontaminationEvents(string clip)
        {
            if (DeadManActive || !_decontamination.Configured || !_decontamination.Announced)
                return;
            if (GasRules.MatchesClip(clip, "DecontaminationProcess05") && !_decontaminationGatesOrdered && _decontaminationGateRoutine == null)
                _decontaminationGateRoutine = StartCoroutine(ApplyDecontaminationGates(_currentNetworkAnnouncementStart - (_officeAccess?.pausedSeconds ?? 0d) + AnnouncementTriggerDelays.EvacuationGates(clip), _activeRaidWorld));
            if (GasRules.MatchesClip(clip, GasRules.TriggerClip) && !_decontaminationExfilsLocked && _decontaminationLockRoutine == null)
                BeginDecontaminationClosing(_currentNetworkAnnouncementStart - (_officeAccess?.pausedSeconds ?? 0d), null);
        }

        private void BeginDecontaminationClosing(double started, string names)
        {
            if (DeadManActive || _decontaminationExfilsLocked || _decontaminationClosingStarted >= 0d)
                return;
            try
            {
                float remaining = (float)(started + GasRules.ExtractionLockDelay - DecontaminationClock);
                LabsFinalLockdown.BeginWindow(remaining, Log, names);
                _decontaminationClosingStarted = started;
                _decontaminationLockRoutine = StartCoroutine(FinishDecontaminationClosing(_activeRaidWorld));
                if (IsFikaServer)
                    NetworkBridge.Broadcast("X|" + GetDecontaminationEventState());
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF: Extraction closing countdown failed: " + ex.GetBaseException().Message);
            }
        }

        private IEnumerator FinishDecontaminationClosing(GameWorld world)
        {
            while (_raidisActive && ReferenceEquals(world, _activeRaidWorld) && LabsFinalLockdown.WindowActive && !LabsFinalLockdown.WindowFinished && !LabsFinalLockdown.WindowExpired)
                yield return null;
            _decontaminationLockRoutine = null;
            if (!_raidisActive || world == null || !ReferenceEquals(world, Singleton<GameWorld>.Instance) || !IsLaboratoryPresentationAllowed())
                yield break;
            if (LabsFinalLockdown.WindowExpired)
                Log.LogError("ISB SOF: Native extraction countdown did not finish before the lockdown deadline.");
            CompleteDecontaminationClosing();
        }

        private void CompleteDecontaminationClosing()
        {
            if (DeadManActive || _decontaminationExfilsLocked)
                return;
            try
            {
                LabsFinalLockdown.Activate(Log);
                _decontaminationExfilsLocked = true;
                _emergencyElevators.Reset();
                _emergencyElevatorMask = 0;
                if (IsFikaServer)
                    NetworkBridge.Broadcast("X|" + GetDecontaminationEventState());
                if (IsEventAuthority)
                    ISB.Compatibility.ISBEffectNotifications.Publish("decontamination_lockdown", Log);
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF: Final extraction lockdown failed: " + ex.GetBaseException().Message);
            }
        }

        private IEnumerator ApplyDecontaminationGates(double when, GameWorld world)
        {
            while (_raidisActive && ReferenceEquals(world, _activeRaidWorld) && (DecontaminationPaused || DecontaminationClock < when))
                yield return null;
            _decontaminationGateRoutine = null;
            if (!_raidisActive || !IsEventAuthority || _decontamination?.Configured != true || world == null || !ReferenceEquals(world, Singleton<GameWorld>.Instance) || !IsLaboratoryPresentationAllowed())
                yield break;
            try
            {
                if (DeadManActive || _decontaminationExfilsLocked)
                    yield break;
                _decontaminationGatesOrdered = true;
                _takeoverGatesOpened = true;
                _gateBClosed = false;
                _takeoverGatesAppliedLocally = false;
                OpenTakeoverGatesLocal();
                if (!_takeoverGatesAppliedLocally)
                    throw new InvalidOperationException("Both evacuation gates could not be opened.");
                ApplyFacilityPanelStateLocal();
                BroadcastTakeoverState();
                if (IsFikaServer)
                    NetworkBridge.Broadcast("X|" + GetDecontaminationEventState());
                ISB.Compatibility.ISBEffectNotifications.Publish("decontamination_gates", Log);
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF: Decontamination facility action failed: " + ex.GetBaseException().Message);
            }
        }

        private string GetDecontaminationEventState() => (_decontaminationGatesOrdered ? "1" : "0") + (_decontaminationExfilsLocked ? "1" : "0") + ";" + _decontaminationClosingStarted.ToString("R", CultureInfo.InvariantCulture) + ";" + LabsFinalLockdown.WindowNames;
        private void ReceiveDecontaminationEventState(string state)
        {
            if (DeadManActive || _officeAbortApplied || IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed() || string.IsNullOrEmpty(state) || state.Length > 2048)
                return;
            var fields = state.Split(';');
            if (fields[0].Length != 2 || (fields.Length != 1 && fields.Length != 3))
                return;
            if (state[0] == '1' && !_decontaminationGatesOrdered)
            {
                _decontaminationGatesOrdered = true;
                _takeoverGatesOpened = true;
                _gateBClosed = false;
                _takeoverGatesAppliedLocally = false;
                OpenTakeoverGatesLocal();
            }

            if (fields.Length == 3 && state[1] != '1' && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double started) && DecontaminationVideoTimeline.ValidStart(started))
                BeginDecontaminationClosing(started, fields[2]);
            if (state[1] == '1' && !_decontaminationExfilsLocked)
            {
                if (!LabsFinalLockdown.WindowActive)
                    CompleteDecontaminationClosing();
            }
        }

        private void ResetDecontaminationEvents()
        {
            StopRoutine(ref _decontaminationGateRoutine);
            StopRoutine(ref _decontaminationLockRoutine);
            _decontaminationGatesOrdered = false;
            _decontaminationExfilsLocked = false;
            _decontaminationClosingStarted = -1d;
            LabsFinalLockdown.Reset();
        }
    }
}
