using ISBSpecialForces.Web;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace ISBSpecialForcesServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class ISBScriptedMapSpawns(RandomUtil random)
{
    private readonly object gate = new();
    private readonly Dictionary<List<BossLocationSpawn>, HashSet<BossLocationSpawn>> additions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Session, string Map), Dictionary<string, bool>> rolls = new();

    public void EndRaid(string? session)
    {
        lock (gate)
            foreach (var key in rolls.Keys.Where(key => key.Session == session).ToArray())
                rolls.Remove(key);
    }

    public void Restore(List<BossLocationSpawn> rows)
    {
        lock (gate)
        {
            if (additions.Remove(rows, out var entries)) rows.RemoveAll(entries.Contains);
        }
    }

    public static void Validate(ScriptedMapSpawnConfig? config)
    {
        if (config == null) return;
        if (!float.IsFinite(config.spawnChance) || config.spawnChance < 0 || config.spawnChance > 100)
            throw new ArgumentException("Scripted map spawn chance must be between 0 and 100.");
        if (config.squads == null) throw new ArgumentException("Scripted map squads must be an object.");
        foreach (var (zone, squad) in config.squads)
            if (string.IsNullOrWhiteSpace(zone) || squad == null || squad.formation == null || !IsbBotCatalog.KnownIsb(squad.primaryWildtype) ||
                squad.formation.Any(pair => !IsbBotCatalog.KnownIsb(pair.Key) || pair.Value < 0 || pair.Value > 30) ||
                squad.formation.GetValueOrDefault(squad.primaryWildtype) < 1 || squad.formation.Values.Sum() > 30)
                throw new ArgumentException("Scripted map squads require a known ISB primary operator and 1–30 total operators.");
    }

    public void Apply(string map, MapConfig settings, List<BossLocationSpawn> rows, string? session, Func<BossLocationSpawn, BossLocationSpawn> clone)
    {
        Restore(rows);
        var config = map == "terminal" ? settings.terminalScriptedSpawns : map == "icebreaker" ? settings.icebreakerScriptedSpawns : null;
        Validate(config);
        if (config?.enabled != true || config.spawnChance == 0) return;
        lock (gate)
        {
            Dictionary<string, bool>? plan = null;
            if (!string.IsNullOrWhiteSpace(session) && !rolls.TryGetValue((session, map), out plan))
            {
                plan = new(StringComparer.OrdinalIgnoreCase);
                foreach (var zone in config.squads.Keys)
                    plan[zone] = config.spawnChance >= 100 || random.GetInt(0, 999999) < config.spawnChance * 10000;
                rolls[(session, map)] = plan;
            }
            var added = new HashSet<BossLocationSpawn>(ReferenceEqualityComparer.Instance);
            foreach (var (zone, squad) in config.squads)
            {
                if (!squad.enabled || plan != null && !plan.GetValueOrDefault(zone)) continue;
                int index = rows.FindIndex(row => string.Equals(row.BossZone, zone, StringComparison.OrdinalIgnoreCase) && !IsbBotCatalog.KnownIsb(row.BossName ?? ""));
                if (index < 0) continue;
                var original = rows[index];
                var replacement = clone(original);
                replacement.BossName = squad.primaryWildtype;
                replacement.BossDifficulty = "impossible";
                replacement.BossChance = plan == null ? config.spawnChance : 100;
                replacement.ForceSpawn = plan == null ? false : original.ForceSpawn;
                replacement.BossEscortAmount = "0";
                replacement.BossEscortDifficulty = "impossible";
                replacement.Supports = squad.formation.Select(pair => (Role: pair.Key, Count: pair.Value - (pair.Key == squad.primaryWildtype ? 1 : 0)))
                    .Where(pair => pair.Count > 0).Select(pair => new BossSupport
                    {
                        BossEscortType = pair.Role,
                        BossEscortAmount = pair.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        BossEscortDifficulty = new ListOrT<string>(["impossible"], null)
                    }).ToList();
                replacement.BossEscortType = replacement.Supports.FirstOrDefault()?.BossEscortType ?? squad.primaryWildtype;
                rows.Add(replacement);
                added.Add(replacement);
            }
            if (added.Count > 0) additions[rows] = added;
        }
    }
}
