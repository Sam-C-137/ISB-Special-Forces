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
        private void BroadcastTakeoverState()
        {
            if (!IsFikaServer || !IsLaboratoryPresentationAllowed())
                return;
            NetworkBridge.Broadcast(string.Join("|", new[] { "U", _labsPreBlackoutEventActive ? "1" : "0", _takeoverAlarmStarted ? "1" : "0", _takeoverAlarmDisabled ? "1" : "0", _takeoverGatesOpened ? "1" : "0", _takeoverPowerA ? "1" : "0", _takeoverPowerB ? "1" : "0", _takeoverFacilityRestored ? "1" : "0", _takeoverPowerC ? "1" : "0", _takeoverPowerD ? "1" : "0", _takeoverGateA ? "1" : "0", _takeoverGateB ? "1" : "0", _takeoverGateC ? "1" : "0", _disableTakeoverFire ? "1" : "0", _disableTakeoverSirens ? "1" : "0", _disableTakeoverDestroyedVehicles ? "1" : "0", _takeoverEmergencyLightsStarted ? "1" : "0", _takeoverEmergencyLightsDisabled ? "1" : "0", _announcementSwitches.MapMuted ? "1" : "0", _announcementSwitches.DecontaminationMuted ? "1" : "0", _takeoverAlarmChoice.ToString(CultureInfo.InvariantCulture) }));
        }

        private void BroadcastFacilityControlState()
        {
            if (!IsFikaServer || !IsLaboratoryPresentationAllowed())
                return;
            bool a = IsShutdownControlMode(_facilityControlMode) ? _manualShutdownA : _takeoverPowerA;
            bool b = IsShutdownControlMode(_facilityControlMode) ? _manualShutdownB : _takeoverPowerB;
            bool completed = IsShutdownControlMode(_facilityControlMode) ? _manualShutdownCompleted : _takeoverFacilityRestored;
            NetworkBridge.Broadcast(string.Join("|", new[] { "V", ((int)_facilityControlMode).ToString(CultureInfo.InvariantCulture), a ? "1" : "0", b ? "1" : "0", completed ? "1" : "0", (IsShutdownControlMode(_facilityControlMode) ? _manualShutdownC : _takeoverPowerC) ? "1" : "0", (IsShutdownControlMode(_facilityControlMode) ? _manualShutdownD : _takeoverPowerD) ? "1" : "0" }));
        }

        private void HandleNetworkFacilityControlState(string[] parts)
        {
            if (parts == null || parts.Length < 5 || !IsLaboratoryPresentationAllowed())
                return;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int modeValue))
                return;
            ApplyNetworkFacilityControlState((LabsFacilityControlMode)modeValue, parts[2] == "1", parts[3] == "1", parts[4] == "1", parts.Length > 5 && parts[5] == "1", parts.Length > 6 && parts[6] == "1");
        }

        private void ApplyNetworkFacilityControlState(LabsFacilityControlMode mode, bool authorizationA, bool authorizationB, bool completed, bool authorizationC = false, bool authorizationE = false)
        {
            if (mode == LabsFacilityControlMode.None)
                return;
            bool modeChanged = _facilityControlMode != mode;
            _facilityControlMode = mode;
            if (IsShutdownControlMode(mode))
            {
                _manualShutdownA = authorizationA;
                _manualShutdownB = authorizationB;
                _manualShutdownC = authorizationC;
                _manualShutdownD = authorizationE;
                _manualShutdownCompleted = completed;
            }
            else
            {
                _takeoverPowerA = authorizationA;
                _takeoverPowerB = authorizationB;
                _takeoverPowerC = authorizationC;
                _takeoverPowerD = authorizationE;
                _takeoverFacilityRestored = completed;
            }

            if (!CanPresentLocally)
                return;
            if (modeChanged)
            {
                LabsTakeoverInfrastructure.Reset();
                _takeoverPanelsInitialized = false;
            }

            EnsureFacilityPanelsLocal(mode);
            if (!_takeoverPanelsInitialized)
                StartFacilityPanelEnsureRoutine(mode);
            ApplyFacilityPanelStateLocal();
        }

        private void HandleNetworkTakeoverState(string[] parts)
        {
            if (parts == null || parts.Length < 8 || !IsLaboratoryPresentationAllowed())
                return;
            int alarmChoice;
            if (parts.Length > 20 && int.TryParse(parts[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out alarmChoice))
                ReceiveTakeoverAlarmChoice(alarmChoice);
            else if (!TakeoverAlarmChoice.Valid(_takeoverAlarmChoice))
                ReceiveTakeoverAlarmChoice(0);
            SetAnnouncementMutes(parts.Length > 18 && parts[18] == "1", parts.Length > 19 && parts[19] == "1");
            ApplyNetworkTakeoverState(parts[1] == "1", parts[2] == "1", parts[3] == "1", parts[4] == "1", parts[5] == "1", parts[6] == "1", parts[7] == "1", parts.Length > 8 && parts[8] == "1", parts.Length > 9 && parts[9] == "1", parts.Length > 10 && parts[10] == "1", parts.Length > 11 && parts[11] == "1", parts.Length > 12 && parts[12] == "1", parts.Length > 13 && parts[13] == "1", parts.Length > 14 && parts[14] == "1", parts.Length > 15 && parts[15] == "1", parts.Length > 16 ? parts[16] == "1" : parts[2] == "1", parts.Length > 17 && parts[17] == "1");
        }

        private void ApplyNetworkTakeoverState(bool takeoverActive, bool alarmStarted, bool alarmDisabled, bool gatesOpened, bool powerA, bool powerB, bool facilityRestored, bool powerC = false, bool powerD = false, bool gateA = false, bool gateB = false, bool gateC = false, bool disableFire = false, bool disableSirens = false, bool disableDestroyedVehicles = false, bool emergencyLightsStarted = false, bool emergencyLightsDisabled = false)
        {
            SetTakeoverSceneryOptions(disableFire, disableSirens, disableDestroyedVehicles);
            bool facilityChanged = facilityRestored && !_takeoverFacilityRestored;
            _labsPreBlackoutEventActive = takeoverActive;
            _takeoverAlarmStarted = alarmStarted;
            _takeoverAlarmDisabled = alarmDisabled || DeadManActive;
            _takeoverEmergencyLightsStarted = takeoverActive && emergencyLightsStarted;
            _takeoverEmergencyLightsDisabled = takeoverActive && emergencyLightsDisabled;
            _takeoverGatesOpened = gatesOpened;
            _takeoverPowerA = powerA;
            _takeoverPowerB = powerB;
            _takeoverPowerC = powerC;
            _takeoverPowerD = powerD;
            _takeoverGateA = gateA;
            _takeoverGateB = gateB;
            _takeoverGateC = gateC;
            _takeoverFacilityRestored = facilityRestored;
            SyncTakeoverFireLocal();
            if (facilityRestored)
            {
                _blackoutApplied = false;
                _manualShutdownCompleted = false;
                if (facilityChanged)
                    RestoreTakeoverFacilityLocal();
                ActivateManualShutdownControls();
            }
            else if (_blackoutApplied)
            {
                StartNativeSwitchLockLocal();
                ActivateNormalBlackoutRestoreControls(false);
            }

            if (gatesOpened)
            {
                _gateBClosed = false;
                OpenTakeoverGatesLocal();
            }

            if (!CanPresentLocally)
                return;
            if (_takeoverAlarmStarted)
                StartTakeoverAlarmLocal();
            ApplyFacilityPanelStateLocal();
        }
    }
}
