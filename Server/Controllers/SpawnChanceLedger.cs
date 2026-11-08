using System;
using System.Collections.Generic;

namespace ISBSpecialForcesServer.Controllers;
public sealed class SpawnChanceHistory
{
    public Dictionary<string, int> normal { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> revenge { get; set; } = new(StringComparer.Ordinal);
    public string revengeCycle { get; set; } = string.Empty;
    public int containment { get; set; }
    public int blackout { get; set; }
    public int scp173 { get; set; }
}

public sealed class SpawnChanceRaid
{
    public string token { get; set; } = Guid.NewGuid().ToString("N");
    public string owner { get; set; } = string.Empty;
    public string map { get; set; } = string.Empty;
    public string revengeCycle { get; set; } = string.Empty;
    public bool started { get; set; }
    public bool isbEligible { get; set; }
    public bool isbSpawned { get; set; }
    public string labsVariant { get; set; } = "legacy";
    public bool? containmentActive { get; set; }
    public bool? containmentRoll { get; set; }
    public bool blackoutEligible { get; set; }
    public bool blackoutOccurred { get; set; }
    public bool? blackoutSelected { get; set; }
    public bool scp173Eligible { get; set; }
    public bool scp173Spawned { get; set; }
}

public sealed class SpawnChanceLedger
{
    public Dictionary<string, SpawnChanceHistory> profiles { get; set; } = new(StringComparer.Ordinal);

    public static double Chance(double baseline, int misses, int increment) => double.IsNaN(baseline) || baseline <= 0 ? 0 : Math.Min(100, Math.Max(0, baseline) + Math.Clamp(misses, 0, 100 / Math.Max(1, increment)) * increment);
    public SpawnChanceHistory Profile(string owner, string cycle)
    {
        if (!profiles.TryGetValue(owner, out var history))
            profiles[owner] = history = new();
        if (history.revengeCycle != cycle)
        {
            history.revenge.Clear();
            history.revengeCycle = cycle;
        }

        return history;
    }

    public double IsbChance(SpawnChanceRaid raid, double baseline)
    {
        var history = Profile(raid.owner, raid.revengeCycle);
        bool revenge = !string.IsNullOrEmpty(raid.revengeCycle);
        return Chance(baseline, (revenge ? history.revenge : history.normal).GetValueOrDefault(raid.map), revenge ? 20 : 10);
    }

    public static bool IsNormalIsbTrigger(string? trigger) =>
        string.IsNullOrEmpty(trigger) || trigger is "hunt" or "isb_formation" or "isb_formation_hunt" ||
        trigger.StartsWith("isb_config_checkpoint_", StringComparison.Ordinal);

    public void RecordBlackoutRoll(SpawnChanceRaid raid, double baseline, Func<double> roll)
    {
        if (raid.map != "laboratory" || raid.labsVariant != "legacy" || raid.containmentActive != false ||
            raid.blackoutSelected.HasValue || !double.IsFinite(baseline) || baseline is < 0 or > 100)
            return;

        raid.blackoutEligible = baseline > 0;
        raid.blackoutSelected = baseline > 0 &&
            roll() < Chance(baseline, Profile(raid.owner, raid.revengeCycle).blackout, 10);
    }

    private static int NextMiss(int misses, int maximum) => Math.Clamp(misses, 0, maximum - 1) + 1;

    public void Finish(SpawnChanceRaid raid)
    {
        if (!raid.started)
            return;
        var history = Profile(raid.owner, raid.revengeCycle);
        if (raid.isbEligible && raid.containmentActive != true && raid.labsVariant != "blackout" && raid.blackoutSelected != true && !raid.blackoutOccurred)
        {
            var counts = string.IsNullOrEmpty(raid.revengeCycle) ? history.normal : history.revenge;
            counts[raid.map] = raid.isbSpawned ? 0 : NextMiss(counts.GetValueOrDefault(raid.map), 10);
        }

        if (raid.containmentRoll is bool containment)
            history.containment = containment ? 0 : NextMiss(history.containment, 10);
        if (raid.blackoutEligible && raid.blackoutSelected is bool blackout)
            history.blackout = blackout ? 0 : NextMiss(history.blackout, 10);
        if (raid.scp173Eligible)
            history.scp173 = raid.scp173Spawned ? 0 : NextMiss(history.scp173, 20);
    }
}
