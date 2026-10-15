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
        private static readonly HashSet<BotsGroup> ScannedGroups = new HashSet<BotsGroup>();
        private static readonly HashSet<BotsGroup> OtherGroups = new HashSet<BotsGroup>();
        private static int _scanFrame = -1;
        public static void InitRaid()
        {
            Enabled = false;
            Logs = false;
            Friendly.Clear();
            ScannedGroups.Clear();
            OtherGroups.Clear();
            _scanFrame = -1;
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

        public static void OnBotActivated(BotOwner botOwner)
        {
            if (!Enabled)
                return;
            if (botOwner?.Profile?.Info?.Settings == null)
                return;
            var group = botOwner.BotsGroup;
            if (group == null)
                return;
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null || gameWorld.AllAlivePlayersList == null)
                return;
            var role = botOwner.Profile.Info.Settings.Role;
            var botIsISB = WildSpawnTypeExtensions.IsISB(role);
            if (!botIsISB && IsFriendlyToISB(role))
                return;
            int frame = UnityEngine.Time.frameCount;
            if (_scanFrame != frame)
            {
                _scanFrame = frame;
                ScannedGroups.Clear();
            }

            if (botIsISB && !ScannedGroups.Add(group))
                return;
            OtherGroups.Clear();
            var added = 0;
            foreach (var person in gameWorld.AllAlivePlayersList)
            {
                if (person == null || !person.IsAI)
                    continue;
                if (person.HealthController == null || !person.HealthController.IsAlive)
                    continue;
                if (person.Profile?.Info?.Settings == null)
                    continue;
                if (person.ProfileId == botOwner.ProfileId)
                    continue;
                var otherRole = person.Profile.Info.Settings.Role;
                if (botIsISB)
                {
                    if (IsFriendlyToISB(otherRole))
                        continue;
                    if (group.AddEnemy(person, EBotEnemyCause.initial))
                        added++;
                    continue;
                }

                if (!WildSpawnTypeExtensions.IsISB(otherRole))
                    continue;
                var otherGroup = person.AIData?.BotOwner?.BotsGroup;
                if (otherGroup == null || otherGroup == group || !OtherGroups.Add(otherGroup))
                    continue;
                if (otherGroup.AddEnemy(botOwner.GetPlayer, EBotEnemyCause.initial))
                    added++;
            }

            if (Logs && added > 0)
            {
                Plugin.LogSource.LogInfo($"[ISB FORCED HOSTILITY] '{botOwner.Profile.Nickname}' ({(int)role}) activated. {added} enemy link(s) set.");
            }
        }
    }
}
