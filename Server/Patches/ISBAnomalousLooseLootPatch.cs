using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils.Cloners;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 12)]
public sealed class ISBAnomalousLooseLootPatch(ConfigController config, ISBSpawnController spawns, ItemHelper items, ICloner cloner, ILogger<ISBAnomalousLooseLootPatch> logger) : IOnLoad
{
    private readonly ConfigController _config = config;
    private readonly ISBSpawnController _spawns = spawns;
    private readonly ItemHelper _items = items;
    private readonly ICloner _cloner = cloner;
    private static ISBAnomalousLooseLootPatch? _instance;
    private static readonly AsyncLocal<AnomalousItemSpawnsConfig?> Current = new();
    private int _reported;
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _instance = this;
        var harmony = new Harmony("com.samc137.isbspecialforces.anomalous-loose-loot");
        var lifecycle = AccessTools.Method(typeof(LocationLifecycleService), "GenerateLocationAndLoot", [typeof(MongoId), typeof(string), typeof(bool)]) ?? throw new MissingMethodException("ISB: GenerateLocationAndLoot");
        var dynamicLoot = AccessTools.Method(typeof(LocationLootGenerator), "GenerateDynamicLoot") ?? throw new MissingMethodException("ISB: GenerateDynamicLoot");
        harmony.Patch(lifecycle, prefix: new HarmonyMethod(typeof(ISBAnomalousLooseLootPatch), nameof(Begin)), finalizer: new HarmonyMethod(typeof(ISBAnomalousLooseLootPatch), nameof(End)));
        harmony.Patch(dynamicLoot, prefix: new HarmonyMethod(typeof(ISBAnomalousLooseLootPatch), nameof(Prepare)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static void Begin(MongoId __0, string __1, bool __2, out AnomalousItemSpawnsConfig? __state)
    {
        __state = Current.Value;
        Current.Value = null;
        var instance = _instance;
        if (instance == null || !__2 || !string.Equals(__1, "laboratory", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            var settings = instance._config.AnomalousSpawns;
            if (AnomalousItemSpawns.EnabledForMap(settings) && instance._spawns.GetLabsPreBlackoutEventStatus(__0.ToString()).active)
                Current.Value = settings;
        }
        catch (Exception ex)
        {
            instance.Report(ex);
        }
    }

    private static void End(AnomalousItemSpawnsConfig? __state) => Current.Value = __state;
    private static void Prepare(ref LooseLoot __0, string __2)
    {
        var instance = _instance;
        var settings = Current.Value;
        if (instance == null || !string.Equals(__2, "laboratory", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            __0 = AnomalousItemSpawns.PrepareLooseLoot(__0, settings, value => instance._cloner.Clone(value), tpl =>
            {
                var item = instance._items.GetItem(tpl);
                return item.Key ? item.Value : null;
            });
        }
        catch (Exception ex)
        {
            instance.Report(ex);
        }
    }

    private void Report(Exception ex)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 0)
            logger.LogError(ex, "[ISB] Could not prepare event anomalous loose loot; keeping native loot.");
    }
}
