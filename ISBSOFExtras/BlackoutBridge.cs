using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal static class BlackoutBridge
    {
        internal const string AutomaticDoorsFileName = "automatic_doors.json";
        internal const string BlackoutSoundFileName = "black_out_huge.wav";
        internal const string GateBExfilName = "lab_Hangar_Gate";
        private sealed class SwitchState
        {
            public EFT.Interactive.Switch Switch;
            public bool Operatable;
        }

        private sealed class ExfilState
        {
            public ExfiltrationPoint Point;
            public EExfiltrationStatus Status;
            public readonly List<SwitchState> Switches = new List<SwitchState>();
        }

        private static readonly HashSet<string> HiddenExfils = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ExfilState> OriginalExfilStates = new Dictionary<string, ExfilState>(StringComparer.Ordinal);
        private static readonly Dictionary<EFT.Interactive.Switch, bool> NativeSwitchStates = new Dictionary<EFT.Interactive.Switch, bool>();
        private static readonly List<EFT.Interactive.Switch> NativeSwitchCache = new List<EFT.Interactive.Switch>();
        private static readonly Dictionary<InteractiveSubscriber.PlaySettings, float> GateAlarmVolumes = new Dictionary<InteractiveSubscriber.PlaySettings, float>();
        private static readonly FieldInfo GatePreOcclusionVolume = typeof(BetterSource).GetField("PreOcclusionVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool _nativeSwitchCacheBuilt;
        private static bool _hideDisabledTransitHudEntries;
        internal static Dictionary<string, Dictionary<int, List<List<int>>>> LoadPathMap(string path, ManualLogSource log, string description)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (log != null)
                {
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        log.LogInfo("ISB SOF Blackout: " + description + " path map was not found.");
                    }
                }

                return new Dictionary<string, Dictionary<int, List<List<int>>>>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<string, Dictionary<int, List<List<int>>>> result = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, List<List<int>>>>>(json);
                if (result == null)
                    result = new Dictionary<string, Dictionary<int, List<List<int>>>>();
                if (log != null)
                {
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        log.LogInfo("ISB SOF Blackout: Loaded " + result.Count + " scene definition from '" + path + "'.");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log.LogError("ISB SOF Blackout: Failed to read " + description + " path map '" + path + "': " + ex.Message);
                }

                return new Dictionary<string, Dictionary<int, List<List<int>>>>();
            }
        }

        internal static bool HasHiddenHudEntries
        {
            get
            {
                return HiddenExfils.Count > 0 || _hideDisabledTransitHudEntries;
            }
        }

        internal static bool HideDisabledTransitHudEntries
        {
            get
            {
                return _hideDisabledTransitHudEntries;
            }
        }

        internal static bool IsExfilHidden(string exfilName)
        {
            return !string.IsNullOrEmpty(exfilName) && !LabsFinalLockdown.PreservesVent(exfilName) && HiddenExfils.Contains(exfilName);
        }

        internal static void ResetHiddenExfils()
        {
            RestoreGateAlarmVolumes();
            RestoreNativeSwitches(null);
            HiddenExfils.Clear();
            OriginalExfilStates.Clear();
            _hideDisabledTransitHudEntries = false;
        }

        internal static void CaptureExfilStates(ManualLogSource log)
        {
            if (OriginalExfilStates.Count > 0)
                return;
            try
            {
                ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
                if (controller == null || controller.ExfiltrationPoints == null)
                    return;
                foreach (ExfiltrationPoint point in controller.ExfiltrationPoints)
                {
                    if (point == null || point.Settings == null || string.IsNullOrEmpty(point.Settings.Name))
                        continue;
                    ExfilState state = new ExfilState
                    {
                        Point = point,
                        Status = point.Status
                    };
                    HashSet<EFT.Interactive.Switch> seen = new HashSet<EFT.Interactive.Switch>();
                    for (EFT.Interactive.Switch sw = point.Switch; sw != null && seen.Add(sw); sw = sw.PreviousSwitch)
                    {
                        state.Switches.Add(new SwitchState { Switch = sw, Operatable = sw.Operatable });
                    }

                    OriginalExfilStates[point.Settings.Name] = state;
                }
            }
            catch (Exception ex)
            {
                log?.LogError("ISB SOF Blackout: Failed to capture Laboratory exfil states: " + ex.Message);
            }
        }

        internal static void IndexDisabledExfilsAndTransits(ManualLogSource log)
        {
            HiddenExfils.Clear();
            _hideDisabledTransitHudEntries = true;
            try
            {
                ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
                if (controller != null && controller.ExfiltrationPoints != null)
                {
                    foreach (ExfiltrationPoint exfil in controller.ExfiltrationPoints)
                    {
                        if (exfil == null || exfil.Settings == null)
                            continue;
                        if (exfil.Status == EExfiltrationStatus.NotPresent && !LabsFinalLockdown.PreservesVent(exfil.Settings.Name))
                            HiddenExfils.Add(exfil.Settings.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                if (log != null)
                    log.LogWarning("ISB SOF Blackout: Failed to index disabled Labs exfils: " + ex.Message);
            }

            LabsBlackoutExfilUiPatcher.HideVisibleEntries(log);
        }

        internal static void ShowExfil(string name, ManualLogSource log)
        {
            if (LabsFinalLockdown.Active && name != LabsFinalLockdown.Vent)
                return;
            HiddenExfils.Remove(name);
            LabsBlackoutExfilUiPatcher.RestoreVisibleEntries(log);
        }

        internal static bool DisableAndHideExfil(string exfilName, ManualLogSource log)
        {
            if (string.IsNullOrEmpty(exfilName) || LabsFinalLockdown.PreservesVent(exfilName))
                return false;
            bool found = false;
            try
            {
                ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
                if (controller != null && controller.ExfiltrationPoints != null)
                {
                    foreach (ExfiltrationPoint exfil in controller.ExfiltrationPoints)
                    {
                        if (exfil == null || exfil.Settings == null)
                            continue;
                        if (!string.Equals(exfil.Settings.Name, exfilName, StringComparison.Ordinal))
                            continue;
                        exfil.Status = EExfiltrationStatus.NotPresent;
                        HiddenExfils.Add(exfilName);
                        found = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log.LogError("ISB SOF Blackout: Failed to disable exfil '" + exfilName + "': " + ex.Message);
                }
            }

            if (found)
            {
                LabsBlackoutExfilUiPatcher.HideVisibleEntries(log);
            }

            return found;
        }

        internal static int LockNativeSwitches()
        {
            if (!_nativeSwitchCacheBuilt)
            {
                _nativeSwitchCacheBuilt = true;
                NativeSwitchCache.Clear();
                foreach (var sw in Resources.FindObjectsOfTypeAll<EFT.Interactive.Switch>())
                {
                    if (sw == null || !sw.gameObject.scene.IsValid() || !sw.gameObject.scene.isLoaded || !sw.gameObject.scene.name.StartsWith("Laboratory_", StringComparison.Ordinal) || sw.name.StartsWith(LabsTakeoverInfrastructure.ObjectPrefix, StringComparison.Ordinal) || sw.name.StartsWith("ISB_EmergencyElevator_", StringComparison.Ordinal) || sw.name.StartsWith("ISB_Office_", StringComparison.Ordinal))
                        continue;
                    NativeSwitchCache.Add(sw);
                }
            }

            foreach (var sw in NativeSwitchCache)
            {
                if (sw == null)
                    continue;
                if (!NativeSwitchStates.ContainsKey(sw))
                    NativeSwitchStates.Add(sw, sw.Operatable);
                sw.Operatable = false;
            }

            return NativeSwitchCache.Count;
        }

        internal static void RestoreNativeSwitches(ManualLogSource log)
        {
            if (NativeSwitchStates.Count == 0)
            {
                NativeSwitchCache.Clear();
                _nativeSwitchCacheBuilt = false;
                return;
            }

            int restored = 0;
            foreach (KeyValuePair<EFT.Interactive.Switch, bool> pair in NativeSwitchStates)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.Operatable = pair.Value;
                restored++;
            }

            NativeSwitchStates.Clear();
            NativeSwitchCache.Clear();
            _nativeSwitchCacheBuilt = false;
        }

        internal static int ActivateGateExfils(IEnumerable<string> gateNames, ManualLogSource log, bool keepVanillaLocked = true, int alarmMaxDistance = 120, float alarmVolumeMultiplier = 2.5f)
        {
            if (gateNames == null)
                return 0;
            HashSet<string> wanted = new HashSet<string>(gateNames, StringComparer.Ordinal);
            if (wanted.Count == 0)
                return 0;
            int activated = 0;
            ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
            if (controller == null || controller.ExfiltrationPoints == null)
                return 0;
            foreach (ExfiltrationPoint point in controller.ExfiltrationPoints)
            {
                if (point == null || point.Settings == null || !wanted.Contains(point.Settings.Name))
                    continue;
                Dictionary<EFT.Interactive.Switch, bool> gateSwitches = new Dictionary<EFT.Interactive.Switch, bool>();
                HashSet<EFT.Interactive.Switch> seen = new HashSet<EFT.Interactive.Switch>();
                for (EFT.Interactive.Switch current = point.Switch; current != null && seen.Add(current); current = current.PreviousSwitch)
                {
                    gateSwitches[current] = current.Operatable;
                    if (keepVanillaLocked && !NativeSwitchStates.ContainsKey(current))
                        NativeSwitchStates[current] = current.Operatable;
                    current.Operatable = true;
                }

                try
                {
                    ConfigureGateAlarm(point.Settings.Name, alarmMaxDistance, alarmVolumeMultiplier);
                    EFT.Interactive.Switch sw = point.Switch;
                    if (sw != null && sw.DoorState == EDoorState.Shut)
                    {
                        sw.LockForInteraction();
                        sw.Interact(new InteractionResult(EInteractionType.Open));
                    }

                    EExfiltrationStatus target = sw != null ? sw.TargetStatus : EExfiltrationStatus.RegularMode;
                    point.ExternalSetStatus(target);
                    if (!LabsFinalLockdown.Blocks(point))
                        HiddenExfils.Remove(point.Settings.Name);
                    activated++;
                }
                catch (Exception ex)
                {
                    log?.LogError("ISB SOF Takeover: Gate activation failed for '" + point.Settings.Name + "': " + ex.Message);
                }
                finally
                {
                    foreach (KeyValuePair<EFT.Interactive.Switch, bool> gateSwitch in gateSwitches)
                    {
                        if (gateSwitch.Key != null)
                            gateSwitch.Key.Operatable = !keepVanillaLocked && gateSwitch.Value;
                    }
                }
            }

            if (activated > 0)
                LabsBlackoutExfilUiPatcher.RestoreVisibleEntries(log);
            return activated;
        }

        internal static void ForceEvacuationExfils(IEnumerable<string> gates, ManualLogSource log)
        {
            var controller = ExfiltrationControllerClass.Instance;
            if (controller?.ExfiltrationPoints == null)
                return;
            var wanted = new HashSet<string>(gates, StringComparer.Ordinal);
            foreach (var point in controller.ExfiltrationPoints)
            {
                if (point?.Settings == null || !wanted.Contains(point.Settings.Name))
                    continue;
                point.gameObject.SetActive(true);
                point.Reusable = true;
                if (point.Status != EExfiltrationStatus.RegularMode && point.Status != EExfiltrationStatus.Countdown)
                    point.ExternalSetStatus(EExfiltrationStatus.RegularMode);
                HiddenExfils.Remove(point.Settings.Name);
            }

            LabsBlackoutExfilUiPatcher.RestoreVisibleEntries(log);
        }

        internal static int ClampGateAlarmDistance(int distance) => Math.Max(1, Math.Min(300, distance));
        internal static float ClampGateAlarmVolumeMultiplier(float multiplier)
        {
            return float.IsNaN(multiplier) || float.IsInfinity(multiplier) ? 1.5f : Math.Max(0f, Math.Min(3f, multiplier));
        }

        private static void ApplyGateAlarmVolume(InteractiveSubscriber.PlaySettings sound, float multiplier)
        {
            if (!GateAlarmVolumes.TryGetValue(sound, out float original))
            {
                original = sound.Volume;
                GateAlarmVolumes.Add(sound, original);
            }

            float previous = sound.Volume;
            sound.Volume = original * ClampGateAlarmVolumeMultiplier(multiplier);
            var source = GetGateAlarmSource(sound);
            if (source == null)
                return;
            source.SetBaseVolume(sound.Volume);
            if (GatePreOcclusionVolume != null)
            {
                float volume = (float)GatePreOcclusionVolume.GetValue(source);
                GatePreOcclusionVolume.SetValue(source, previous > 0f ? volume * (sound.Volume / previous) : source.BaseVolume);
            }

            source.UpdateSourceVolume(1f);
        }

        private static void RestoreGateAlarmVolumes()
        {
            foreach (var entry in GateAlarmVolumes)
                entry.Key.Volume = entry.Value;
            GateAlarmVolumes.Clear();
        }

        private static BetterSource GetGateAlarmSource(InteractiveSubscriber.PlaySettings sound)
        {
            var source = sound.Source;
            return source != null && source.source1 != null && source.source1.clip == sound.Clip ? source : null;
        }

        private static void ConfigureGateAlarm(string gateName, int distance, float volumeMultiplier)
        {
            string rootName = gateName == "lab_Hangar_Gate" ? "Hangar_Gate" : gateName == "lab_Parking_Gate" ? "Parking_Gate" : null;
            if (rootName == null)
                return;
            var scene = SceneManager.GetSceneByName("Laboratory_Collector_Exit");
            if (!scene.isLoaded)
                return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null || root.name != rootName)
                    continue;
                var alarm = root.transform.Find("Alarm");
                var subscriber = alarm != null ? alarm.GetComponent<InteractiveSubscriber>() : null;
                if (subscriber?.Sounds == null)
                    continue;
                foreach (var sound in subscriber.Sounds)
                {
                    if (sound == null || !sound.Loop)
                        continue;
                    ApplyGateAlarmVolume(sound, volumeMultiplier);
                    sound.MaxDistance = ClampGateAlarmDistance(distance);
                    var source = GetGateAlarmSource(sound);
                    if (source != null)
                    {
                        source.SetRolloff(sound.MaxDistance);
                        var culling = Singleton<GameWorld>.Instance?.AudioSourceCulling;
                        if (sound.EnableAudioCulling && culling != null)
                        {
                            culling.Remove(source.source1);
                            culling.Register(source.source1, sound.Loop);
                        }
                    }
                }
            }
        }

        internal static int RestoreElevatorExfils(ManualLogSource log)
        {
            int restored = 0;
            ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
            if (controller == null || controller.ExfiltrationPoints == null)
                return 0;
            foreach (ExfiltrationPoint point in controller.ExfiltrationPoints)
            {
                if (point == null || point.Settings == null || !LooksLikeElevator(point))
                    continue;
                try
                {
                    ExfilState state;
                    OriginalExfilStates.TryGetValue(point.Settings.Name, out state);
                    if (state != null)
                    {
                        foreach (SwitchState switchState in state.Switches)
                        {
                            if (switchState.Switch != null)
                                switchState.Switch.Operatable = !NativeSwitchStates.ContainsKey(switchState.Switch);
                        }
                    }
                    else if (point.Switch != null)
                    {
                        for (EFT.Interactive.Switch sw = point.Switch; sw != null; sw = sw.PreviousSwitch)
                            sw.Operatable = !NativeSwitchStates.ContainsKey(sw);
                    }

                    EExfiltrationStatus restoredStatus = state != null ? state.Status : EExfiltrationStatus.UncompleteRequirements;
                    if (restoredStatus == EExfiltrationStatus.NotPresent)
                        restoredStatus = EExfiltrationStatus.UncompleteRequirements;
                    point.ExternalSetStatus(restoredStatus);
                    if (!LabsFinalLockdown.Blocks(point))
                        HiddenExfils.Remove(point.Settings.Name);
                    restored++;
                }
                catch (Exception ex)
                {
                    log?.LogError("ISB SOF Takeover: Elevator restoration failed for '" + point.Settings.Name + "': " + ex.Message);
                }
            }

            if (restored > 0)
                LabsBlackoutExfilUiPatcher.RestoreVisibleEntries(log);
            return restored;
        }

        private static bool LooksLikeElevator(ExfiltrationPoint point)
        {
            if (point == null)
                return false;
            if (ContainsElevator(point.Settings?.Name) || ContainsElevator(point.gameObject?.name))
                return true;
            EFT.Interactive.Switch sw = point.Switch;
            HashSet<EFT.Interactive.Switch> seen = new HashSet<EFT.Interactive.Switch>();
            while (sw != null && seen.Add(sw))
            {
                if (ContainsElevator(sw.Id) || ContainsElevator(sw.gameObject?.name))
                    return true;
                sw = sw.PreviousSwitch;
            }

            Transform current = point.transform;
            for (int i = 0; current != null && i < 8; i++, current = current.parent)
            {
                if (ContainsElevator(current.name))
                    return true;
            }

            return false;
        }

        private static bool ContainsElevator(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return value.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("lift", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
