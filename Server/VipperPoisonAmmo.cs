using HarmonyLib;
using ISB.Compatibility;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Servers;
using System.Text.Json.Nodes;
#if ISB_SPT41
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
#else
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Services;
#endif

namespace ISBSpecialForcesServer;
#if ISB_SPT41
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 99999)]
#else
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 9999)]
#endif
public sealed class VipperPoisonAmmo(
#if ISB_SPT41
    TemplateTable templates, LocaleTable locales, ItemConfig itemConfig,
#else
    DatabaseService database, ConfigServer configs,
#endif
    JsonUtil json, ItemBaseClassService classes) : IOnLoad
{
    private const string HarmonyId = "com.samc137.isb.vipper.poison";
    private static readonly Dictionary<MongoId, MongoId> Poisoned = new();
    private static readonly Dictionary<MongoId, MongoId> Original = new();
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
#if ISB_SPT41
        cancellationToken.ThrowIfCancellationRequested();
#else
        var templates = database.GetTemplates();
        var locales = database.GetTables().Locales;
        var itemConfig = configs.GetConfig<ItemConfig>();
#endif
        var ammunition = templates.Items.Values.Where(item => item.Type == "Item" && item.Properties != null && !VipperPoisonIdentity.IsPoisoned(item.Id.ToString()) && classes.ItemHasBaseClass(item.Id, "5485a8684bdc2da71d8b4567")).ToArray();
        foreach (var original in ammunition)
        {
            MongoId id = VipperPoisonIdentity.TemplateId(original.Id.ToString());
            if (templates.Items.ContainsKey(id))
                throw new InvalidOperationException("Vipper poison template ID collision: " + id);
            var node = JsonNode.Parse(json.Serialize(original, false))!;
            node["_id"] = id.ToString();
            node["_name"] = VipperPoisonIdentity.Marker + original.Id;
            node["_props"]!["BackgroundColor"] = "violet";
            node["_props"]!["CanSellOnRagfair"] = false;
            node["_props"]!["CanRequireOnRagfair"] = false;
            templates.Items[id] = json.Deserialize<TemplateItem>(node.ToJsonString())!;
            Poisoned[original.Id] = id;
            Original[id] = original.Id;
            if (templates.Prices.TryGetValue(original.Id, out var price)) templates.Prices[id] = price;
            var handbook = templates.Handbook.Items.FirstOrDefault(item => item.Id == original.Id);
            if (handbook != null) templates.Handbook.Items.Add(handbook with { Id = id });
            itemConfig.Blacklist.Add(id);
            itemConfig.LootableItemBlacklist.Add(id);
            itemConfig.RewardItemBlacklist.Add(id);
            classes.AddItemToCache(id);
        }
        foreach (var lazy in locales.Global.Values)
            lazy.AddTransformer(current =>
            {
                foreach (var pair in Poisoned)
                    foreach (var suffix in new[] { "Name", "ShortName", "Description" })
                        if (current.TryGetValue(pair.Key + " " + suffix, out var value))
                            current[pair.Value + " " + suffix] = value + (suffix == "Description" ? "" : " (Poisoned)");
                return current;
            });
        return Task.CompletedTask;
    }
    internal Task Complete()
    {
#if !ISB_SPT41
        var templates = database.GetTemplates();
        var locales = database.GetTables().Locales;
#endif
        foreach (var pair in Poisoned)
        {
            var node = JsonNode.Parse(json.Serialize(templates.Items[pair.Key], false))!;
            node["_id"] = pair.Value.ToString();
            node["_name"] = VipperPoisonIdentity.Marker + pair.Key;
            node["_props"]!["BackgroundColor"] = "violet";
            node["_props"]!["CanSellOnRagfair"] = false;
            node["_props"]!["CanRequireOnRagfair"] = false;
            templates.Items[pair.Value] = json.Deserialize<TemplateItem>(node.ToJsonString())!;
        }
        foreach (var template in templates.Items.Values)
        {
            foreach (var slot in (template.Properties?.Slots ?? []).Concat(template.Properties?.Chambers ?? []).Concat(template.Properties?.Cartridges ?? []))
                foreach (var filter in slot.Properties?.Filters ?? [])
                    if (filter.Filter != null)
                        foreach (var original in filter.Filter.Where(Poisoned.ContainsKey).ToArray()) filter.Filter.Add(Poisoned[original]);
            foreach (var stack in template.Properties?.StackSlots ?? [])
                foreach (var filter in stack.Properties?.Filters ?? [])
                    if (filter.Filter != null)
                        foreach (var original in filter.Filter.Where(Poisoned.ContainsKey).ToArray()) filter.Filter.Add(Poisoned[original]);
            foreach (var grid in template.Properties?.Grids ?? [])
                foreach (var filter in grid.Properties?.Filters ?? [])
                {
                    if (filter.Filter != null)
                        foreach (var original in filter.Filter.Where(Poisoned.ContainsKey).ToArray()) filter.Filter.Add(Poisoned[original]);
                    if (filter.ExcludedFilter != null)
                        foreach (var original in filter.ExcludedFilter.Where(Poisoned.ContainsKey).ToArray()) filter.ExcludedFilter.Add(Poisoned[original]);
                }
        }
        var harmony = new Harmony(HarmonyId);
        foreach (var name in new[] { "GenerateBot", "PrepareAndGenerateBot" })
        {
            var method = AccessTools.Method(typeof(BotGenerator), name) ?? throw new MissingMethodException(typeof(BotGenerator).FullName, name);
            var owners = Harmony.GetPatchInfo(method)?.Owners.Where(owner => owner != HarmonyId).ToArray() ?? [];
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(VipperPoisonAmmo), nameof(Apply)) { priority = Priority.Last, after = owners });
        }
        var batch = AccessTools.Method(typeof(BotController), "Generate") ?? throw new MissingMethodException("BotController.Generate");
        harmony.Patch(batch, postfix: new HarmonyMethod(typeof(VipperPoisonAmmo), nameof(ApplyBatch)) { priority = Priority.Last, after = Harmony.GetPatchInfo(batch)?.Owners.Where(owner => owner != HarmonyId).ToArray() ?? [] });
        return Task.CompletedTask;
    }
    private static void ApplyBatch(ref Task<IEnumerable<BotBase?>> __result) => __result = ApplyBatchAsync(__result);
    private static async Task<IEnumerable<BotBase?>> ApplyBatchAsync(Task<IEnumerable<BotBase?>> task)
    {
        var bots = (await task.ConfigureAwait(false)).ToList();
        foreach (var bot in bots) Apply(bot);
        return bots;
    }
    private static void Apply(BotBase? __result)
    {
        if (__result?.Inventory?.Items == null) return;
        var role = __result.Info?.Settings?.Role;
        var vipper = string.Equals(role, "ISBFollowerVipper", StringComparison.OrdinalIgnoreCase) || role == "13703";
        foreach (var item in __result.Inventory.Items)
            if (vipper && Poisoned.TryGetValue(item.Template, out var poisoned)) item.Template = poisoned;
            else if (!vipper && Original.TryGetValue(item.Template, out var original)) item.Template = original;
    }
}

[Injectable(TypePriority = int.MaxValue - 1)]
public sealed class VipperPoisonCompletion(VipperPoisonAmmo catalog) : IOnLoad
{
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return catalog.Complete();
    }
#else
    public Task OnLoad() => catalog.Complete();
#endif
}
