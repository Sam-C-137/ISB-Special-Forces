using System;

namespace ISB.Compatibility
{
    public sealed class PopulationTargets
    {
        public int blackDivision { get; set; } = 10;
        public int isb { get; set; } = 4;
        public int chaosInsurgency { get; set; } = 3;
        public int raiders { get; set; }
        public int pmcs { get; set; }
        public int[] Counts() => new[] { blackDivision, isb, chaosInsurgency, raiders, pmcs, 0 };
    }

    public sealed class EventPopulationConfig
    {
        public bool enabled { get; set; } = true;
        public int maximumBots { get; set; } = 17;
        public int squadOverflow { get; set; } = 3;
        public int reinforcementIntervalSeconds { get; set; } = 300;
        public int reinforcementWindowSeconds { get; set; } = 60;
        public int genericRefillDelaySeconds { get; set; } = 15;
        public PopulationTargets normal { get; set; } = new PopulationTargets();
        public PopulationTargets reinforcement { get; set; } = new PopulationTargets { blackDivision = 7, isb = 2, chaosInsurgency = 2, raiders = 3, pmcs = 3 };
        public int performanceMaximumBots { get; set; } = 13;
        public PopulationTargets performanceNormal { get; set; } = new PopulationTargets { blackDivision = 8, isb = 3, chaosInsurgency = 2 };
        public PopulationTargets performanceReinforcement { get; set; } = new PopulationTargets { blackDivision = 6, isb = 2, chaosInsurgency = 1, raiders = 2, pmcs = 2 };
        public int Ceiling => maximumBots + squadOverflow;
        public bool IsReinforcementWindow(double seconds) => seconds >= reinforcementIntervalSeconds && seconds % reinforcementIntervalSeconds < reinforcementWindowSeconds;
        public int[] Targets(double seconds) => (IsReinforcementWindow(seconds) ? reinforcement : normal).Counts();
        public EventPopulationConfig ForPerformanceMode(bool performanceMode)
        {
            if (!performanceMode) return this;
            return new EventPopulationConfig
            {
                enabled = enabled,
                maximumBots = performanceMaximumBots,
                squadOverflow = squadOverflow,
                reinforcementIntervalSeconds = reinforcementIntervalSeconds,
                reinforcementWindowSeconds = reinforcementWindowSeconds,
                genericRefillDelaySeconds = genericRefillDelaySeconds,
                normal = performanceNormal,
                reinforcement = performanceReinforcement,
                performanceMaximumBots = performanceMaximumBots,
                performanceNormal = performanceNormal,
                performanceReinforcement = performanceReinforcement
            };
        }
        public void Validate()
        {
            if (maximumBots < 1 || maximumBots > 100 || squadOverflow < 0 || squadOverflow > 10 ||
                reinforcementIntervalSeconds < 60 || reinforcementIntervalSeconds > 3600 ||
                reinforcementWindowSeconds < 1 || reinforcementWindowSeconds >= reinforcementIntervalSeconds ||
                genericRefillDelaySeconds < 1 || genericRefillDelaySeconds > 600 ||
                performanceMaximumBots < 1 || performanceMaximumBots > maximumBots ||
                normal == null || reinforcement == null || performanceNormal == null || performanceReinforcement == null)
                throw new ArgumentException("Invalid event spawn limit settings.");
            ValidateTargets(normal, maximumBots);
            ValidateTargets(reinforcement, maximumBots);
            ValidateTargets(performanceNormal, performanceMaximumBots);
            ValidateTargets(performanceReinforcement, performanceMaximumBots);
        }
        private static void ValidateTargets(PopulationTargets targets, int limit)
        {
            int sum = 0;
            foreach (int count in targets.Counts())
            {
                if (count < 0 || count > limit) throw new ArgumentException("Faction targets must be between zero and the population limit.");
                sum += count;
            }
            if (sum > limit) throw new ArgumentException("Faction targets exceed the population limit.");
        }
    }

    public static class EventPopulationPolicy
    {
        public static bool IsChaosOnly(int[] counts)
        {
            for (int i = 0; i < counts.Length; i++)
                if (i != 2 && counts[i] > 0) return false;
            return counts.Length > 2 && counts[2] > 0;
        }

        public static bool CanAdmit(int global, int[] current, int[] incoming, int[] targets, EventPopulationConfig config, bool special, bool specialWaiting)
        {
            int amount = 0;
            foreach (int count in incoming) amount += count;
            if (amount <= 0 || global + amount > config.Ceiling) return false;
            if (special) return true;
            if (specialWaiting || global >= config.maximumBots) return false;
            for (int i = 0; i < 5; i++)
                if (incoming[i] > 0 && (targets[i] <= current[i] || current[i] + incoming[i] > targets[i] + config.squadOverflow)) return false;
            return true;
        }
    }
}
