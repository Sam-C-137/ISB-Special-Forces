using Vector3 = SPTarkov.Server.Core.Models.Eft.Common.XYZ;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
public class ContainmentMapLootConfig
{
    public bool enabled { get; set; } = true;
    public List<ContainmentLootGroup> groups { get; set; } = new();
}

public class ContainmentLootGroup
{
    public string id { get; set; } = string.Empty;
    public bool enabled { get; set; } = true;
    public string item { get; set; } = string.Empty;
    public Dictionary<string, double> items { get; set; } = new();
    public int count { get; set; } = 1;
    public double chance { get; set; } = 100;
    public List<ContainmentLootPoint> positions { get; set; } = new();
}

public class ContainmentLootPoint
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
    public float rotationX { get; set; }
    public float rotationY { get; set; }
    public float rotationZ { get; set; }
    internal bool Valid => new[]
    {
        x,
        y,
        z,
        rotationX,
        rotationY,
        rotationZ
    }.All(float.IsFinite);
}

public static class ContainmentMapLoot
{
    public static void Validate(ContainmentMapLootConfig? config)
    {
        if (config?.groups == null || config.groups.Count > 128)
            throw new ArgumentException("Map loot must contain at most 128 groups.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in config.groups)
        {
            if (g == null || string.IsNullOrWhiteSpace(g.id) || g.id.Length > 100 || !ids.Add(g.id))
                throw new ArgumentException("Map loot groups require unique IDs of up to 100 characters.");
            if (!double.IsFinite(g.chance) || g.chance < 0 || g.chance > 100 || g.count < 1 || g.count > 256)
                throw new ArgumentException("Map loot chance must be 0–100 and count must be 1–256.");
            if (!g.enabled)
                continue;
            if (g.positions == null || g.positions.Count > 256 || g.positions.Any(p => p == null || !p.Valid) || g.positions.Select(p => (p.x, p.y, p.z)).Distinct().Count() < g.count)
                throw new ArgumentException("Map loot needs at least count distinct valid positions, up to 256.");
            if (g.items == null || (g.items.Count == 0 && !MongoId.IsValidMongoId(g.item)) || g.items.Any(p => !MongoId.IsValidMongoId(p.Key) || !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 1000000) || (g.items.Count > 0 && !g.items.Any(p => p.Value > 0)))
                throw new ArgumentException("Map loot needs an item or a weighted item pool with positive weights.");
        }
    }

    public static int Apply(LocationBase location, string map, bool eventActive, bool generateLoot, ContainmentMapLootConfig? config, Func<MongoId, TemplateItem?> resolve, Func<TemplateItem, Upd> createUpd, Random random, string idPrefix = "ISB_CB_LOOT_")
    {
        if (!eventActive || !generateLoot || config?.enabled != true || config.groups == null || !string.Equals(map, "laboratory", StringComparison.OrdinalIgnoreCase))
            return 0;
        var loot = location.Loot?.ToList() ?? [];
        var used = loot.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        int added = 0;
        foreach (var group in config.groups.Take(128))
        {
            if (group?.enabled != true || string.IsNullOrWhiteSpace(group.id) || group.id.Length > 100 || group.count < 1 || group.count > 256 || !double.IsFinite(group.chance) || group.chance <= 0 || group.chance > 100)
                continue;
            string prefix = idPrefix + group.id;
            string SlotId(int slot) => slot == 0 ? prefix : prefix + "_" + slot;
            var expectedIds = Enumerable.Range(0, group.count).Select(SlotId).ToHashSet(StringComparer.Ordinal);
            var existing = loot.Where(p => p.Id != null && expectedIds.Contains(p.Id)).ToArray();
            if (existing.Length >= group.count)
                continue;
            var positions = group.positions?.Where(p => p != null && p.Valid).DistinctBy(p => (p.x, p.y, p.z)).Where(p => !existing.Any(e => e.Position?.X == p.x && e.Position?.Y == p.y && e.Position?.Z == p.z)).Take(256).ToList();
            if (positions == null || positions.Count == 0)
                continue;
            var weights = group.items is { Count: > 0 } ? group.items : new Dictionary<string, double>
            {
                [group.item] = 1
            };
            var pool = weights.Where(p => MongoId.IsValidMongoId(p.Key) && p.Value > 0 && double.IsFinite(p.Value)).Select(p => (Template: resolve(p.Key), Weight: p.Value)).Where(p => p.Template?.Properties is { QuestItem: not true, Unlootable: not true }).ToArray();
            double total = pool.Sum(p => p.Weight);
            if (pool.Length == 0 || !double.IsFinite(total))
                continue;
            if (existing.Length == 0 && group.chance < 100 && random.NextDouble() * 100 >= group.chance)
                continue;
            for (int slot = 0; slot < group.count && positions.Count > 0; slot++)
            {
                string id = SlotId(slot);
                if (used.Contains(id))
                    continue;
                int index = random.Next(positions.Count);
                var point = positions[index];
                positions[index] = positions[^1];
                positions.RemoveAt(positions.Count - 1);
                double roll = random.NextDouble() * total;
                var template = pool[^1].Template!;
                foreach (var candidate in pool)
                {
                    roll -= candidate.Weight;
                    if (roll < 0)
                    {
                        template = candidate.Template!;
                        break;
                    }
                }

                MongoId itemId = new();
                var upd = createUpd(template) ?? new Upd();
                upd.SpawnedInSession = true;
                upd.StackObjectsCount = 1;
                loot.Add(new SpawnpointTemplate { Id = id, IsContainer = false, UseGravity = true, RandomRotation = false, IsAlwaysSpawn = true, IsGroupPosition = false, Position = new Vector3 { X = point.x, Y = point.y, Z = point.z }, Rotation = new Vector3 { X = point.rotationX, Y = point.rotationY, Z = point.rotationZ }, Root = itemId.ToString(), Items = [new SptLootItem { Id = itemId, Template = template.Id, Upd = upd }] });
                used.Add(id);
                added++;
            }
        }

        if (added > 0)
            location.Loot = loot;
        return added;
    }
}
