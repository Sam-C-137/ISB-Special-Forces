using System.Text.Json;
using ISB.Compatibility;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
public sealed class AnomalousLooseLootConfig
{
    public bool enabled { get; set; } = true;
    public double chancePerPoint { get; set; }
    public Dictionary<string, double> weights { get; set; } = new();
}

public sealed class AnomalousBotLootConfig
{
    public bool enabled { get; set; }
    public double chancePerBot { get; set; }
    public List<string> wildTypes { get; set; } = new();
    public List<string> containers { get; set; } = ["Pockets", "TacticalVest", "Backpack"];
    public Dictionary<string, double> weights { get; set; } = new();
}

public sealed class AnomalousItemSpawnsConfig
{
    public bool enabled { get; set; } = true;
    public AnomalousLooseLootConfig looseLoot { get; set; } = new();
    public AnomalousBotLootConfig botLoot { get; set; } = new();
}

public static class AnomalousItemSpawns
{
    public static readonly IReadOnlySet<string> ItemIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "b27508815b9307ccd1bffa36",
        "1800c994e11cfa35b423e8ed",
        "6183f2a51cb013b5ab62a796",
        "fac4f1a658ad562cddde72c6",
        "72eb2bb2416ee48ee80d63d4",
        "a38cbf3abbfdcbedaa1b9a0e",
        "b543125009b989d7287b58ab",
        "9644da05612ad219e7fc4387",
        "cd19a7a32ec3820a6ea48800",
        "1254eb68d298789e43b45ed3",
        "36be503998aadd53701c57e3",
        "f131c988930a86c7e5610c57",
        "45e6cfe53aabe0b5e846cad5",
        "9df4b9812799170d2f7295f7",
        "57d2c307a64e179e8995e061",
        "90c688feeba7c89aa1a3c8a7",
        "4a2e4aaef50cda01112c41b2",
        "73cc246044b1d66dd70aedb7",
        "06f0d5bdc44c4ddcd0759d31",
        "a8d639d499ac25709f630d2c",
        "922129d70c57a265f61ee8b1",
        "d180bb453e8bb2027c5fa540",
        "51574a824fe10af4518191af",
        "0056a97d873bba252113c74b",
        "ae711ed0c5878f74a4264e27",
        "d94f4a94edf1050b40c93cdb",
        "eee72a686853f282c7cd01da",
        "25dd7397974f7d7940ad9742",
        "68c80d9fb3ef768bbe58a072",
        "d88925f5ddd4cd8795d5c9bd",
        "a2c16b836b441dc953652bd6",
        "571e1ce5aa8a32c10b70d206",
        "3ddc22a4b7e5ba67ee38edf9",
        "b7e0639e756abd3bbb872c2c",
        "337052e7f499ba2f52dfa089",
        "397495f6cda093c895814c0f",
        "e57cfb4c1b58a40f9032db70"
    };
    private static bool SpawnItem(string id) => ItemIds.Contains(id) || id == "2b20c487a99515b22b11ff78";
    public static void Validate(AnomalousItemSpawnsConfig config)
    {
        static void Chance(double value)
        {
            if (!double.IsFinite(value) || value < 0 || value > 100)
                throw new ArgumentException("Anomalous spawn chances must be between 0 and 100.");
        }

        static void Weights(Dictionary<string, double> values)
        {
            foreach (var pair in values)
                if (!SpawnItem(pair.Key) || !double.IsFinite(pair.Value) || pair.Value < 0 || pair.Value > 1000000)
                    throw new ArgumentException("Choose registered anomalous items with weights between 0 and 1000000.");
        }

        if (config == null || config.looseLoot?.weights == null || config.botLoot?.weights == null || config.botLoot.wildTypes == null || config.botLoot.containers == null)
            throw new ArgumentException("Keep looseLoot and botLoot, including their weights, roles and containers.");
        Chance(config.looseLoot.chancePerPoint);
        Chance(config.botLoot.chancePerBot);
        Weights(config.looseLoot.weights);
        Weights(config.botLoot.weights);
        foreach (var role in config.botLoot.wildTypes)
            if (string.IsNullOrWhiteSpace(role) || !(ISBSpecialForces.Web.IsbBotCatalog.KnownIsb(NormalizeRole(role)) || ContainmentPocketLoot.IsBlackDivision(role)))
                throw new ArgumentException($"Unknown anomalous loot bot role: {role}.");
        if (config.botLoot.containers.Count == 0 || config.botLoot.containers.Count > 3 || config.botLoot.containers.Distinct(StringComparer.Ordinal).Count() != config.botLoot.containers.Count || config.botLoot.containers.Any(c => c is not ("Pockets" or "TacticalVest" or "Backpack")))
            throw new ArgumentException("Choose unique containers from Pockets, TacticalVest and Backpack, in search order.");
    }

    private static string NormalizeRole(string? role) => ISBSpecialForces.Web.IsbBotCatalog.NormalizeRole(ContainmentPocketLoot.NormalizeRole(GhostBotVariant.Is(role) ? GhostBotVariant.BaseRole(role) : role) ?? string.Empty);
    public static bool EligibleBot(AnomalousItemSpawnsConfig config, string? role) => config.enabled && config.botLoot.enabled && config.botLoot.chancePerBot > 0 && config.botLoot.wildTypes.Any(r => string.Equals(NormalizeRole(r), NormalizeRole(role), StringComparison.OrdinalIgnoreCase));
    public static int ApplyBotLoot(BotBase bot, bool active, string? location, AnomalousItemSpawnsConfig config, Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf, Func<TemplateItem, Upd> createUpd, Random random, string? requestedRole = null)
    {
        if (!active || !string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) || !EligibleBot(config, requestedRole ?? bot.Info?.Settings?.Role) || bot.Inventory?.Items == null || (config.botLoot.chancePerBot < 100 && random.NextDouble() * 100 >= config.botLoot.chancePerBot))
            return 0;
        var candidates = config.botLoot.weights.Where(p => p.Value > 0 && SpawnItem(p.Key) && config.botLoot.containers.Any(container => ContainmentPocketLoot.CanPlaceExtraItem(bot, p.Key, container, resolve, sizeOf))).ToArray();
        if (candidates.Length == 0)
            return 0;
        double roll = random.NextDouble() * candidates.Sum(p => p.Value);
        var selected = candidates[^1].Key;
        foreach (var candidate in candidates)
        {
            roll -= candidate.Value;
            if (roll < 0)
            {
                selected = candidate.Key;
                break;
            }
        }

        foreach (var container in config.botLoot.containers)
        {
            int placed = ContainmentPocketLoot.PlaceExtraItem(bot, selected, container, resolve, sizeOf, createUpd, random);
            if (placed > 0)
                return placed;
        }

        return 0;
    }

    public static bool EnabledForMap(AnomalousItemSpawnsConfig? config) => config?.enabled == true && config.looseLoot is { enabled: true, chancePerPoint: > 0 and <= 100 } && config.looseLoot.weights.Any(p => p.Value > 0 && SpawnItem(p.Key));
    public static LooseLoot PrepareLooseLoot(LooseLoot source, AnomalousItemSpawnsConfig? config, Func<LooseLoot, LooseLoot> clone, Func<MongoId, TemplateItem?> resolve)
    {
        var result = clone(source);
        if (!EnabledForMap(config))
            return result;
        var candidates = config!.looseLoot.weights.Where(p => p.Value > 0 && SpawnItem(p.Key) && resolve(new MongoId(p.Key))?.Properties is { QuestItem: not true, Unlootable: not true }).ToArray();
        if (candidates.Length == 0)
            return result;
        double total = candidates.Sum(p => p.Value), chance = config.looseLoot.chancePerPoint / 100;
        foreach (var point in result.Spawnpoints ?? [])
        {
            var template = point.Template;
            if (template == null || template.IsContainer == true || template.IsAlwaysSpawn == true || template.Items == null || point.ItemDistribution == null)
                continue;
            var items = template.Items.ToList();
            if (items.Count == 0 || items.Any(i => resolve(i.Template)?.Properties is { QuestItem: true } or { Unlootable: true }))
                continue;
            var keys = items.Select(i => i.ComposedKey).ToHashSet(StringComparer.Ordinal);
            var distribution = point.ItemDistribution.Where(p => keys.Contains(p.ComposedKey?.Key)).ToList();
            var ordinary = distribution.Where(p => p.RelativeProbability is> 0 && double.IsFinite(p.RelativeProbability.Value)).ToArray();
            double ordinaryTotal = ordinary.Sum(p => p.RelativeProbability!.Value);
            if (ordinaryTotal <= 0 || !double.IsFinite(ordinaryTotal))
                continue;
            foreach (var entry in distribution)
                entry.RelativeProbability = Math.Max(0, entry.RelativeProbability ?? 0) / ordinaryTotal * (1 - chance);
            foreach (var candidate in candidates)
            {
                string key = "ISB_ANOM_" + candidate.Key;
                items.Add(new SptLootItem { Id = new MongoId(), Template = new MongoId(candidate.Key), ComposedKey = key });
                distribution.Add(new LooseLootItemDistribution { ComposedKey = new ComposedKey { Key = key }, RelativeProbability = chance * candidate.Value / total });
            }

            template.Items = items;
            point.ItemDistribution = distribution;
        }

        return result;
    }
}
