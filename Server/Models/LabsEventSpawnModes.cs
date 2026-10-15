using System.Runtime.CompilerServices;
using SPTarkov.Server.Core.Utils.Json;
using ISB.Compatibility;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
internal static class LabsEventSpawnModes
{
    private sealed record State(string Boss, string Difficulty, string Escort, string EscortDifficulty, string Amount, List<BossSupport>? Supports);
    private static readonly ConditionalWeakTable<BossLocationSpawn, State> Originals = new();
    internal static void Restore(IEnumerable<BossLocationSpawn> spawns)
    {
        foreach (var spawn in spawns)
            if (Originals.TryGetValue(spawn, out var state))
            {
                spawn.BossName = state.Boss;
                spawn.BossDifficulty = state.Difficulty;
                spawn.BossEscortType = state.Escort;
                spawn.BossEscortDifficulty = state.EscortDifficulty;
                spawn.BossEscortAmount = state.Amount;
                spawn.Supports = state.Supports;
                Originals.Remove(spawn);
            }
    }
    private static bool Affected(string role, LabsPreBlackoutEventConfig config) =>
        LabsEventModes.Role(role, config, () => 0) != role || LabsEventModes.Role(role, config, () => .9999) != role;
    internal static void Apply(IEnumerable<BossLocationSpawn> spawns, LabsPreBlackoutEventConfig config)
    {
        if (!config.ghostBusterOnly && !config.whiteTuskOnly && !config.noISBMode && !config.noBlackDivMode)
            return;
        LabsEventModes.Validate(config);
        foreach (var spawn in spawns)
        {
            if (Originals.TryGetValue(spawn, out _) || !Affected(spawn.BossName, config) && !Affected(spawn.BossEscortType, config) && spawn.Supports?.Any(s => Affected(s.BossEscortType, config)) != true)
                continue;
            Originals.Add(spawn, new(spawn.BossName, spawn.BossDifficulty, spawn.BossEscortType, spawn.BossEscortDifficulty, spawn.BossEscortAmount, spawn.Supports?.ToList()));
            var boss = LabsEventModes.Role(GhostBotVariant.FromMarker(spawn.BossDifficulty, spawn.BossName) ?? spawn.BossName, config, Random.Shared.NextDouble);
            spawn.BossName = GhostBotVariant.Is(boss) ? GhostBotVariant.BaseRole(boss) : boss;
            if (GhostBotVariant.Is(boss))
                spawn.BossDifficulty = GhostBotVariant.Marker(boss);
            else if (GhostBotVariant.FromMarker(spawn.BossDifficulty, boss) != null)
                spawn.BossDifficulty = "impossible";
            var supports = new List<BossSupport>();
            void Add(string role, string amount, ListOrT<string> difficulty)
            {
                if (string.IsNullOrWhiteSpace(amount) || amount.Trim() == "0")
                    return;
                if (!Affected(role, config))
                {
                    supports.Add(new BossSupport { BossEscortType = role, BossEscortAmount = amount, BossEscortDifficulty = difficulty });
                    return;
                }
                var variants = amount.Split(',').Select(x => int.TryParse(x.Trim(), out var n) ? (int?)n : null).ToArray();
                if (variants.Length == 0 || variants.Any(x => x == null || x < 0))
                    throw new InvalidDataException("ISB: unsupported escort count: " + amount);
                var total = variants[Random.Shared.Next(variants.Length)]!.Value;
                for (var i = 0; i < total; i++)
                {
                    var target = LabsEventModes.Role(role, config, Random.Shared.NextDouble);
                    supports.Add(new BossSupport { BossEscortType = GhostBotVariant.Is(target) ? GhostBotVariant.BaseRole(target) : target, BossEscortAmount = "1", BossEscortDifficulty = GhostBotVariant.Is(target) ? new ListOrT<string>([GhostBotVariant.Marker(target)], null) : string.Equals(target, role, StringComparison.OrdinalIgnoreCase) ? difficulty : new ListOrT<string>(["impossible"], null) });
                }
            }
            if (spawn.Supports?.Any() == true)
                foreach (var support in spawn.Supports)
                    Add(support.BossEscortType, support.BossEscortAmount, support.BossEscortDifficulty);
            else
                Add(GhostBotVariant.FromMarker(spawn.BossEscortDifficulty, spawn.BossEscortType) ?? spawn.BossEscortType, spawn.BossEscortAmount, new ListOrT<string>([spawn.BossEscortDifficulty], null));
            spawn.Supports = supports;
            spawn.BossEscortAmount = "0";
            if (supports.Count > 0)
                spawn.BossEscortType = supports[0].BossEscortType;
            spawn.BossEscortDifficulty = "impossible";
        }
    }
}
