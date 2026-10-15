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
    public bool containmentEligible { get; set; }
    public bool containmentOccurred { get; set; }
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

    public void Finish(SpawnChanceRaid raid)
    {
        if (!raid.started)
            return;
        var history = Profile(raid.owner, raid.revengeCycle);
        if (raid.isbEligible || raid.isbSpawned)
        {
            var counts = string.IsNullOrEmpty(raid.revengeCycle) ? history.normal : history.revenge;
            counts[raid.map] = raid.isbSpawned ? 0 : Math.Min(10, counts.GetValueOrDefault(raid.map) + 1);
        }

        if (raid.containmentEligible)
            history.containment = raid.containmentOccurred ? 0 : Math.Min(10, history.containment + 1);
        if (raid.blackoutEligible)
            history.blackout = raid.blackoutOccurred ? 0 : Math.Min(10, history.blackout + 1);
        if (raid.scp173Eligible)
            history.scp173 = raid.scp173Spawned ? 0 : Math.Min(20, history.scp173 + 1);
    }
}
