using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverPanelPointDefinition
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotationX { get; set; }
        public float rotationY { get; set; }
        public float rotationZ { get; set; }
        public float interactionRadius { get; set; } = 1f;
        public Vector3 Position => new Vector3(x, y, z);
        public Quaternion Rotation => Quaternion.Euler(rotationX, rotationY, rotationZ);
    }

    internal sealed class TakeoverAlarmDefinition
    {
        public bool enabled { get; set; } = true;
        public string audio { get; set; } = "ISB-PreLockdownAlarm.wav";
        public float volume { get; set; } = 35f;
        public TakeoverPanelPointDefinition button { get; set; } = new TakeoverPanelPointDefinition();
    }

    internal sealed class TakeoverGateOverrideDefinition
    {
        public bool enabled { get; set; } = true;
        public int alarmMaxDistance { get; set; } = 120;
        public float alarmVolumeMultiplier { get; set; } = 2.5f;
        public string[] gates { get; set; } = Array.Empty<string>();
        public TakeoverPanelPointDefinition authorizationA { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationB { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationC { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition button { get; set; } = new TakeoverPanelPointDefinition();
    }

    internal sealed class TakeoverFacilityPowerDefinition
    {
        public bool enabled { get; set; } = true;
        public bool restoreLights { get; set; } = true;
        public bool restoreElevators { get; set; } = true;
        public TakeoverPanelPointDefinition authorizationA { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationB { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationC { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationD { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition master { get; set; } = new TakeoverPanelPointDefinition();
    }

    internal sealed class TakeoverPowerRestorationAudioDefinition
    {
        public bool enabled { get; set; } = true;
        public string audio { get; set; } = "black_out_huge.wav";
        public float volume { get; set; } = 4f;
        public string[] generatorAudios { get; set; } =
        {
            "OneGenerator.wav",
            "TwoGenerators.wav",
            "ThreeGenerators.wav",
            "FourGenerators.wav",
            "FiveGenerators.wav"
        };
        public float countdownAt { get; set; } = 19.7f;
        public float countdownSeconds { get; set; } = 60f;
    }

    internal sealed class NormalBlackoutPowerDefinition
    {
        public bool enabled { get; set; } = true;
    }

    internal sealed class ManualBlackoutDefinition
    {
        public bool enabled { get; set; } = true;
        public TakeoverPanelPointDefinition authorizationA { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationB { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationC { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition authorizationD { get; set; } = new TakeoverPanelPointDefinition();
        public TakeoverPanelPointDefinition master { get; set; } = new TakeoverPanelPointDefinition();
    }

    internal sealed class TakeoverInfrastructureDefinition
    {
        public bool enabled { get; set; } = true;
        public TakeoverAlarmDefinition alarm { get; set; } = new TakeoverAlarmDefinition();
        public TakeoverGateOverrideDefinition gateOverride { get; set; } = new TakeoverGateOverrideDefinition();
        public TakeoverFacilityPowerDefinition facilityPower { get; set; } = new TakeoverFacilityPowerDefinition();
        public NormalBlackoutPowerDefinition normalBlackoutPower { get; set; } = new NormalBlackoutPowerDefinition();
        public ManualBlackoutDefinition manualBlackout { get; set; } = new ManualBlackoutDefinition();
        public TakeoverPowerRestorationAudioDefinition powerRestoration { get; set; } = new TakeoverPowerRestorationAudioDefinition();
    }

    internal enum TakeoverPanelRole
    {
        AlarmSilence,
        GateOverride,
        PowerAuthorizationA,
        PowerAuthorizationB,
        PowerMaster,
        ShutdownAuthorizationA,
        ShutdownAuthorizationB,
        ShutdownMaster,
        PowerAuthorizationC,
        PowerAuthorizationD,
        ShutdownAuthorizationC,
        ShutdownAuthorizationD,
        GateAuthorizationA,
        GateAuthorizationB,
        GateAuthorizationC,
        EmergencyLights,
        MapAnnouncements,
        DecontaminationAnnouncements
    }

    internal enum LabsFacilityControlMode
    {
        None,
        Takeover,
        RestoreOnly,
        ShutdownOnly,
        TakeoverShutdown
    }

    internal sealed class TakeoverPanelHandle
    {
        internal TakeoverPanelRole Role { get; }
        internal GameObject Root { get; }
        internal EFT.Interactive.Switch Switch { get; }

        internal TakeoverPanelHandle(TakeoverPanelRole role, GameObject root, EFT.Interactive.Switch sw)
        {
            Role = role;
            Root = root;
            Switch = sw;
        }
    }

    internal static class LabsTakeoverInfrastructure
    {
        internal const string AlarmAction = "ALARM";
        internal const string EmergencyLightsAction = "EMERGENCY_LIGHTS";
        internal const string MapAnnouncementsAction = "MAP_ANNOUNCEMENTS";
        internal const string DecontaminationAnnouncementsAction = "DECON_ANNOUNCEMENTS";
        internal const string GatesAction = "GATES";
        internal const string GateAAction = "GATE_A";
        internal const string GateBAction = "GATE_B";
        internal const string GateCAction = "GATE_C";
        internal const string PowerAAction = "POWER_A";
        internal const string PowerBAction = "POWER_B";
        internal const string PowerCAction = "POWER_C";
        internal const string PowerDAction = "POWER_D";
        internal const string MasterAction = "MASTER";
        internal const string ShutdownAAction = "SHUTDOWN_A";
        internal const string ShutdownBAction = "SHUTDOWN_B";
        internal const string ShutdownCAction = "SHUTDOWN_C";
        internal const string ShutdownDAction = "SHUTDOWN_D";
        internal const string ShutdownMasterAction = "SHUTDOWN_MASTER";
        internal const string ObjectPrefix = "ISB_TakeoverControl_";
        private static readonly Dictionary<TakeoverPanelRole, string> PromptLocaleKeys = new Dictionary<TakeoverPanelRole, string>
        {
            {
                TakeoverPanelRole.AlarmSilence,
                "isb_takeover_alarm_shutdown"
            },
            {
                TakeoverPanelRole.EmergencyLights,
                "isb_takeover_emergency_lights_off"
            },
            {
                TakeoverPanelRole.GateOverride,
                "isb_takeover_gate_override"
            },
            {
                TakeoverPanelRole.GateAuthorizationA,
                "isb_takeover_gate_auth_a"
            },
            {
                TakeoverPanelRole.GateAuthorizationB,
                "isb_takeover_gate_auth_b"
            },
            {
                TakeoverPanelRole.GateAuthorizationC,
                "isb_takeover_gate_auth_c"
            },
            {
                TakeoverPanelRole.PowerAuthorizationA,
                "isb_takeover_power_auth_a"
            },
            {
                TakeoverPanelRole.PowerAuthorizationB,
                "isb_takeover_power_auth_b"
            },
            {
                TakeoverPanelRole.PowerAuthorizationC,
                "isb_takeover_power_auth_c"
            },
            {
                TakeoverPanelRole.PowerAuthorizationD,
                "isb_takeover_power_auth_d"
            },
            {
                TakeoverPanelRole.PowerMaster,
                "isb_takeover_power_master"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationA,
                "isb_labs_shutdown_auth_a"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationB,
                "isb_labs_shutdown_auth_b"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationC,
                "isb_labs_shutdown_auth_c"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationD,
                "isb_labs_shutdown_auth_d"
            },
            {
                TakeoverPanelRole.ShutdownMaster,
                "isb_labs_shutdown_master"
            }
        };
        private static readonly Dictionary<TakeoverPanelRole, string> PromptFallbacks = new Dictionary<TakeoverPanelRole, string>
        {
            {
                TakeoverPanelRole.AlarmSilence,
                "Deactivate Alarm"
            },
            {
                TakeoverPanelRole.EmergencyLights,
                "Turn Off Emergency Lights"
            },
            {
                TakeoverPanelRole.GateOverride,
                "Override Gates"
            },
            {
                TakeoverPanelRole.GateAuthorizationA,
                "Activate Security Pistons"
            },
            {
                TakeoverPanelRole.GateAuthorizationB,
                "Activate Security Pistons"
            },
            {
                TakeoverPanelRole.GateAuthorizationC,
                "Activate Security Pistons"
            },
            {
                TakeoverPanelRole.PowerAuthorizationA,
                "Turn On Power Cell"
            },
            {
                TakeoverPanelRole.PowerAuthorizationB,
                "Turn On Power Cell"
            },
            {
                TakeoverPanelRole.PowerAuthorizationC,
                "Turn On Power Cell"
            },
            {
                TakeoverPanelRole.PowerAuthorizationD,
                "Turn On Power Cell"
            },
            {
                TakeoverPanelRole.PowerMaster,
                "Turn On Auxiliary Power"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationA,
                "Turn Off Power Cell"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationB,
                "Turn Off Power Cell"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationC,
                "Turn Off Power Cell"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationD,
                "Turn Off Power Cell"
            },
            {
                TakeoverPanelRole.ShutdownMaster,
                "Shutdown Facility Power"
            }
        };
        private static readonly Dictionary<TakeoverPanelRole, string> FeedbackLocaleKeys = new Dictionary<TakeoverPanelRole, string>
        {
            {
                TakeoverPanelRole.AlarmSilence,
                "isb_takeover_alarm_disabled"
            },
            {
                TakeoverPanelRole.EmergencyLights,
                "isb_takeover_emergency_lights_disabled"
            },
            {
                TakeoverPanelRole.GateOverride,
                "isb_takeover_gates_opened"
            },
            {
                TakeoverPanelRole.GateAuthorizationA,
                "isb_takeover_gate_a_confirmed"
            },
            {
                TakeoverPanelRole.GateAuthorizationB,
                "isb_takeover_gate_b_confirmed"
            },
            {
                TakeoverPanelRole.GateAuthorizationC,
                "isb_takeover_gate_c_confirmed"
            },
            {
                TakeoverPanelRole.PowerAuthorizationA,
                "isb_takeover_power_a_confirmed"
            },
            {
                TakeoverPanelRole.PowerAuthorizationB,
                "isb_takeover_power_b_confirmed"
            },
            {
                TakeoverPanelRole.PowerAuthorizationC,
                "isb_takeover_power_c_confirmed"
            },
            {
                TakeoverPanelRole.PowerAuthorizationD,
                "isb_takeover_power_d_confirmed"
            },
            {
                TakeoverPanelRole.PowerMaster,
                "isb_takeover_power_restored"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationA,
                "isb_labs_shutdown_a_confirmed"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationB,
                "isb_labs_shutdown_b_confirmed"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationC,
                "isb_labs_shutdown_c_confirmed"
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationD,
                "isb_labs_shutdown_d_confirmed"
            },
            {
                TakeoverPanelRole.ShutdownMaster,
                "isb_labs_shutdown_completed"
            }
        };
        private static readonly Dictionary<TakeoverPanelRole, string> FeedbackFallbacks = new Dictionary<TakeoverPanelRole, string>
        {
            {
                TakeoverPanelRole.AlarmSilence,
                "Facility emergency alarm disabled."
            },
            {
                TakeoverPanelRole.EmergencyLights,
                "All facility emergency lights have been turned off."
            },
            {
                TakeoverPanelRole.GateOverride,
                "Gate security release completed."
            },
            {
                TakeoverPanelRole.GateAuthorizationA,
                "Gate security pistons activated."
            },
            {
                TakeoverPanelRole.GateAuthorizationB,
                "Gate security pistons activated."
            },
            {
                TakeoverPanelRole.GateAuthorizationC,
                "Gate security pistons activated."
            },
            {
                TakeoverPanelRole.PowerAuthorizationA,
                "Channel \"A\" Power Cell ready."
            },
            {
                TakeoverPanelRole.PowerAuthorizationB,
                "Channel \"B\" Power Cell ready."
            },
            {
                TakeoverPanelRole.PowerAuthorizationC,
                "Channel \"C\" Power Cell ready."
            },
            {
                TakeoverPanelRole.PowerAuthorizationD,
                "Channel \"D\" Power Cell ready."
            },
            {
                TakeoverPanelRole.PowerMaster,
                "Facility auxiliary power restored."
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationA,
                "Channel \"A\" Power Cell deactivated."
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationB,
                "Channel \"B\" Power Cell deactivated."
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationC,
                "Channel \"C\" Power Cell deactivated."
            },
            {
                TakeoverPanelRole.ShutdownAuthorizationD,
                "Channel \"D\" Power Cell deactivated."
            },
            {
                TakeoverPanelRole.ShutdownMaster,
                "Total shutdown of facility auxiliary power completed."
            }
        };
        private static Func<string, string, string> _localize;
        private static bool _localizationResolved;
        private static readonly Dictionary<TakeoverPanelRole, TakeoverPanelHandle> Panels = new Dictionary<TakeoverPanelRole, TakeoverPanelHandle>();
        private static readonly Dictionary<EFT.Interactive.Switch, TakeoverPanelRole> RolesBySwitch = new Dictionary<EFT.Interactive.Switch, TakeoverPanelRole>();
        private static readonly HashSet<TakeoverPanelRole> ActivatedRoles = new HashSet<TakeoverPanelRole>();
        private static readonly HashSet<TakeoverPanelRole> VisibleRoles = new HashSet<TakeoverPanelRole>();
        private static Action<string> _requestAction;
        private static ManualLogSource _log;
        private static bool _initialized;
        private static LabsFacilityControlMode _mode;
        private static bool _alarmDisabledState;
        private static bool _emergencyLightsDisabledState;
        private static bool _mapAnnouncementsMuted, _decontaminationAnnouncementsMuted;
        internal static void ApplyAnnouncementMutes(bool map, bool decontamination)
        {
            _mapAnnouncementsMuted = map;
            _decontaminationAnnouncementsMuted = decontamination;
        }

        private static bool _emergencyLightsAvailable;
        private static TakeoverInfrastructureDefinition _definition;
        private static bool _gateCReady;
        private static string _gateMissing = "A, B";
        private static bool _powerDReady, _shutdownDReady;
        private static string _powerMissing = "A, B, C", _shutdownMissing = "A, B, C";
        private static bool _powerMasterReady;
        internal static bool Initialize(TakeoverInfrastructureDefinition definition, LabsFacilityControlMode mode, ManualLogSource log, Action<string> requestAction, bool emergencyLightsAvailable = true)
        {
            if (_initialized && _mode == mode)
                return true;
            if (_initialized)
                Reset();
            if (definition == null || !definition.enabled || mode == LabsFacilityControlMode.None)
                return false;
            EFT.Interactive.Switch nativeTemplate = ResolveNativeSwitchTemplate();
            if (nativeTemplate == null)
            {
                return false;
            }

            _log = log;
            _requestAction = requestAction;
            _mode = mode;
            _definition = definition;
            _emergencyLightsAvailable = emergencyLightsAvailable;
            bool containmentBreach = IsContainmentBreachMode;
            if (containmentBreach)
            {
                if (definition.alarm != null)
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.AlarmSilence, definition.alarm.button, true);
            }

            if (definition.gateOverride != null && definition.gateOverride.enabled)
            {
                RegisterPanel(nativeTemplate, TakeoverPanelRole.GateOverride, definition.gateOverride.button, mode == LabsFacilityControlMode.RestoreOnly);
                if (containmentBreach)
                {
                    bool gateAuthorizationAvailable = mode == LabsFacilityControlMode.Takeover;
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.GateAuthorizationA, definition.gateOverride.authorizationA, gateAuthorizationAvailable);
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.GateAuthorizationB, definition.gateOverride.authorizationB, gateAuthorizationAvailable);
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.GateAuthorizationC, definition.gateOverride.authorizationC, gateAuthorizationAvailable);
                }
            }

            if (mode == LabsFacilityControlMode.Takeover || mode == LabsFacilityControlMode.RestoreOnly)
            {
                if (definition.facilityPower != null && definition.facilityPower.enabled)
                {
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.PowerAuthorizationA, definition.facilityPower.authorizationA, true);
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.PowerAuthorizationB, definition.facilityPower.authorizationB, true);
                    if (containmentBreach)
                    {
                        RegisterPanel(nativeTemplate, TakeoverPanelRole.PowerAuthorizationC, definition.facilityPower.authorizationC, true);
                        RegisterPanel(nativeTemplate, TakeoverPanelRole.PowerAuthorizationD, definition.facilityPower.authorizationD, true);
                    }

                    RegisterPanel(nativeTemplate, TakeoverPanelRole.PowerMaster, definition.facilityPower.master, containmentBreach);
                }
            }
            else if (mode == LabsFacilityControlMode.ShutdownOnly || mode == LabsFacilityControlMode.TakeoverShutdown)
            {
                if (definition.manualBlackout != null && definition.manualBlackout.enabled)
                {
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.ShutdownAuthorizationA, definition.manualBlackout.authorizationA, true);
                    RegisterPanel(nativeTemplate, TakeoverPanelRole.ShutdownAuthorizationB, definition.manualBlackout.authorizationB, true);
                    if (containmentBreach)
                    {
                        RegisterPanel(nativeTemplate, TakeoverPanelRole.ShutdownAuthorizationC, definition.manualBlackout.authorizationC, true);
                        RegisterPanel(nativeTemplate, TakeoverPanelRole.ShutdownAuthorizationD, definition.manualBlackout.authorizationD, true);
                    }

                    RegisterPanel(nativeTemplate, TakeoverPanelRole.ShutdownMaster, definition.manualBlackout.master, false);
                }
            }

            _initialized = Panels.Count > 0;
            return _initialized;
        }

        internal static void ApplyState(bool alarmDisabled, bool gatesOpened, bool powerA, bool powerB, bool facilityRestored, bool powerC = false, bool powerD = false, bool gateA = false, bool gateB = false, bool gateC = false, bool emergencyLightsDisabled = false)
        {
            if (!_initialized)
                return;
            ApplyAlarmState(alarmDisabled);
            ApplyEmergencyLightsState(emergencyLightsDisabled);
            bool containmentBreach = IsContainmentBreachMode;
            bool gatesAvailable = containmentBreach ? _mode == LabsFacilityControlMode.Takeover && !facilityRestored : _mode == LabsFacilityControlMode.RestoreOnly || facilityRestored;
            _gateCReady = gateA && gateB;
            _gateMissing = MissingAuthorizations(gateA, gateB, true);
            _powerMissing = MissingAuthorizations(powerA, powerB, powerC);
            _powerDReady = _powerMissing.Length == 0;
            _powerMasterReady = powerA && powerB && (!containmentBreach || (powerC && powerD));
            SetConsumed(TakeoverPanelRole.GateAuthorizationA, gateA || gatesOpened);
            SetConsumed(TakeoverPanelRole.GateAuthorizationB, gateB || gatesOpened);
            SetConsumed(TakeoverPanelRole.GateAuthorizationC, gateC || gatesOpened);
            SetPanelVisible(TakeoverPanelRole.GateAuthorizationA, containmentBreach && gatesAvailable && !gatesOpened);
            SetPanelVisible(TakeoverPanelRole.GateAuthorizationB, containmentBreach && gatesAvailable && !gatesOpened);
            SetPanelVisible(TakeoverPanelRole.GateAuthorizationC, containmentBreach && gatesAvailable && !gatesOpened);
            SetConsumed(TakeoverPanelRole.GateOverride, gatesOpened);
            SetPanelVisible(TakeoverPanelRole.GateOverride, gatesAvailable && !gatesOpened && (!containmentBreach || (gateA && gateB && gateC)));
            SetConsumed(TakeoverPanelRole.PowerAuthorizationA, powerA || facilityRestored);
            SetConsumed(TakeoverPanelRole.PowerAuthorizationB, powerB || facilityRestored);
            SetConsumed(TakeoverPanelRole.PowerAuthorizationC, powerC || facilityRestored);
            SetConsumed(TakeoverPanelRole.PowerAuthorizationD, powerD || facilityRestored);
            SetPanelVisible(TakeoverPanelRole.PowerMaster, containmentBreach || _powerMasterReady || facilityRestored);
            SetConsumed(TakeoverPanelRole.PowerMaster, facilityRestored);
        }

        internal static void ApplyShutdownState(bool authorizationA, bool authorizationB, bool completed, bool authorizationC = false, bool authorizationD = false)
        {
            if (!_initialized)
                return;
            _shutdownMissing = MissingAuthorizations(authorizationA, authorizationB, authorizationC);
            _shutdownDReady = _shutdownMissing.Length == 0;
            SetConsumed(TakeoverPanelRole.ShutdownAuthorizationA, authorizationA || completed);
            SetConsumed(TakeoverPanelRole.ShutdownAuthorizationB, authorizationB || completed);
            SetConsumed(TakeoverPanelRole.ShutdownAuthorizationC, authorizationC || completed);
            SetConsumed(TakeoverPanelRole.ShutdownAuthorizationD, authorizationD || completed);
            bool masterUnlocked = (authorizationA && authorizationB && (!IsContainmentBreachMode || (authorizationC && authorizationD))) || completed;
            SetPanelVisible(TakeoverPanelRole.ShutdownMaster, masterUnlocked);
            SetConsumed(TakeoverPanelRole.ShutdownMaster, completed);
        }

        private static bool IsContainmentBreachMode => _mode == LabsFacilityControlMode.Takeover || _mode == LabsFacilityControlMode.TakeoverShutdown;

        internal static void Reset()
        {
            foreach (TakeoverPanelHandle handle in Panels.Values)
            {
                if (handle?.Switch != null)
                    handle.Switch.OnDoorStateChanged -= HandleNativeSwitchState;
                if (handle?.Root != null)
                    UnityEngine.Object.Destroy(handle.Root);
            }

            Panels.Clear();
            RolesBySwitch.Clear();
            ActivatedRoles.Clear();
            VisibleRoles.Clear();
            _requestAction = null;
            _nextControlAction = 0f;
            _log = null;
            _initialized = false;
            _mode = LabsFacilityControlMode.None;
            _alarmDisabledState = false;
            _emergencyLightsDisabledState = false;
            ApplyAnnouncementMutes(false, false);
            _emergencyLightsAvailable = false;
            _definition = null;
            _gateCReady = _powerDReady = _shutdownDReady = _powerMasterReady = false;
            _powerMissing = _shutdownMissing = "A, B, C";
        }

        private static EFT.Interactive.Switch ResolveNativeSwitchTemplate()
        {
            ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
            if (controller?.ExfiltrationPoints != null)
            {
                EFT.Interactive.Switch fallback = null;
                foreach (ExfiltrationPoint point in controller.ExfiltrationPoints)
                {
                    if (point?.Settings == null || !IsUsableNativeSwitch(point.Switch))
                        continue;
                    if (string.Equals(point.Settings.Name, "lab_Parking_Gate", StringComparison.Ordinal))
                        return point.Switch;
                    if (fallback == null && string.Equals(point.Settings.Name, "lab_Hangar_Gate", StringComparison.Ordinal))
                        fallback = point.Switch;
                }

                if (fallback != null)
                    return fallback;
            }

            return null;
        }

        private static bool IsUsableNativeSwitch(EFT.Interactive.Switch candidate)
        {
            if (candidate == null || candidate.gameObject == null)
                return false;
            if (!candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded)
                return false;
            return !candidate.gameObject.name.StartsWith(ObjectPrefix, StringComparison.Ordinal);
        }

        internal static EFT.Interactive.Switch CreateEmergencySwitch(string identity, string text, Vector3 position, Quaternion? rotation = null, bool visible = false)
        {
            var template = ResolveNativeSwitchTemplate();
            if (template == null)
                return null;
            var staging = new GameObject(identity + "_Staging");
            staging.SetActive(false);
            GameObject root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(template.gameObject, staging.transform);
                root.SetActive(false);
                root.transform.SetParent(null, true);
                root.name = identity;
                root.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
                root.transform.localScale = Vector3.one;
                var sw = root.GetComponent<EFT.Interactive.Switch>();
                if (sw == null)
                    throw new InvalidOperationException("Native switch component missing.");
                PrepareIndependentSwitch(sw, (TakeoverPanelRole)(-1), identity);
                var localField = HarmonyLib.AccessTools.Field(typeof(WorldInteractiveObject), "_forceLocalInteraction");
                if (localField == null)
                    throw new MissingFieldException("WorldInteractiveObject._forceLocalInteraction");
                localField.SetValue(sw, true);
                sw.interactWithoutAnimation = true;
                sw.ExtractionZoneTip = text;
                sw.ContextMenuTip = text;
                PrepareInvisibleNativeSwitch(root, 0.45f);
                root.GetComponent<BoxCollider>().size = new Vector3(.4f, .4f, .2f);
                if (visible)
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    { renderer.enabled = true; renderer.forceRenderingOff = false; }
                }
                root.SetActive(true);
                return sw;
            }
            catch
            {
                if (root != null)
                    UnityEngine.Object.Destroy(root);
                throw;
            }
            finally
            {
                UnityEngine.Object.Destroy(staging);
            }
        }

        private static readonly Dictionary<WorldInteractiveObject, Action<GamePlayerOwner>> EmergencyActions = new Dictionary<WorldInteractiveObject, Action<GamePlayerOwner>>();
        internal static void RegisterEmergencyAction(WorldInteractiveObject control, Action action) => EmergencyActions[control] = owner => action();
        internal static void RegisterEmergencyAction(WorldInteractiveObject control, Action<GamePlayerOwner> action) => EmergencyActions[control] = action;
        internal static void UnregisterEmergencyAction(WorldInteractiveObject control) => EmergencyActions.Remove(control);
        private sealed class EmergencyPlantAction
        {
            internal string Text;
            internal Func<bool> Available;
            internal Action<GamePlayerOwner> Install;
        }

        private static readonly Dictionary<PlaceItemTrigger, EmergencyPlantAction> EmergencyPlantActions = new Dictionary<PlaceItemTrigger, EmergencyPlantAction>();
        internal static void RegisterEmergencyPlantZone(PlaceItemTrigger zone, string text, Func<bool> available, Action<GamePlayerOwner> install)
        {
            EmergencyPlantActions[zone] = new EmergencyPlantAction
            {
                Text = text,
                Available = available,
                Install = install
            };
        }

        internal static void UnregisterEmergencyPlantZone(PlaceItemTrigger zone) => EmergencyPlantActions.Remove(zone);
        private static bool PlantActions(GamePlayerOwner __0, PlaceItemTrigger __1, ref ActionsReturnClass __result)
        {
            if (__1 == null || __1.Id?.StartsWith("isb_cb_office_plant_", StringComparison.Ordinal) != true)
                return true;
            EmergencyPlantActions.TryGetValue(__1, out var action);
            var actions = new List<ActionsTypesClass>(1);
            if (action != null && __1.gameObject.activeInHierarchy && action.Available())
                actions.Add(new ActionsTypesClass { Name = action.Text, Action = () =>
                {
                    if (__1 != null && __1.gameObject.activeInHierarchy && action.Available())
                        action.Install(__0);
                } });
            __result = new ActionsReturnClass
            {
                Actions = actions,
                SelectedAction = actions.Count > 0 ? actions[0] : null
            };
            return false;
        }

        private static Harmony _controlHarmony;
        private static float _nextControlAction;
        internal static void EnableControlMenu(ManualLogSource log)
        {
            if (_controlHarmony != null)
                return;
            try
            {
                _controlHarmony = new Harmony("com.samc137.isbsofextras.facility-menu");
                foreach (var type in new[]
                {
                    typeof(WorldInteractiveObject),
                    typeof(EFT.Interactive.Switch),
                    typeof(PlaceItemTrigger)
                }

                )
                {
                    var parameters = new[]
                    {
                        typeof(GamePlayerOwner),
                        type
                    };
                    var target = typeof(GetActionsClass).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).SingleOrDefault(m => m.ReturnType == typeof(ActionsReturnClass) && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
                    if (target == null)
                        throw new MissingMethodException("Facility control interaction menu");
                    _controlHarmony.Patch(target, prefix: new HarmonyMethod(typeof(LabsTakeoverInfrastructure), type == typeof(PlaceItemTrigger) ? nameof(PlantActions) : nameof(ControlActions)));
                }
            }
            catch (Exception ex)
            {
                DisableControlMenu();
                log?.LogError("ISB SOF: Facility control menu unavailable: " + ex.GetBaseException().Message);
            }
        }

        internal static void DisableControlMenu()
        {
            _controlHarmony?.UnpatchSelf();
            _controlHarmony = null;
        }

        private static bool IsControlAvailable(TakeoverPanelRole role) => IsContainmentBreachMode && (role == TakeoverPanelRole.MapAnnouncements || role == TakeoverPanelRole.DecontaminationAnnouncements || (role == TakeoverPanelRole.EmergencyLights ? _emergencyLightsAvailable : _definition?.alarm?.enabled == true));
        private static bool ControlActions(GamePlayerOwner __0, WorldInteractiveObject __1, ref ActionsReturnClass __result)
        {
            if (__1 != null && EmergencyActions.TryGetValue(__1, out var callback))
            {
                var officeActions = new List<ActionsTypesClass>(1);
                if (__1.Operatable && __1.gameObject.activeInHierarchy)
                    officeActions.Add(new ActionsTypesClass { Name = (__1 as EFT.Interactive.Switch)?.ContextMenuTip, Action = () =>
                    {
                        if (__1 != null && __1.Operatable && __1.gameObject.activeInHierarchy && EmergencyActions.TryGetValue(__1, out var current) && current == callback)
                            callback(__0);
                    } });
                __result = new ActionsReturnClass
                {
                    Actions = officeActions,
                    SelectedAction = officeActions.Count > 0 ? officeActions[0] : null
                };
                return false;
            }

            if (!_initialized || !(__1 is EFT.Interactive.Switch sw) || !RolesBySwitch.TryGetValue(sw, out var role) || !VisibleRoles.Contains(role) || !sw.gameObject.activeInHierarchy)
                return true;
            if (AuthorizationPending(role) && !ActivatedRoles.Contains(role))
            {
                var action = new ActionsTypesClass
                {
                    Name = ResolveLocalizedText(PromptLocaleKeys[role], PromptFallbacks[role]),
                    Action = () => TryActivateInternal(role, "menu")
                };
                __result = new ActionsReturnClass
                {
                    Actions = new List<ActionsTypesClass>
                    {
                        action
                    },
                    SelectedAction = action
                };
                return false;
            }

            if (role != TakeoverPanelRole.AlarmSilence)
                return true;
            var actions = new List<ActionsTypesClass>(4);
            if (IsControlAvailable(TakeoverPanelRole.AlarmSilence))
                actions.Add(new ActionsTypesClass { Name = ResolveLocalizedText(_alarmDisabledState ? "isb_takeover_alarm_enable" : "isb_takeover_alarm_shutdown", _alarmDisabledState ? "Enable Alarm" : "Deactivate Alarm"), Action = () => TryActivateInternal(TakeoverPanelRole.AlarmSilence, "menu") });
            if (IsControlAvailable(TakeoverPanelRole.EmergencyLights))
                actions.Add(new ActionsTypesClass { Name = ResolveLocalizedText(_emergencyLightsDisabledState ? "isb_takeover_emergency_lights_on" : "isb_takeover_emergency_lights_off", _emergencyLightsDisabledState ? "Turn On Emergency Lights" : "Turn Off Emergency Lights"), Action = () => TryActivateInternal(TakeoverPanelRole.EmergencyLights, "menu") });
            if (IsControlAvailable(TakeoverPanelRole.MapAnnouncements))
                actions.Add(new ActionsTypesClass { Name = ResolveLocalizedText(_mapAnnouncementsMuted ? "isb_map_pa_on" : "isb_map_pa_off", _mapAnnouncementsMuted ? "Enable Map Announcements" : "Disable Map Announcements"), Action = () => TryActivateInternal(TakeoverPanelRole.MapAnnouncements, "menu") });
            if (IsControlAvailable(TakeoverPanelRole.DecontaminationAnnouncements))
                actions.Add(new ActionsTypesClass { Name = ResolveLocalizedText(_decontaminationAnnouncementsMuted ? "isb_decon_pa_on" : "isb_decon_pa_off", _decontaminationAnnouncementsMuted ? "Enable Decontamination Announcements" : "Disable Decontamination Announcements"), Action = () => TryActivateInternal(TakeoverPanelRole.DecontaminationAnnouncements, "menu") });
            __result = new ActionsReturnClass
            {
                Actions = actions,
                SelectedAction = actions.Count > 0 ? actions[0] : null
            };
            return false;
        }

        private static void RegisterPanel(EFT.Interactive.Switch nativeTemplate, TakeoverPanelRole role, TakeoverPanelPointDefinition point, bool visible)
        {
            if (point == null || Panels.ContainsKey(role))
                return;
            GameObject staging = new GameObject(ObjectPrefix + "Staging_" + role);
            staging.SetActive(false);
            GameObject root = UnityEngine.Object.Instantiate(nativeTemplate.gameObject, staging.transform);
            if (root == null)
            {
                UnityEngine.Object.Destroy(staging);
                return;
            }

            root.SetActive(false);
            root.transform.SetParent(null, true);
            UnityEngine.Object.Destroy(staging);
            string identity = ObjectPrefix + role;
            root.name = identity;
            root.transform.SetPositionAndRotation(point.Position, point.Rotation);
            EFT.Interactive.Switch sw = root.GetComponent<EFT.Interactive.Switch>();
            if (sw == null)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }

            PrepareIndependentSwitch(sw, role, identity);
            int nativeColliderCount = PrepareInvisibleNativeSwitch(root, point.interactionRadius);
            if (nativeColliderCount == 0)
            {
                _log?.LogError("ISB SOF Takeover: Cloned EFT switch for " + role + " has no inherited collider; the control may not be interactable.");
            }

            ApplyLocalizedInteractionText(sw, role);
            TakeoverPanelHandle handle = new TakeoverPanelHandle(role, root, sw);
            Panels[role] = handle;
            RolesBySwitch[sw] = role;
            sw.OnDoorStateChanged += HandleNativeSwitchState;
            root.SetActive(true);
            SetPanelVisible(role, visible);
        }

        private static void PrepareIndependentSwitch(EFT.Interactive.Switch sw, TakeoverPanelRole role, string identity)
        {
            sw.Id = PromptLocaleKeys.TryGetValue(role, out string localeKey) ? localeKey : identity;
            sw.ExfiltrationPoint = null;
            sw.Door = null;
            sw.PreviousSwitch = null;
            sw.NextSwitches = Array.Empty<EFT.Interactive.Switch.SwitchAndOperation>();
            sw.AutoTurnOff = false;
            sw.DoorState = EDoorState.Shut;
            sw.Operatable = true;
            sw.InteractionDirection = Vector3.zero;
            sw.InteractionDot = -1f;
        }

        private static int PrepareInvisibleNativeSwitch(GameObject root, float interactionRadius)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = false;
                renderer.forceRenderingOff = true;
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            foreach (Light light in lights)
                light.enabled = false;
            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particle in particles)
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var emission = particle.emission;
                emission.enabled = false;
            }

            AudioSource[] audioSources = root.GetComponentsInChildren<AudioSource>(true);
            foreach (AudioSource audioSource in audioSources)
            {
                audioSource.Stop();
                audioSource.enabled = false;
            }

            Collider[] inherited = root.GetComponentsInChildren<Collider>(true);
            Collider donorCollider = null;
            int usable = 0;
            foreach (Collider collider in inherited)
            {
                if (collider == null || !collider.enabled)
                    continue;
                usable++;
                if (donorCollider == null)
                    donorCollider = collider;
            }

            int interactionLayer = donorCollider != null ? donorCollider.gameObject.layer : root.layer;
            bool isTrigger = donorCollider == null || donorCollider.isTrigger;
            BoxCollider interactionCollider = root.GetComponent<BoxCollider>();
            if (interactionCollider == null)
                interactionCollider = root.AddComponent<BoxCollider>();
            foreach (Collider collider in inherited)
            {
                if (collider == null || ReferenceEquals(collider, interactionCollider))
                    continue;
                collider.enabled = false;
            }

            float radius = Mathf.Clamp(interactionRadius, 0.45f, 1.25f);
            float width = Mathf.Clamp(radius * 1.35f, 0.70f, 1.50f);
            float height = Mathf.Clamp(radius * 1.35f, 0.70f, 1.50f);
            float depth = Mathf.Clamp(radius * 0.45f, 0.28f, 0.55f);
            interactionCollider.center = Vector3.zero;
            interactionCollider.size = new Vector3(width, height, depth);
            interactionCollider.isTrigger = isTrigger;
            interactionCollider.enabled = true;
            interactionCollider.gameObject.layer = interactionLayer;
            root.layer = interactionLayer;
            return usable + 1;
        }

        private static void HandleNativeSwitchState(WorldInteractiveObject interactiveObject, EDoorState previousState, EDoorState nextState)
        {
            if (nextState != EDoorState.Open)
                return;
            EFT.Interactive.Switch sw = interactiveObject as EFT.Interactive.Switch;
            if (sw == null || !RolesBySwitch.TryGetValue(sw, out TakeoverPanelRole role))
                return;
            TryActivateInternal(role, "native");
        }

        private static bool TryActivateInternal(TakeoverPanelRole role, string source)
        {
            bool repeatableControl = role == TakeoverPanelRole.AlarmSilence || role == TakeoverPanelRole.EmergencyLights || role == TakeoverPanelRole.MapAnnouncements || role == TakeoverPanelRole.DecontaminationAnnouncements;
            var panelRole = repeatableControl ? TakeoverPanelRole.AlarmSilence : role;
            if (repeatableControl && (!IsControlAvailable(role) || Time.unscaledTime < _nextControlAction))
                return false;
            if (!_initialized || !VisibleRoles.Contains(panelRole) || (!repeatableControl && ActivatedRoles.Contains(role)))
                return false;
            if (!Panels.TryGetValue(panelRole, out TakeoverPanelHandle handle) || handle?.Root == null || handle.Switch == null || !handle.Root.activeInHierarchy)
            {
                return false;
            }

            if (AuthorizationPending(role))
            {
                handle.Switch.DoorState = EDoorState.Shut;
                handle.Switch.Operatable = true;
                if (Time.unscaledTime >= _nextControlAction)
                {
                    _nextControlAction = Time.unscaledTime + 0.3f;
                    if (role == TakeoverPanelRole.PowerAuthorizationD || role == TakeoverPanelRole.ShutdownAuthorizationD)
                        ShowLocalizedMessage("isb_takeover_power_d_locked", "Authorization D unavailable. Complete authorizations: {0}.", role == TakeoverPanelRole.PowerAuthorizationD ? _powerMissing : _shutdownMissing);
                    else
                    {
                        bool gate = role == TakeoverPanelRole.GateAuthorizationC;
                        ShowLocalizedMessage(gate ? "isb_takeover_gate_c_locked" : "isb_takeover_power_master_locked", gate ? "Security Piston C unavailable. Activate pistons: {0}." : "Power Cell activation still pending.", _gateMissing);
                    }
                }

                return false;
            }

            if (!repeatableControl && !ActivatedRoles.Add(role))
                return false;
            if (repeatableControl)
                _nextControlAction = Time.unscaledTime + 0.3f;
            else
                handle.Switch.Operatable = false;
            string action = ActionForRole(role);
            ShowLocalizedFeedback(role);
            _requestAction?.Invoke(action);
            return true;
        }

        private static bool AuthorizationPending(TakeoverPanelRole role)
        {
            if (!IsContainmentBreachMode)
                return false;
            switch (role)
            {
                case TakeoverPanelRole.GateAuthorizationC:
                    return !_gateCReady;
                case TakeoverPanelRole.PowerAuthorizationD:
                    return !_powerDReady;
                case TakeoverPanelRole.ShutdownAuthorizationD:
                    return !_shutdownDReady;
                case TakeoverPanelRole.PowerMaster:
                    return !_powerMasterReady;
                default:
                    return false;
            }
        }

        private static void ApplyLocalizedInteractionText(EFT.Interactive.Switch sw, TakeoverPanelRole role)
        {
            if (sw == null)
                return;
            string key;
            string fallback;
            if (role == TakeoverPanelRole.AlarmSilence && _alarmDisabledState)
            {
                key = "isb_takeover_alarm_enable";
                fallback = "Enable Alarm";
            }
            else if (role == TakeoverPanelRole.EmergencyLights && _emergencyLightsDisabledState)
            {
                key = "isb_takeover_emergency_lights_on";
                fallback = "Turn On Emergency Lights";
            }
            else
            {
                key = PromptLocaleKeys.TryGetValue(role, out string configuredKey) ? configuredKey : string.Empty;
                fallback = PromptFallbacks.TryGetValue(role, out string configuredFallback) ? configuredFallback : role.ToString();
            }

            if (!string.IsNullOrWhiteSpace(key))
                sw.Id = key;
            string text = ResolveLocalizedText(key, fallback);
            sw.ExtractionZoneTip = text;
            sw.ContextMenuTip = text;
            SetStringMember(sw, "LocalizedName", text);
            SetStringMember(sw, "Description", text);
        }

        internal static void ShowTerminalNotice(string key, string fallback) => ShowLocalizedMessage("ISB/Office/" + key, fallback);
        internal static void NotifyRemote(string action, bool accepted)
        {
            foreach (TakeoverPanelRole role in Enum.GetValues(typeof(TakeoverPanelRole)))
                if (ActionForRole(role) == action)
                {
                    if (!accepted)
                        ShowTerminalNotice("CommandBlocked", "Command blocked. Check prerequisites and the current remote operation.");
                    else if (!ShowAnnouncementFeedback(role, true))
                    {
                        PlayControlClick(role);
                        if (FeedbackLocaleKeys.TryGetValue(role, out string key))
                            ShowLocalizedMessage(key, FeedbackFallbacks[role]);
                    }

                    return;
                }

            ShowTerminalNotice(accepted ? "CommandApplied" : "CommandBlocked", accepted ? "Command applied." : "Command blocked. Check prerequisites and the current remote operation.");
        }

        private static bool ShowAnnouncementFeedback(TakeoverPanelRole role, bool afterChange)
        {
            bool decontamination = role == TakeoverPanelRole.DecontaminationAnnouncements;
            if (!decontamination && role != TakeoverPanelRole.MapAnnouncements)
                return false;
            bool muted = decontamination ? _decontaminationAnnouncementsMuted : _mapAnnouncementsMuted;
            bool enabled = afterChange ? !muted : muted;
            string subject = decontamination ? "Decontamination announcements" : "Map announcements";
            ShowLocalizedMessage(AnnouncementAudioSwitches.FeedbackKey(decontamination, enabled), subject + (enabled ? " enabled." : " disabled."));
            PlayControlClick(role);
            return true;
        }

        private static void PlayControlClick(TakeoverPanelRole role)
        {
            if ((role == TakeoverPanelRole.AlarmSilence || role == TakeoverPanelRole.EmergencyLights || role == TakeoverPanelRole.MapAnnouncements || role == TakeoverPanelRole.DecontaminationAnnouncements) && Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonClick);
        }

        private static void ShowLocalizedFeedback(TakeoverPanelRole role)
        {
            if (ShowAnnouncementFeedback(role, false))
                return;
            PlayControlClick(role);
            string key;
            string fallback;
            if (role == TakeoverPanelRole.AlarmSilence && _alarmDisabledState)
            {
                key = "isb_takeover_alarm_enabled";
                fallback = "Facility emergency alarm activated.";
            }
            else if (role == TakeoverPanelRole.EmergencyLights && _emergencyLightsDisabledState)
            {
                key = "isb_takeover_emergency_lights_enabled";
                fallback = "All facility emergency lights have been turned on.";
            }
            else
            {
                key = FeedbackLocaleKeys.TryGetValue(role, out string configuredKey) ? configuredKey : string.Empty;
                fallback = FeedbackFallbacks.TryGetValue(role, out string configuredFallback) ? configuredFallback : string.Empty;
            }

            ShowLocalizedMessage(key, fallback);
        }

        internal static string MissingAuthorizations(bool a, bool b, bool c)
        {
            switch ((a ? 0 : 1) | (b ? 0 : 2) | (c ? 0 : 4))
            {
                case 1:
                    return "A";
                case 2:
                    return "B";
                case 3:
                    return "A, B";
                case 4:
                    return "C";
                case 5:
                    return "A, C";
                case 6:
                    return "B, C";
                case 7:
                    return "A, B, C";
                default:
                    return string.Empty;
            }
        }

        private static void ShowLocalizedMessage(string key, string fallback, string pending = null)
        {
            string text = ResolveLocalizedText(key, fallback);
            if (pending != null)
                text = text.Replace("{0}", pending);
            if (string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                NotificationManagerClass.DisplayMessageNotification(text, ENotificationDurationType.Long);
            }
            catch (Exception)
            {
            }
        }

        private static string ResolveLocalizedText(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
                return fallback ?? string.Empty;
            EnsureLocalizationResolved();
            try
            {
                string value = _localize?.Invoke(key, null);
                if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
                    return value;
            }
            catch
            {
            }

            return fallback ?? key;
        }

        private static void EnsureLocalizationResolved()
        {
            if (_localizationResolved)
                return;
            _localizationResolved = true;
            Type[] types;
            try
            {
                types = typeof(Player).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray();
            }
            catch
            {
                return;
            }

            foreach (Type type in types)
            {
                try
                {
                    MethodInfo method = type.GetMethod("Localized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string) }, null);
                    if (method == null || method.ReturnType != typeof(string) || method.ContainsGenericParameters)
                        continue;
                    _localize = (Func<string, string, string>)Delegate.CreateDelegate(typeof(Func<string, string, string>), method);
                    return;
                }
                catch
                {
                }
            }
        }

        private static void SetStringMember(object target, string memberName, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return;
            Type current = target.GetType();
            while (current != null)
            {
                try
                {
                    PropertyInfo property = current.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
                    {
                        property.SetValue(target, value, null);
                        return;
                    }

                    FieldInfo field = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        field.SetValue(target, value);
                        return;
                    }

                    FieldInfo backingField = current.GetField("<" + memberName + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (backingField != null && backingField.FieldType == typeof(string))
                    {
                        backingField.SetValue(target, value);
                        return;
                    }
                }
                catch
                {
                }

                current = current.BaseType;
            }
        }

        private static string ActionForRole(TakeoverPanelRole role)
        {
            switch (role)
            {
                case TakeoverPanelRole.AlarmSilence:
                    return AlarmAction;
                case TakeoverPanelRole.EmergencyLights:
                    return EmergencyLightsAction;
                case TakeoverPanelRole.MapAnnouncements:
                    return MapAnnouncementsAction;
                case TakeoverPanelRole.DecontaminationAnnouncements:
                    return DecontaminationAnnouncementsAction;
                case TakeoverPanelRole.GateOverride:
                    return GatesAction;
                case TakeoverPanelRole.GateAuthorizationA:
                    return GateAAction;
                case TakeoverPanelRole.GateAuthorizationB:
                    return GateBAction;
                case TakeoverPanelRole.GateAuthorizationC:
                    return GateCAction;
                case TakeoverPanelRole.PowerAuthorizationA:
                    return PowerAAction;
                case TakeoverPanelRole.PowerAuthorizationB:
                    return PowerBAction;
                case TakeoverPanelRole.PowerAuthorizationC:
                    return PowerCAction;
                case TakeoverPanelRole.PowerAuthorizationD:
                    return PowerDAction;
                case TakeoverPanelRole.PowerMaster:
                    return MasterAction;
                case TakeoverPanelRole.ShutdownAuthorizationA:
                    return ShutdownAAction;
                case TakeoverPanelRole.ShutdownAuthorizationB:
                    return ShutdownBAction;
                case TakeoverPanelRole.ShutdownAuthorizationC:
                    return ShutdownCAction;
                case TakeoverPanelRole.ShutdownAuthorizationD:
                    return ShutdownDAction;
                case TakeoverPanelRole.ShutdownMaster:
                    return ShutdownMasterAction;
                default:
                    return string.Empty;
            }
        }

        private static void ApplyEmergencyLightsState(bool disabled)
        {
            _emergencyLightsDisabledState = disabled;
        }

        private static void ApplyAlarmState(bool alarmDisabled)
        {
            bool changed = _alarmDisabledState != alarmDisabled;
            _alarmDisabledState = alarmDisabled;
            if (!Panels.TryGetValue(TakeoverPanelRole.AlarmSilence, out var handle) || handle?.Switch == null)
                return;
            handle.Switch.DoorState = EDoorState.Shut;
            handle.Switch.Operatable = true;
            if (changed)
                ApplyLocalizedInteractionText(handle.Switch, TakeoverPanelRole.AlarmSilence);
        }

        private static void SetConsumed(TakeoverPanelRole role, bool consumed)
        {
            if (!Panels.TryGetValue(role, out TakeoverPanelHandle handle) || handle?.Switch == null)
                return;
            if (consumed)
                ActivatedRoles.Add(role);
            handle.Switch.Operatable = !ActivatedRoles.Contains(role) && VisibleRoles.Contains(role);
        }

        private static void SetPanelVisible(TakeoverPanelRole role, bool visible)
        {
            if (!Panels.TryGetValue(role, out TakeoverPanelHandle handle) || handle?.Root == null || handle.Switch == null)
            {
                return;
            }

            if (!handle.Root.activeSelf)
                handle.Root.SetActive(true);
            if (visible)
                VisibleRoles.Add(role);
            else
                VisibleRoles.Remove(role);
            BoxCollider interactionCollider = handle.Root.GetComponent<BoxCollider>();
            if (interactionCollider != null)
                interactionCollider.enabled = visible;
            handle.Switch.Operatable = visible && !ActivatedRoles.Contains(role);
        }
    }
}
