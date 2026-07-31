using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ISB.MixedAmmo
{
    public static class ISBMixedAmmoState
    {
        private static readonly ConcurrentDictionary<string, Dictionary<string, CaliberIntDefinition>> Registry =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, bool> BotRollCache = new();


        public static void Clear(string botRole)
        {
            Registry.TryRemove(botRole, out _);
        }

        public static void ClearAll()
        {
            Registry.Clear();
            BotRollCache.Clear();
        }

        public static void Merge(string botRole, Dictionary<string, CaliberIntDefinition>? mixes)
        {
            if (mixes == null || mixes.Count == 0)
            {
                return;
            }

            var target = Registry.GetOrAdd(botRole, _ => new Dictionary<string, CaliberIntDefinition>(StringComparer.OrdinalIgnoreCase));

            lock (target)
            {
                foreach (var kvp in mixes)
                {
                    target[kvp.Key] = kvp.Value;
                }
            }
        }

        public static bool TryGet(string botRole, string caliber, out CaliberIntDefinition mix)
        {
            mix = null!;

            if (!Registry.TryGetValue(botRole, out var byCaliber))
            {
                return false;
            }

            lock (byCaliber)
            {
                return byCaliber.TryGetValue(caliber, out mix!) && mix.entries.Count > 0;
            }
        }

        public static bool RollForBot(string botId, string caliber, int chance)
        {
            if (chance >= 100)
            {
                return true;
            }

            if (chance <= 0)
            {
                return false;
            }

            if (BotRollCache.Count > 512)
            {
                BotRollCache.Clear();
            }

            var key = $"{botId}:{caliber}";
            return BotRollCache.GetOrAdd(key, _ => Random.Shared.Next(100) < chance);
        }
    }
}
