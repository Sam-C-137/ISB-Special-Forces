using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;

namespace ISBSSOFExtra
{
    internal static class BlackoutBridge
    {
        internal const string EmissiveMeshesFileName = "emissive_meshes.json";
        internal const string AutomaticDoorsFileName = "automatic_doors.json";
        internal const string BlackoutSoundFileName = "black_out_huge.wav";
        internal const string GateBExfilName = "lab_Hangar_Gate";

        private static readonly HashSet<string> HiddenExfils =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool _hideDisabledTransitHudEntries;

        internal static Dictionary<string, Dictionary<int, List<List<int>>>> LoadPathMap(
            string path,
            ManualLogSource log,
            string description)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (log != null)
                {
                    log.LogInfo(
                        "ISB SOF Blackout: " + description +
                        " path map was not found.");
                }

                return new Dictionary<string, Dictionary<int, List<List<int>>>>();
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<string, Dictionary<int, List<List<int>>>> result =
                    JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, List<List<int>>>>>(json);

                if (result == null)
                    result = new Dictionary<string, Dictionary<int, List<List<int>>>>();

                if (log != null)
                {
                    log.LogInfo(
                        "ISB SOF Blackout: Loaded " + result.Count +
                        " scene definition from '" + path + "'.");
                }

                return result;
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log.LogWarning(
                        "ISB SOF Blackout: Failed to read " + description +
                        " path map '" + path + "': " + ex.Message);
                }

                return new Dictionary<string, Dictionary<int, List<List<int>>>>();
            }
        }

        internal static bool HasHiddenHudEntries
        {
            get { return HiddenExfils.Count > 0 || _hideDisabledTransitHudEntries; }
        }

        internal static bool HideDisabledTransitHudEntries
        {
            get { return _hideDisabledTransitHudEntries; }
        }

        internal static bool IsExfilHidden(string exfilName)
        {
            return !string.IsNullOrEmpty(exfilName) && HiddenExfils.Contains(exfilName);
        }

        internal static void ResetHiddenExfils()
        {
            HiddenExfils.Clear();
            _hideDisabledTransitHudEntries = false;
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

                        if (exfil.Status == EExfiltrationStatus.NotPresent)
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

        internal static bool DisableAndHideExfil(string exfilName, ManualLogSource log)
        {
            if (string.IsNullOrEmpty(exfilName))
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
                    log.LogWarning(
                        "ISB SOF Blackout: Failed to disable exfil '" + exfilName + "': " + ex.Message);
                }
            }

            if (found)
            {
                LabsBlackoutExfilUiPatcher.HideVisibleEntries(log);

                if (log != null)
                    log.LogInfo("ISB SOF Blackout: Disabled and hid exfil '" + exfilName + "'.");
            }
            else if (log != null)
            {
                log.LogWarning(
                    "ISB SOF Blackout: Exfil '" + exfilName + "' was not found in the current raid.");
            }

            return found;
        }
    }
}
