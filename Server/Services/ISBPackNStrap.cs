using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using HarmonyLib;
using ISB.Compatibility;
using ISBSpecialForcesServer.Controllers;
using ISBSpecialForcesServer.Patches;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ISBSpecialForcesServer.Services;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 20)]
public sealed class ISBPackNStrap(ConfigController config, TemplateTable templates, ItemHelper items,
    ICloner cloner, IReadOnlyList<SptMod> mods, ILogger<ISBPackNStrap> logger) : IOnLoad
{
    private static ISBPackNStrap _instance = null!;
    private static readonly ConditionalWeakTable<BotBase, object> Applied = new();
    private TemplateItem[] _belts = [];
    private TemplateItem[] _magazines = [];
    private readonly ConcurrentDictionary<MongoId, IReadOnlyList<TemplateItem>> _compatibleMagazines = new();
    private int _failureReported;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!mods.Any(m => string.Equals(m.ModMetadata.ModGuid, "com.wtt.packnstrap", StringComparison.OrdinalIgnoreCase)))
            return Task.CompletedTask;
        _belts = templates.Items.Values.Where(t => t.Parent.ToString() == PackNStrapBelts.ParentId && t.Properties?.Grids?.Any() == true).ToArray();
        if (_belts.Length == 0)
            return Task.CompletedTask;
        _magazines = templates.Items.Values.Where(t => t.Properties?.Cartridges?.FirstOrDefault()?.MaxCount > 0
            && t.Properties.ReloadMagType?.ToString()?.Contains("Internal", StringComparison.OrdinalIgnoreCase) != true
            && t.Properties.ReloadMagType?.ToString()?.Contains("OnlyBarrel", StringComparison.OrdinalIgnoreCase) != true).ToArray();
        _instance = this;
        var target = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot));
        if (target != null)
            new Harmony("com.samc137.isb.packnstrap").Patch(target, postfix: new HarmonyMethod(typeof(ISBPackNStrap), nameof(AfterGeneration))
            {
                priority = Priority.Last,
                after = [ISBBotPresetGenerationPatch.HarmonyId, ISBDogtagGenerationPatch.HarmonyId, ISBContainmentPocketLootPatch.HarmonyId],
                before = ["com.samc137.isb.vipper.poison"]
            });
        return Task.CompletedTask;
    }

    private static void AfterGeneration(BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        _instance.Apply(botGenerationDetails, __result);
    }

    private void Apply(BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        string? role = GhostBotGenerationPatch.RequestedRole(botGenerationDetails) ?? __result?.Info?.Settings?.Role;
        if (__result?.Inventory?.Items == null || !PackNStrapBelts.Eligible(role)
            || config.ModConfig?.IsCollabLoadoutEnabled(PackNStrapBelts.ConfigKey) != true)
            return;
        lock (__result)
        {
            if (Applied.TryGetValue(__result, out _))
                return;
            Applied.Add(__result, new object());
            try
            {
                PackNStrapBelts.Apply(__result, role!, _belts,
                    id => templates.Items.GetValueOrDefault(id),
                    item =>
                    {
                        var properties = templates.Items.GetValueOrDefault(item.Template)?.Properties;
                        return (properties?.Width ?? 0, properties?.Height ?? 0);
                    }, items.GenerateUpdForItem, cloner, Random.Shared, CompatibleMagazines);
            }
            catch (Exception exception)
            {
                if (Interlocked.Exchange(ref _failureReported, 1) == 0)
                    logger.LogWarning(exception, "[ISB] PackNStrap belt integration failed; original equipment was retained.");
            }
        }
    }

    private IReadOnlyList<TemplateItem> CompatibleMagazines(MongoId weapon) => _compatibleMagazines.GetOrAdd(weapon, id =>
    {
        var filters = templates.Items.GetValueOrDefault(id)?.Properties?.Slots?.FirstOrDefault(s => s.Name == "mod_magazine")?.Properties?.Filters;
        if (filters == null) return [];
        return _magazines.Where(m =>
        {
            var ancestors = new HashSet<MongoId>();
            var current = m.Id;
            for (int depth = 0; depth < 32 && ancestors.Add(current); depth++)
            {
                if (!templates.Items.TryGetValue(current, out var template)) break;
                current = template.Parent;
            }
            return filters.Any(f => f.Filter?.Overlaps(ancestors) == true);
        }).ToArray();
    });
}
