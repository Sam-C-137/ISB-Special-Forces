#nullable enable
using System;
using System.Collections.Generic;

namespace ISB.Compatibility
{
    public sealed class PopulationTargets
    {
        public int blackDivision { get; set; } = 11;
        public int isb { get; set; } = 4;
        public int chaosInsurgency { get; set; } = 3;
        public int raiders { get; set; }
        public int pmcs { get; set; }
        public int[] Counts() => new[] { blackDivision, isb, chaosInsurgency, raiders, pmcs, 0 };
    }

    public sealed class EventPopulationConfig
    {
        public bool enabled { get => true; set { } }
        public int maximumBots { get; set; } = 18;
        public int squadOverflow { get; set; }
        public int reinforcementIntervalSeconds { get; set; } = 300;
        public int reinforcementWindowSeconds { get; set; } = 120;
        public int genericRefillDelaySeconds { get; set; } = 15;
        public PopulationTargets normal { get; set; } = new PopulationTargets();
        public PopulationTargets reinforcement { get; set; } = new PopulationTargets { blackDivision = 7, isb = 0, raiders = 2, pmcs = 6 };
        public int performanceMaximumBots { get; set; } = 13;
        public PopulationTargets performanceNormal { get; set; } = new PopulationTargets { blackDivision = 6 };
        public PopulationTargets performanceReinforcement { get; set; } = new PopulationTargets { blackDivision = 2, isb = 0, raiders = 4, pmcs = 4 };
        public int Ceiling => maximumBots + squadOverflow;
        public bool IsReinforcementWindow(double seconds) =>
            seconds % (reinforcementIntervalSeconds + reinforcementWindowSeconds) >= reinforcementIntervalSeconds;
        public static EventPopulationConfig BlackoutDefaults() => new EventPopulationConfig
        {
            normal = new PopulationTargets { blackDivision = 8, isb = 4, chaosInsurgency = 0 },
            reinforcement = new PopulationTargets { blackDivision = 4, isb = 2, chaosInsurgency = 0, raiders = 3, pmcs = 6 },
            performanceNormal = new PopulationTargets { blackDivision = 6, isb = 2, chaosInsurgency = 0 },
            performanceReinforcement = new PopulationTargets { blackDivision = 3, isb = 2, chaosInsurgency = 0, raiders = 2, pmcs = 4 }
        };
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
        public void Validate(string path = "eventSpawnQueue")
        {
            var errors = new List<string>();
            CheckRange(maximumBots, 1, 100, path + ".maximumBots", errors);
            CheckRange(squadOverflow, 0, 10, path + ".squadOverflow", errors);
            CheckRange(reinforcementIntervalSeconds, 60, 3600, path + ".reinforcementIntervalSeconds", errors);
            CheckRange(reinforcementWindowSeconds, 1, 3600, path + ".reinforcementWindowSeconds", errors);
            CheckRange(genericRefillDelaySeconds, 1, 600, path + ".genericRefillDelaySeconds", errors);
            CheckRange(performanceMaximumBots, 1, maximumBots, path + ".performanceMaximumBots", errors);
            ValidateTargets(normal, maximumBots, path + ".normal", errors);
            ValidateTargets(reinforcement, maximumBots, path + ".reinforcement", errors);
            ValidateTargets(performanceNormal, performanceMaximumBots, path + ".performanceNormal", errors);
            ValidateTargets(performanceReinforcement, performanceMaximumBots, path + ".performanceReinforcement", errors);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join("; ", errors));
        }
        private static readonly string[] Factions = { "blackDivision", "isb", "chaosInsurgency", "raiders", "pmcs" };
        private static void CheckRange(int value, int minimum, int maximum, string path, List<string> errors)
        {
            if (value < minimum || value > maximum)
                errors.Add(path + " = " + value + ": use a whole number between " + minimum + " and " + maximum + ".");
        }
        private static void ValidateTargets(PopulationTargets? targets, int limit, string path, List<string> errors)
        {
            if (targets == null)
            {
                errors.Add(path + ": expected an object with faction targets.");
                return;
            }
            long sum = 0;
            var counts = targets.Counts();
            for (int i = 0; i < Factions.Length; i++)
            {
                CheckRange(counts[i], 0, limit, path + "." + Factions[i], errors);
                sum += counts[i];
            }
            if (sum > limit)
                errors.Add(path + ": faction targets total " + sum + ", but the population limit is " + limit + ".");
        }
        public static EventPopulationConfig Recover(EventPopulationConfig? value, string path, Action<string>? warning = null, EventPopulationConfig? defaults = null)
        {
            var changes = new List<string>();
            defaults ??= new EventPopulationConfig();
            if (value == null)
            {
                value = defaults;
                changes.Add(path + ": missing queue; using the default queue.");
            }
            value.maximumBots = RepairRange(value.maximumBots, 1, 100, path + ".maximumBots", changes);
            value.squadOverflow = RepairRange(value.squadOverflow, 0, 10, path + ".squadOverflow", changes);
            value.reinforcementIntervalSeconds = RepairRange(value.reinforcementIntervalSeconds, 60, 3600, path + ".reinforcementIntervalSeconds", changes);
            value.reinforcementWindowSeconds = RepairRange(value.reinforcementWindowSeconds, 1, 3600, path + ".reinforcementWindowSeconds", changes);
            value.genericRefillDelaySeconds = RepairRange(value.genericRefillDelaySeconds, 1, 600, path + ".genericRefillDelaySeconds", changes);
            value.performanceMaximumBots = RepairRange(value.performanceMaximumBots, 1, value.maximumBots, path + ".performanceMaximumBots", changes);
            value.normal = RecoverTargets(value.normal, defaults.normal, value.maximumBots, path + ".normal", changes);
            value.reinforcement = RecoverTargets(value.reinforcement, defaults.reinforcement, value.maximumBots, path + ".reinforcement", changes);
            value.performanceNormal = RecoverTargets(value.performanceNormal, defaults.performanceNormal, value.performanceMaximumBots, path + ".performanceNormal", changes);
            value.performanceReinforcement = RecoverTargets(value.performanceReinforcement, defaults.performanceReinforcement, value.performanceMaximumBots, path + ".performanceReinforcement", changes);
            value.Validate(path);
            if (changes.Count > 0)
                warning?.Invoke("[ISB SOF] Invalid spawn settings corrected for this server session: " + string.Join("; ", changes) + " The configuration file was not rewritten. Correct these fields in the control panel or JSON file.");
            return value;
        }
        private static int RepairRange(int value, int minimum, int maximum, string path, List<string> changes)
        {
            int repaired = Math.Max(minimum, Math.Min(maximum, value));
            if (repaired != value)
                changes.Add(path + ": " + value + " -> " + repaired);
            return repaired;
        }
        private static PopulationTargets RecoverTargets(PopulationTargets? value, PopulationTargets fallback, int limit, string path, List<string> changes)
        {
            if (value == null)
            {
                value = fallback;
                changes.Add(path + ": missing targets; using defaults within the configured limit.");
            }
            var original = value.Counts();
            var repaired = new int[Factions.Length];
            int sum = 0;
            for (int i = 0; i < repaired.Length; i++)
            {
                repaired[i] = Math.Max(0, Math.Min(limit, original[i]));
                sum += repaired[i];
            }
            if (sum > limit)
            {
                var remainder = new int[repaired.Length];
                int allocated = 0;
                for (int i = 0; i < repaired.Length; i++)
                {
                    int numerator = repaired[i] * limit;
                    repaired[i] = numerator / sum;
                    remainder[i] = numerator % sum;
                    allocated += repaired[i];
                }
                while (allocated < limit)
                {
                    int best = 0;
                    for (int i = 1; i < remainder.Length; i++)
                        if (remainder[i] > remainder[best]) best = i;
                    repaired[best]++;
                    remainder[best] = -1;
                    allocated++;
                }
            }
            for (int i = 0; i < repaired.Length; i++)
                if (original[i] != repaired[i]) changes.Add(path + "." + Factions[i] + ": " + original[i] + " -> " + repaired[i]);
            value.blackDivision = repaired[0];
            value.isb = repaired[1];
            value.chaosInsurgency = repaired[2];
            value.raiders = repaired[3];
            value.pmcs = repaired[4];
            return value;
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
