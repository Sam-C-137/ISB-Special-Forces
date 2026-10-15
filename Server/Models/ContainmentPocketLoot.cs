using System.Text.Json;
using ISB.Compatibility;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
public class ContainmentPocketLootConfig
{
    public bool enabled { get; set; }
    public ContainmentKeycardUses? keycardRemainingUses { get; set; } = new();
    public Dictionary<string, ContainmentPocketLootRole> wildTypes { get; set; } = new();
}

public class ContainmentKeycardUses
{
    public int min { get; set; } = 2;
    public int max { get; set; } = 4;
}

public class ContainmentPocketLootRole : ContainmentPocketLootRule
{
    public ContainmentPocketLootRule? pockets { get; set; }
    public ContainmentPocketLootRule? backpack { get; set; }
}

public class ContainmentPocketLootRule
{
    public bool enabled { get; set; } = true;
    public string? item { get; set; }
    public double chance { get; set; } = 100;
    public int count { get; set; } = 1;
    public int maxItems { get; set; } = 2;
    public Dictionary<string, double>? pool { get; set; }
    public Dictionary<string, double>? oneOf { get; set; }
    public Dictionary<string, double>? independent { get; set; }
    public bool reserveOneOfSlot { get; set; }
}

public static class ContainmentPocketLoot
{
    public static string? NormalizeRole(string? role) => role?.Trim() switch
    {
        "848420" => "blackDivLead",
        "848421" => "blackDivAssault",
        "848422" => "blackDivBreacher",
        "848423" => "blackDivSupport",
        "848424" => "bossWedge",
        "848426" => "blackDivIb",
        var value => value
    };
    public static bool IsBlackDivision(string? role) => NormalizeRole(role)?.StartsWith("blackDiv", StringComparison.OrdinalIgnoreCase) == true || string.Equals(NormalizeRole(role), "bossWedge", StringComparison.OrdinalIgnoreCase) || GhostBotVariant.Is(role);
    public static bool Applies(bool active, string? location, string? role) => active && string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) && IsBlackDivision(role);
    private static bool ValidId(string? value) => value?.Length == 24 && value.All(Uri.IsHexDigit);
    private static bool ValidChance(double value) => double.IsFinite(value) && value > 0 && value <= 100;
    private static bool Roll(double chance, Random random) => chance >= 100 || random.NextDouble() * 100 < chance;
    public static int Apply(BotBase bot, bool active, string? location, ContainmentPocketLootConfig? config, Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf, Func<TemplateItem, Upd> createUpd, Random random, bool eventOnly = true, bool blackDivisionOnly = true, string? requestedRole = null)
    {
        var role = NormalizeRole(requestedRole ?? bot.Info?.Settings?.Role);
        if (config?.enabled != true || config.wildTypes == null || !(eventOnly ? active && string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) && (!blackDivisionOnly || IsBlackDivision(role)) : IsBlackDivision(role)) || bot.Inventory?.Items == null)
            return 0;
        var rule = config.wildTypes.FirstOrDefault(p => string.Equals(NormalizeRole(p.Key), role, StringComparison.OrdinalIgnoreCase)).Value;
        if (rule == null)
            config.wildTypes.TryGetValue("*", out rule);
        if (rule?.enabled != true)
            return 0;
        var keycard = new MongoId("69439100a320344f805ba61f");
        var keycardTemplate = resolve(keycard);
        if (keycardTemplate != null)
            foreach (var item in bot.Inventory.Items)
                if (item.Template == keycard)
                {
                    item.Upd ??= new Upd();
                    NormalizeKeycardUses(item.Upd, keycardTemplate, config.keycardRemainingUses, random);
                }

        Upd CreateLootUpd(TemplateItem template)
        {
            var upd = createUpd(template) ?? new Upd();
            NormalizeKeycardUses(upd, template, config.keycardRemainingUses, random);
            return upd;
        }

        var pocketRule = rule.pockets ?? (rule.backpack == null ? rule : null);
        return ApplyContainer(bot, pocketRule, "Pockets", resolve, sizeOf, CreateLootUpd, random) + ApplyContainer(bot, rule.backpack, "Backpack", resolve, sizeOf, CreateLootUpd, random);
    }

    private static void NormalizeKeycardUses(Upd upd, TemplateItem template, ContainmentKeycardUses? uses, Random random)
    {
        int capacity = template.Properties?.MaximumNumberOfUsage ?? 0;
        if (template.Id.ToString() != "69439100a320344f805ba61f" || capacity <= 0 || uses is not { min: > 0 } || uses.max < uses.min)
            return;
        int min = Math.Min(uses.min, capacity), max = Math.Min(uses.max, capacity);
        int remaining = capacity - (upd.Key?.NumberOfUsages ?? 0);
        if (upd.Key != null && remaining >= min && remaining <= max)
            return;
        remaining = min == max ? min : (int)random.NextInt64(min, (long)max + 1);
        upd.Key ??= new UpdKey();
        upd.Key.NumberOfUsages = capacity - remaining;
    }

    public static bool CanPlaceExtraItem(BotBase bot, string item, string slot, Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf)
    {
        var inventory = bot.Inventory?.Items;
        if (inventory == null)
            return false;
        var id = new MongoId(item);
        var properties = resolve(id)?.Properties;
        if (properties is not { QuestItem: not true, Unlootable: not true, Width: > 0, Height: > 0 })
            return false;
        var container = inventory.FirstOrDefault(i => i.SlotId == slot && i.ParentId == bot.Inventory!.Equipment?.ToString());
        if (container == null)
            return false;
        return ReadGrids(container, inventory, resolve, sizeOf).Any(g => Allowed(id, g.Grid, resolve) && Find(g.Cells, properties.Width.Value, properties.Height.Value) != null);
    }

    public static int PlaceExtraItem(BotBase bot, string item, string slot, Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf, Func<TemplateItem, Upd> createUpd, Random random) => ApplyContainer(bot, new ContainmentPocketLootRule { item = item, chance = 100, count = 1 }, slot, resolve, sizeOf, createUpd, random);
    private static int ApplyContainer(BotBase bot, ContainmentPocketLootRule? rule, string slot, Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf, Func<TemplateItem, Upd> createUpd, Random random)
    {
        if (rule?.enabled != true)
            return 0;
        bool specific = !string.IsNullOrWhiteSpace(rule.item);
        if (specific && rule.pool?.Count > 0)
            return 0;
        if (specific && (!ValidId(rule.item) || !ValidChance(rule.chance) || rule.count is < 1 or > 64))
            return 0;
        bool hasPool = rule.pool != null && rule.maxItems is >= 1 and <= 64;
        bool hasChoice = rule.oneOf?.Count > 0;
        bool hasIndependent = rule.independent?.Count > 0;
        if (!specific && !hasPool && !hasChoice && !hasIndependent)
            return 0;
        var inventory = bot.Inventory!.Items!;
        var container = inventory.FirstOrDefault(i => i.SlotId == slot && i.ParentId == bot.Inventory.Equipment?.ToString());
        if (container == null)
            return 0;
        if (hasIndependent)
            foreach (var existing in inventory.Where(i => i.ParentId == container.Id.ToString() && rule.independent!.TryGetValue(i.Template.ToString(), out var chance) && ValidChance(chance) && chance < 100 && !inventory.Any(child => child.ParentId == i.Id.ToString())).ToArray())
                inventory.Remove(existing);
        var available = ReadGrids(container, inventory, resolve, sizeOf);
        if (available.Count == 0)
            return 0;
        bool Reserve(MongoId id)
        {
            var properties = resolve(id)?.Properties;
            if (properties == null || properties.QuestItem == true || properties.Unlootable == true || properties.Width is not > 0 || properties.Height is not > 0)
                return false;
            foreach (var child in inventory.Where(i => i.ParentId == container.Id.ToString()).ToArray())
            {
                if (inventory.Any(i => i.ParentId == child.Id.ToString()) || (rule.oneOf?.ContainsKey(child.Template.ToString()) == true || rule.independent?.ContainsKey(child.Template.ToString()) == true) || resolve(child.Template)?.Properties?.QuestItem == true)
                    continue;
                var cleared = ReadGrids(container, inventory, resolve, sizeOf, child.Id);
                if (!cleared.Any(g => Allowed(id, g.Grid, resolve) && Find(g.Cells, properties.Width.Value, properties.Height.Value) != null))
                    continue;
                bool moved = false;
                foreach (var destination in inventory.Where(i => i.ParentId == bot.Inventory.Equipment?.ToString() && i.Id != container.Id && (i.SlotId == "Backpack" || i.SlotId == "TacticalVest")).ToArray())
                {
                    var size = sizeOf(child);
                    if (size.Width <= 0 || size.Height <= 0)
                        continue;
                    foreach (var g in ReadGrids(destination, inventory, resolve, sizeOf))
                    {
                        if (!Allowed(child.Template, g.Grid, resolve))
                            continue;
                        var spot = Find(g.Cells, size.Width, size.Height);
                        if (spot == null)
                            continue;
                        child.ParentId = destination.Id.ToString();
                        child.SlotId = g.Grid.Name;
                        child.Location = spot;
                        moved = true;
                        break;
                    }

                    if (moved)
                        break;
                }

                if (!moved)
                    inventory.Remove(child);
                available = cleared;
                return true;
            }

            return false;
        }

        int Place(MongoId id, int count)
        {
            var template = resolve(id);
            var properties = template?.Properties;
            if (properties == null || properties.QuestItem == true || properties.Unlootable == true || properties.Width is not > 0 || properties.Height is not > 0)
                return 0;
            int added = 0, stackLimit = Math.Max(1, properties.StackMaxSize ?? 1);
            while (added < count)
            {
                int stack = Math.Min(count - added, stackLimit);
                bool placed = false;
                foreach (var(grid, cells)in available)
                {
                    if (!Allowed(id, grid, resolve))
                        continue;
                    var spot = Find(cells, properties.Width.Value, properties.Height.Value);
                    if (spot == null)
                        continue;
                    var upd = createUpd(template!) ?? new Upd();
                    upd.SpawnedInSession = true;
                    upd.StackObjectsCount = stack;
                    inventory.Add(new Item { Id = new MongoId(), Template = id, ParentId = container.Id.ToString(), SlotId = grid.Name, Location = spot, Upd = upd });
                    var size = (int)spot.R == 1 ? (properties.Height.Value, properties.Width.Value) : (properties.Width.Value, properties.Height.Value);
                    Fill(cells, spot.X!.Value, spot.Y!.Value, size.Item1, size.Item2);
                    added += stack;
                    placed = true;
                    break;
                }

                if (!placed)
                    break;
            }

            return added;
        }

        int independentAdded = 0;
        if (hasIndependent)
            foreach (var entry in rule.independent!.Take(128))
            {
                if (!ValidId(entry.Key) || !ValidChance(entry.Value) || !Roll(entry.Value, random))
                    continue;
                if (inventory.Any(i => i.ParentId == container.Id.ToString() && i.Template.ToString() == entry.Key))
                    continue;
                var id = new MongoId(entry.Key);
                int placed = Place(id, 1);
                if (placed == 0 && Reserve(id))
                    placed = Place(id, 1);
                independentAdded += placed;
            }

        int guaranteed = 0;
        MongoId? chosen = null;
        if (hasChoice)
        {
            var choices = rule.oneOf!.Take(128).Where(p => ValidId(p.Key) && ValidChance(p.Value)).ToList();
            if (rule.reserveOneOfSlot)
            {
                var existing = inventory.FirstOrDefault(i => i.ParentId == container.Id.ToString() && choices.Any(c => c.Key == i.Template.ToString()));
                if (existing != null)
                {
                    chosen = existing.Template;
                    choices.Clear();
                }
            }

            while (choices.Count > 0)
            {
                double roll = random.NextDouble() * choices.Sum(p => p.Value);
                int index = choices.Count - 1;
                for (int i = 0; i < choices.Count; i++)
                {
                    roll -= choices[i].Value;
                    if (roll < 0)
                    {
                        index = i;
                        break;
                    }
                }

                var id = new MongoId(choices[index].Key);
                choices.RemoveAt(index);
                if (Place(id, 1) == 0 && !(rule.reserveOneOfSlot && Reserve(id) && Place(id, 1) > 0))
                    continue;
                guaranteed = 1;
                chosen = id;
                break;
            }
        }

        if (specific)
            return independentAdded + guaranteed + (chosen?.ToString() != rule.item && Roll(rule.chance, random) ? Place(new MongoId(rule.item!), rule.count) : 0);
        if (!hasPool)
            return independentAdded + guaranteed;
        var candidates = new List<MongoId>();
        var seen = new HashSet<MongoId>();
        foreach (var entry in rule.pool!.Take(128))
        {
            if (!ValidId(entry.Key) || !ValidChance(entry.Value))
                continue;
            var id = new MongoId(entry.Key);
            if (rule.independent?.ContainsKey(entry.Key) != true && id != chosen && seen.Add(id) && Roll(entry.Value, random))
                candidates.Add(id);
        }

        var order = candidates.ToArray();
        random.Shuffle(order);
        int result = 0;
        foreach (var id in order)
        {
            if (result >= rule.maxItems)
                break;
            result += Place(id, 1);
        }

        return independentAdded + guaranteed + result;
    }

    private static List<(Grid Grid, bool[, ] Cells)> ReadGrids(Item container, List<Item> inventory, Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf, MongoId? excluded = null)
    {
        var available = new List<(Grid Grid, bool[, ] Cells)>();
        var grids = resolve(container.Template)?.Properties?.Grids;
        if (grids == null)
            return available;
        foreach (var grid in grids)
        {
            int width = grid.Properties?.CellsH ?? 0, height = grid.Properties?.CellsV ?? 0;
            if (width is <= 0 or > 32 || height is <= 0 or > 32 || string.IsNullOrEmpty(grid.Name))
                continue;
            var cells = new bool[width, height];
            bool valid = true;
            foreach (var child in inventory.Where(i => i.Id != excluded && i.ParentId == container.Id.ToString() && i.SlotId == grid.Name))
            {
                var position = ReadLocation(child.Location);
                if (position?.X == null || position.Y == null)
                {
                    valid = false;
                    break;
                }

                var size = sizeOf(child);
                if ((int)position.R == 1)
                    size = (size.Height, size.Width);
                int x = position.X.Value, y = position.Y.Value;
                if (size.Width <= 0 || size.Height <= 0 || x < 0 || y < 0 || x + size.Width > width || y + size.Height > height)
                {
                    valid = false;
                    break;
                }

                Fill(cells, x, y, size.Width, size.Height);
            }

            if (valid)
                available.Add((grid, cells));
        }

        return available;
    }

    private static ItemLocation? ReadLocation(object? value)
    {
        if (value is ItemLocation location)
            return location;
        try
        {
            return value == null ? null : JsonSerializer.Deserialize<ItemLocation>(JsonSerializer.Serialize(value));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool Allowed(MongoId tpl, Grid grid, Func<MongoId, TemplateItem?> resolve)
    {
        var filters = grid.Properties?.Filters?.ToArray();
        if (filters == null || filters.Length == 0)
            return true;
        var ancestors = new HashSet<MongoId>();
        for (int depth = 0; depth < 32 && ancestors.Add(tpl); depth++)
        {
            var item = resolve(tpl);
            if (item == null)
                break;
            tpl = item.Parent;
        }

        if (filters.Any(f => f.ExcludedFilter?.Overlaps(ancestors) == true))
            return false;
        return filters.Any(f => f.Filter == null || f.Filter.Count == 0 || f.Filter.Overlaps(ancestors));
    }

    private static ItemLocation? Find(bool[, ] cells, int width, int height)
    {
        for (int rotation = 0; rotation < 2; rotation++)
        {
            int w = rotation == 0 ? width : height, h = rotation == 0 ? height : width;
            for (int y = 0; y <= cells.GetLength(1) - h; y++)
                for (int x = 0; x <= cells.GetLength(0) - w; x++)
                {
                    bool free = true;
                    for (int yy = y; yy < y + h && free; yy++)
                        for (int xx = x; xx < x + w; xx++)
                            if (cells[xx, yy])
                            {
                                free = false;
                                break;
                            }

                    if (free)
                        return new ItemLocation
                        {
                            X = x,
                            Y = y,
                            R = (ItemRotation)rotation
                        };
                }
        }

        return null;
    }

    private static void Fill(bool[, ] cells, int x, int y, int width, int height)
    {
        for (int yy = y; yy < y + height; yy++)
            for (int xx = x; xx < x + width; xx++)
                cells[xx, yy] = true;
    }
}
