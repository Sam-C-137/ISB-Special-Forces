using ISBSpecialForces.Web;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ISBSpecialForcesServer;

public static class PackNStrapBelts
{
    public const string ParentId = "6815465859b8c6ff13f94026";
    public const string ConfigKey = "PackNStrapLoadouts";
    private sealed record Cargo(List<Item> Tree, Item? Move, int Width, int Height);

    public static bool Eligible(string? role) => role != null && IsbBotCatalog.KnownIsb(IsbBotCatalog.NormalizeRole(role));

    public static int Apply(BotBase bot, string role, IReadOnlyList<TemplateItem> belts,
        Func<MongoId, TemplateItem?> resolve, Func<Item, (int Width, int Height)> sizeOf,
        Func<TemplateItem, Upd> updFor, ICloner cloner, Random random,
        Func<MongoId, IReadOnlyList<TemplateItem>>? compatibleMagazines = null)
    {
        var inventory = bot.Inventory?.Items;
        if (!Eligible(role) || inventory == null || belts.Count == 0)
            return 0;
        role = IsbBotCatalog.NormalizeRole(role);
        string equipment = bot.Inventory!.Equipment.ToString();
        var armband = inventory.FirstOrDefault(i => i.ParentId == equipment && i.SlotId == "ArmBand");
        if (armband != null && belts.Any(t => t.Id == armband.Template))
            return 0;
        var magazines = new List<(Item Weapon, Item Magazine, Item Ammunition, int Count)>();
        foreach (var (slot, count) in new[] { ("FirstPrimaryWeapon", 2), ("SecondPrimaryWeapon", 1), ("Holster", 1) })
        {
            var weapon = inventory.FirstOrDefault(i => i.ParentId == equipment && i.SlotId == slot);
            if (weapon == null)
                continue;
            var magazine = inventory.FirstOrDefault(i => i.ParentId == weapon.Id.ToString() && i.SlotId == "mod_magazine");
            var ammunition = magazine == null ? null : inventory.FirstOrDefault(i => i.ParentId == magazine.Id.ToString() && i.SlotId == "cartridges");
            var mode = magazine == null ? null : resolve(magazine.Template)?.Properties?.ReloadMagType?.ToString();
            bool internalMagazine = mode?.Contains("Internal", StringComparison.OrdinalIgnoreCase) == true || mode?.Contains("OnlyBarrel", StringComparison.OrdinalIgnoreCase) == true;
            if (magazine == null || internalMagazine || ammunition == null)
                continue;
            magazines.Add((weapon, magazine, ammunition, count));
        }
        for (int compact = 0; compact < 2; compact++)
        {
            var cargo = new List<Cargo>();
            foreach (var entry in magazines)
            {
                var template = resolve(entry.Magazine.Template);
                var source = Descendants(entry.Magazine, inventory);
                var size = sizeOf(entry.Magazine);
                if (compact != 0 && compatibleMagazines != null)
                {
                    double minimum = Math.Min(20, template?.Properties?.Cartridges?.FirstOrDefault()?.MaxCount ?? 1);
                    var alternative = compatibleMagazines(entry.Weapon.Template)
                        .Where(t => t.Properties?.Width is > 0 && t.Properties.Height is > 0 && t.Properties.Cartridges?.FirstOrDefault() is { MaxCount: > 0 } cartridge
                            && cartridge.MaxCount >= minimum && Accepts(t, entry.Ammunition.Template, resolve))
                        .Select(t => (Template: t, Size: (t.Properties!.Width!.Value, t.Properties.Height!.Value)))
                        .Where(t => t.Size.Item1 > 0 && t.Size.Item2 > 0)
                        .OrderBy(t => t.Size.Item1 * t.Size.Item2)
                        .ThenByDescending(t => t.Template.Properties!.Cartridges!.First().MaxCount).FirstOrDefault();
                    if (alternative.Template != null)
                    {
                        var magazine = new Item { Id = new MongoId(), Template = alternative.Template.Id, Upd = updFor(alternative.Template) };
                        var ammunition = cloner.Clone(entry.Ammunition)!;
                        ammunition.Id = new MongoId();
                        ammunition.ParentId = magazine.Id.ToString();
                        ammunition.SlotId = "cartridges";
                        ammunition.Location = 0;
                        ammunition.Upd ??= new Upd();
                        ammunition.Upd.StackObjectsCount = alternative.Template.Properties!.Cartridges!.First().MaxCount!.Value;
                        source = [magazine, ammunition];
                        size = alternative.Size;
                    }
                }
                if (size.Width <= 0 || size.Height <= 0)
                    return 0;
                for (int index = 0; index < entry.Count; index++)
                    cargo.Add(new(source, null, size.Width, size.Height));
            }
            if (armband != null)
            {
                var size = sizeOf(armband);
                if (size.Width <= 0 || size.Height <= 0)
                    return 0;
                cargo.Add(new(Descendants(armband, inventory), armband, size.Width, size.Height));
            }
            var candidates = belts.ToArray();
            random.Shuffle(candidates);
            foreach (var template in candidates.OrderBy(t => Preference(role, t.Id.ToString())))
            {
                var belt = new Item { Id = new MongoId(), Template = template.Id, ParentId = equipment, SlotId = "ArmBand", Upd = updFor(template) };
                var grids = ContainmentPocketLoot.ReadGrids(belt, [], resolve, sizeOf);
                var placements = new List<(Cargo Cargo, string Grid, ItemLocation Location)>();
                foreach (var entry in cargo.OrderByDescending(c => c.Width * c.Height))
                {
                    foreach (var (grid, cells) in grids.OrderBy(g => g.Cells.Length))
                    {
                        if (!ContainmentPocketLoot.Allowed(entry.Tree[0].Template, grid, resolve))
                            continue;
                        var location = ContainmentPocketLoot.Find(cells, entry.Width, entry.Height);
                        if (location == null)
                            continue;
                        int width = (int)location.R == 1 ? entry.Height : entry.Width;
                        int height = (int)location.R == 1 ? entry.Width : entry.Height;
                        ContainmentPocketLoot.Fill(cells, location.X!.Value, location.Y!.Value, width, height);
                        placements.Add((entry, grid.Name!, location));
                        break;
                    }
                }
                if (placements.Count != cargo.Count)
                    continue;
                var additions = new List<Item> { belt };
                var moves = new List<(Item Item, string Grid, ItemLocation Location)>();
                foreach (var (entry, grid, location) in placements)
                {
                    if (entry.Move != null)
                    {
                        moves.Add((entry.Move, grid, location));
                        continue;
                    }
                    var copies = cloner.Clone(entry.Tree) ?? throw new InvalidOperationException("Unable to copy belt magazines.");
                    var ids = copies.ToDictionary(i => i.Id.ToString(), _ => new MongoId());
                    foreach (var copy in copies)
                    {
                        copy.Id = ids[copy.Id.ToString()];
                        if (copy.ParentId != null && ids.TryGetValue(copy.ParentId, out var parent))
                            copy.ParentId = parent.ToString();
                    }
                    copies[0].ParentId = belt.Id.ToString();
                    copies[0].SlotId = grid;
                    copies[0].Location = location;
                    double capacity = resolve(copies[0].Template)?.Properties?.Cartridges?.FirstOrDefault()?.MaxCount ?? 0;
                    var cartridges = copies.Where(i => i.ParentId == copies[0].Id.ToString() && i.SlotId == "cartridges").ToArray();
                    double loaded = cartridges.Sum(i => i.Upd?.StackObjectsCount ?? 0);
                    if (capacity > loaded && cartridges.Length > 0)
                    {
                        cartridges[0].Upd ??= new Upd();
                        cartridges[0].Upd!.StackObjectsCount = (cartridges[0].Upd!.StackObjectsCount ?? 0) + capacity - loaded;
                    }
                    additions.AddRange(copies);
                }
                foreach (var id in Supplies(role, random))
                {
                    var supply = resolve(new MongoId(id));
                    if (supply?.Properties?.Width is not > 0 || supply.Properties.Height is not > 0)
                        continue;
                    int width = supply.Properties.Width.Value, height = supply.Properties.Height.Value;
                    foreach (var (grid, cells) in grids.OrderBy(g => g.Cells.Length))
                    {
                        if (!ContainmentPocketLoot.Allowed(supply.Id, grid, resolve))
                            continue;
                        var location = ContainmentPocketLoot.Find(cells, width, height);
                        if (location == null)
                            continue;
                        additions.Add(new Item { Id = new MongoId(), Template = supply.Id, ParentId = belt.Id.ToString(), SlotId = grid.Name, Location = location, Upd = updFor(supply) });
                        ContainmentPocketLoot.Fill(cells, location.X!.Value, location.Y!.Value, (int)location.R == 1 ? height : width, (int)location.R == 1 ? width : height);
                        break;
                    }
                }
                foreach (var (item, grid, location) in moves)
                {
                    item.ParentId = belt.Id.ToString();
                    item.SlotId = grid;
                    item.Location = location;
                }
                inventory.AddRange(additions);
                return magazines.Sum(m => m.Count);
            }
        }
        return 0;
    }

    private static bool Accepts(TemplateItem magazine, MongoId ammunition, Func<MongoId, TemplateItem?> resolve)
    {
        var filters = magazine.Properties?.Cartridges?.FirstOrDefault()?.Properties?.Filters;
        var ancestors = new HashSet<MongoId>();
        for (int depth = 0; depth < 32 && ancestors.Add(ammunition); depth++)
        {
            var template = resolve(ammunition);
            if (template == null) break;
            ammunition = template.Parent;
        }
        return filters?.Any(f => f.Filter?.Overlaps(ancestors) == true) == true;
    }

    private static List<Item> Descendants(Item root, List<Item> inventory)
    {
        var result = new List<Item> { root };
        var ids = new HashSet<string> { root.Id.ToString() };
        for (int i = 0; i < result.Count; i++)
            foreach (var child in inventory.Where(v => v.ParentId == result[i].Id.ToString()))
                if (ids.Add(child.Id.ToString()))
                    result.Add(child);
        return result;
    }

    private static int Preference(string role, string belt) => role.ToLowerInvariant() switch
    {
        "isbscout" or "isbassassin" => belt is "6761b213607f9a6f79017af4" or "680e8d81e16a7caa3dd10730" or "68170b2cb595bf820b60571a" ? 0 : 1,
        "isbheavy" or "isbfollowerloghan" => belt is "6761b213607f9a6f79017afd" or "69364a368a4b47eeaf9ba4a3" ? 0 : 1,
        "isbmarksman" => belt is "6761b213607f9a6f79017aff" or "681bb00c7fecf6d490b2fb3c" ? 0 : 1,
        "isbteamleader" or "isbsecondleader" => belt is "68084891d93e159932d7e326" or "69352d5c409abe93d29321e4" ? 0 : 1,
        _ => belt is "6761b213607f9a6f79017af7" or "681baec29eb1732a417bd7dd" or "69352d5c409abe93d29321e4" ? 0 : 1
    };

    private static string[] Supplies(string role, Random random)
    {
        string[] medicine = ["544fb45d4bdc2dee738b4568", "590c678286f77426c9660122", "60098ad7c2240c0fe85c570a"];
        string utility = role.ToLowerInvariant() switch
        {
            "isbscout" or "isbassassin" => "61605ddea09d851a0a0c1bbc",
            "isbmarksman" => "544fb25a4bdc2dfb738b4567",
            "isbteamleader" or "isbsecondleader" or "isbbossfirefly" => "5c0e530286f7747fa1419862",
            _ => "5e8488fa988a8701445df1e4"
        };
        return [medicine[random.Next(medicine.Length)], utility];
    }
}
