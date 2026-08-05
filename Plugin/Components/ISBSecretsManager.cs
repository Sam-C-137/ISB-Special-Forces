using Comfort.Common;
using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public static class ISBSecretsManager
    {
        private static readonly HashSet<string> fired = new HashSet<string>();
        private static readonly Dictionary<string, int> chances = new Dictionary<string, int>
        {
            ["isb_secret_expurgo_farol_hunt"] = 20,
            ["isb_secret_bd_clash_hunt"] = 15,
            ["isb_secret_bd_counterattack"] = 100,
            ["isb_secret_massacre_hunt"] = 25,
            ["isb_secret_relic_hunt"] = 10,
            ["isb_secret_local_hunt"] = 10,
        };
        private static readonly Dictionary<string, bool> raidRolls = new Dictionary<string, bool>();

        public static void ResetRaid()
        {
            fired.Clear();
            raidRolls.Clear();
        }

        public static bool IsEnabledForRaid(string triggerId)
        {
            if (!chances.TryGetValue(triggerId, out var chance))
                return true;

            if (raidRolls.TryGetValue(triggerId, out var enabled))
                return enabled;

            enabled = chance >= 100 || (chance > 0 && Random.value * 100f < chance);
            raidRolls[triggerId] = enabled;
            return enabled;
        }

        public static bool Fire(string triggerId)
        {
            if (fired.Contains(triggerId))
                return true;

            if (!IsEnabledForRaid(triggerId))
                return false;

            var eventHandler = Singleton<BotEventHandler>.Instance;

            if (eventHandler == null)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB SECRET] BotEventHandler is unavailable. Trigger '{triggerId}' was not fired and may retry.");
                return false;
            }

            try
            {
                eventHandler.AnyEvent(triggerId);
                fired.Add(triggerId);
                Plugin.LogSource.LogInfo($"[ISB SECRET] Trigger '{triggerId}' fired.");
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB SECRET] Trigger '{triggerId}' failed: {ex.Message}");
                return false;
            }
        }

        public static bool AlreadyFired(string triggerId)
        {
            return fired.Contains(triggerId);
        }
    }
}
