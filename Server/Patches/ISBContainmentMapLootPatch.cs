using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 11)]
public sealed class ISBContainmentMapLootPatch(ConfigController config, ISBSpawnController spawns, ItemHelper items, ILogger<ISBContainmentMapLootPatch> logger) : IOnLoad
{
    private static ISBContainmentMapLootPatch? _instance;
    private int _reported;
    public Task OnLoad()
    {
        _instance = this;
        var target = AccessTools.Method(typeof(LocationLifecycleService), "GenerateLocationAndLoot", [typeof(MongoId), typeof(string), typeof(bool)]) ?? throw new MissingMethodException("ISB: LocationLifecycleService.GenerateLocationAndLoot");
        new Harmony("com.samc137.isbspecialforces.containment-map-loot").Patch(target, postfix: new HarmonyMethod(typeof(ISBContainmentMapLootPatch), nameof(AfterGeneration)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static void AfterGeneration(MongoId __0, string __1, bool __2, LocationBase __result)
    {
        var instance = _instance;
        if (instance == null || __result == null || !__2)
            return;
        instance.Apply(__0, __1, __result);
    }

    private void Apply(MongoId session, string map, LocationBase location)
    {
        if (!string.Equals(map, "laboratory", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            var status = spawns.GetLabsPreBlackoutEventStatus(session.ToString());
            ContainmentMapLoot.Apply(location, map, status.active, true, config.ModConfig.labsContainmentBreachEvent?.mapLoot, tpl =>
            {
                var item = items.GetItem(tpl);
                return item.Key ? item.Value : null;
            }, items.GenerateUpdForItem, Random.Shared);
            ContainmentMapLoot.Apply(location, map, status.active, true, config.ModConfig.labsContainmentBreachEvent?.containmentCaseSpawns, tpl =>
            {
                var item = items.GetItem(tpl);
                return item.Key ? item.Value : null;
            }, items.GenerateUpdForItem, Random.Shared, "ISB_CB_CASE_");
            var office = status.officeAccess?.config;
            if (office != null)
            {
                ContainmentLootGroup Group(string id, string item, ISB.Compatibility.OfficePoint? point) => new()
                {
                    id = id,
                    item = item,
                    enabled = point?.IsValid() == true,
                    positions = point == null ? [] : [new()
                    {
                        x = point.x,
                        y = point.y,
                        z = point.z,
                        rotationY = point.rotationY
                    }

                    ]
                };
                var officeLoot = new ContainmentMapLootConfig
                {
                    groups = [Group("office_cassie", office.cassieFlashDriveItem, office.cassieFlashDrive), Group("office_pending_flash_drive", office.pendingFlashDriveItem, office.pendingFlashDrive)]
                };
                ContainmentMapLoot.Apply(location, map, status.active, true, officeLoot, tpl =>
                {
                    var item = items.GetItem(tpl);
                    return item.Key ? item.Value : null;
                }, items.GenerateUpdForItem, Random.Shared);
            }
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _reported, 1) == 0)
                logger.LogError(ex, "ISB: could not add ContainmentBreach map loot.");
        }
    }
}
