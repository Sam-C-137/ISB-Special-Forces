using System;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public static class ISBAishiLoyaltyConditionDamageTracker
    {
        private sealed class Totals
        {
            internal readonly Dictionary<string, float> Bots = new Dictionary<string, float>(StringComparer.Ordinal);
            internal float Faction;
        }

        private static readonly Dictionary<string, Totals> Aggressors = new Dictionary<string, Totals>(StringComparer.Ordinal);
        public static void Reset() => Aggressors.Clear();
        public static float Accumulate(string aggressorProfileId, string victimProfileId, float damage)
        {
            if (string.IsNullOrEmpty(aggressorProfileId) || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
                return 0f;
            if (!Aggressors.TryGetValue(aggressorProfileId, out var totals))
                Aggressors.Add(aggressorProfileId, totals = new Totals());
            totals.Faction += damage;
            if (string.IsNullOrEmpty(victimProfileId))
                return totals.Faction;
            totals.Bots.TryGetValue(victimProfileId, out var current);
            return totals.Bots[victimProfileId] = current + damage;
        }

        public static float GetFactionTotal(string aggressorProfileId) => !string.IsNullOrEmpty(aggressorProfileId) && Aggressors.TryGetValue(aggressorProfileId, out var totals) ? totals.Faction : 0f;
        public static float GetBotTotal(string aggressorProfileId, string victimProfileId) => !string.IsNullOrEmpty(aggressorProfileId) && !string.IsNullOrEmpty(victimProfileId) && Aggressors.TryGetValue(aggressorProfileId, out var totals) && totals.Bots.TryGetValue(victimProfileId, out var total) ? total : 0f;
    }
}
