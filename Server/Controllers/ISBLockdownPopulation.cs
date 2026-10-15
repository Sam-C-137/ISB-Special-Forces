using SPTarkov.Server.Core.Models.Eft.Common;

namespace ISBSpecialForcesServer.Controllers;
public static class ISBLockdownPopulation
{
    public static bool Applies(string? map, bool active) => active && string.Equals(map, "laboratory", StringComparison.OrdinalIgnoreCase);
    public static int Apply(LocationBase location, List<BossLocationSpawn> eventSpawns, bool blockPmcs, bool blockRaiders)
    {
        location.BossLocationSpawn ??= [];
        var protectedIds = eventSpawns.Where(x => !string.IsNullOrEmpty(x.SptId)).Select(x => x.SptId).ToHashSet(StringComparer.Ordinal);
        var presentIds = location.BossLocationSpawn.Where(x => !string.IsNullOrEmpty(x.SptId)).Select(x => x.SptId).ToHashSet(StringComparer.Ordinal);
        foreach (var spawn in eventSpawns)
            if (!string.IsNullOrEmpty(spawn.SptId) && presentIds.Add(spawn.SptId))
                location.BossLocationSpawn.Add(spawn);
        var removed = location.BossLocationSpawn.RemoveAll(x => !protectedIds.Contains(x.SptId ?? "") && IsBlocked(x.BossName, blockPmcs, blockRaiders));
        removed += location.Waves?.RemoveAll(x => IsBlocked(x.WildSpawnType?.ToString(), blockPmcs, blockRaiders)) ?? 0;
        return removed;
    }

    private static bool IsBlocked(string? role, bool pmcs, bool raiders) => (pmcs && (string.Equals(role, "pmcUSEC", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "pmcBEAR", StringComparison.OrdinalIgnoreCase))) || (raiders && string.Equals(role, "pmcBot", StringComparison.OrdinalIgnoreCase));
}
