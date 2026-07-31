using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public static class ISBAishiLoyaltyConditionDamageTracker
    {
        private static readonly Dictionary<string, float> DamageByBot = new Dictionary<string, float>();

        private static float _factionTotal;

        public static void Reset()
        {
            DamageByBot.Clear();
            _factionTotal = 0f;
        }

        public static float Accumulate(string victimProfileId, float damage)
        {
            if (damage <= 0f)
                return 0f;

            _factionTotal += damage;

            if (string.IsNullOrEmpty(victimProfileId))
                return _factionTotal;

            float current;

            if (!DamageByBot.TryGetValue(victimProfileId, out current))
                current = 0f;

            current += damage;
            DamageByBot[victimProfileId] = current;

            return current;
        }

        public static float FactionTotal
        {
            get { return _factionTotal; }
        }

        public static float GetBotTotal(string victimProfileId)
        {
            if (string.IsNullOrEmpty(victimProfileId))
                return 0f;

            float current;
            return DamageByBot.TryGetValue(victimProfileId, out current) ? current : 0f;
        }
    }
}
