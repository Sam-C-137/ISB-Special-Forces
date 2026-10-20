using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public class ISBForcedHostilityResponse
    {
        public bool enabled { get; set; }
        public bool logs { get; set; }
        public List<string> friendlyBotTypes { get; set; } = new List<string>();
    }

    public static class ISBForcedHostilityState
    {
        public static bool Enabled;
        public static bool Logs;
        private static readonly HashSet<WildSpawnType> Friendly = new HashSet<WildSpawnType>();
        public static void InitRaid()
        {
            Enabled = false;
            Logs = false;
            Friendly.Clear();
            try
            {
                var json = RequestHandler.GetJson("/ISB/forcedHostility");
                if (string.IsNullOrEmpty(json))
                {
                    Plugin.LogSource.LogWarning("[ISB] ForcedHostility route returned null. Disabled for this raid.");
                    return;
                }

                var response = JsonConvert.DeserializeObject<ISBForcedHostilityResponse>(json);
                if (response == null)
                {
                    Plugin.LogSource.LogWarning("[ISB] Could not parse forcedHostility response. Disabled for this raid.");
                    return;
                }

                Enabled = response.enabled;
                Logs = response.logs;
                if (response.friendlyBotTypes != null)
                {
                    foreach (var entry in response.friendlyBotTypes)
                    {
                        if (string.IsNullOrWhiteSpace(entry))
                            continue;
                        if (System.Enum.TryParse<WildSpawnType>(entry, true, out var parsedName))
                        {
                            Friendly.Add(parsedName);
                            continue;
                        }

                        if (int.TryParse(entry, out var parsedValue))
                        {
                            Friendly.Add((WildSpawnType)parsedValue);
                            continue;
                        }

                        Plugin.LogSource.LogWarning($"[ISB] Unknown friendlyBotTypes entry '{entry}' ignored.");
                    }
                }

                if (Enabled)
                {
                    Plugin.LogSource.LogInfo($"[ISB] Forced hostility active. ISB is hostile to every bot type except {Friendly.Count} whitelisted.");
                }
            }
            catch (System.Exception ex)
            {
                Enabled = false;
                Plugin.LogSource.LogError($"[ISB] Failed to load forcedHostility state: {ex.Message}");
            }
        }

        public static bool IsFriendlyToISB(WildSpawnType role)
        {
            return WildSpawnTypeExtensions.IsISB(role) || Friendly.Contains(role);
        }

        public static void OnBotActivated(BotOwner botOwner) => ISBEventHostility.OnBotActivated(botOwner);
    }
}
