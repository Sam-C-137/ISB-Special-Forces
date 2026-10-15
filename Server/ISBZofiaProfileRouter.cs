using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace ISBSpecialForcesServer;
[Injectable]
public sealed class ISBZofiaProfileRouter : SaveLoadRouter
{
    internal const string LegacyRole = "ISBBerserker";
    internal const string CurrentRole = "ISBFollowerZofia";
    protected override List<HandledRoute> GetHandledRoutes() => [new("isb-zofia-profile", false)];
    protected override SptProfile HandleLoadInternal(SptProfile profile)
    {
        Migrate(profile.CharacterData?.PmcData);
        Migrate(profile.CharacterData?.ScavData);
        return profile;
    }

    internal static void Migrate(PmcData? character)
    {
        var stats = character?.Stats?.Eft;
        if (stats == null)
            return;
        foreach (var victim in stats.Victims ?? [])
        {
            if (!IsLegacy(victim.Role))
                continue;
            victim.Role = CurrentRole;
        }

        if (stats.Aggressor != null && IsLegacy(stats.Aggressor.Role))
            stats.Aggressor.Role = CurrentRole;
        MigrateCounters(stats.SessionCounters?.Items);
        MigrateCounters(stats.OverallCounters?.Items);
    }

    private static bool IsLegacy(string? value) => string.Equals(value, LegacyRole, StringComparison.OrdinalIgnoreCase);
    private static void MigrateCounters(IEnumerable<CounterKeyValue>? counters)
    {
        if (counters == null)
            return;
        foreach (var counter in counters)
        {
            if (counter.Key == null)
                continue;
            if (counter.Key.RemoveWhere(IsLegacy) > 0)
                counter.Key.Add(CurrentRole);
        }
    }
}
