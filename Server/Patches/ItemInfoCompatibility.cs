using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
#if ISB_SPT41
using SPTarkov.Server.Core.Models.Spt.Tables;
#else
using SPTarkov.Server.Core.Services;
#endif

namespace ISBSpecialForcesServer.Patches;

#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1000)]
public sealed class ItemInfoCompatibility(TradersTable traders, ILogger<ItemInfoCompatibility> logger) : IOnLoad
#else
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 1)]
public sealed class ItemInfoCompatibility(DatabaseService database, ILogger<ItemInfoCompatibility> logger) : IOnLoad
#endif
{
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
        var type = AccessTools.TypeByName("ItemInfo.Utils");
        if (type == null)
            return Task.CompletedTask;
        var method = AccessTools.Method(type, "GetFleaPrice", [typeof(string)]);
        if (method == null || !method.IsStatic || method.ReturnType != typeof(double?))
        {
            logger.LogError("ISB: installed Item Info has no compatible flea-price query.");
            return Task.CompletedTask;
        }
        if (AccessTools.TypeByName("WTTContentBackport.WTTContentBackport") != null)
        {
#if !ISB_SPT41
            var traders = database.GetTraders();
#endif
            if (traders.TryGetValue("5ac3b934156ae10c4430e83c", out var ragman) &&
                traders.TryGetValue("5935c25fb3acc3127c3d8cd9", out var peacekeeper) &&
                ragman.Assort?.Items != null && ragman.Assort.BarterScheme != null && peacekeeper.Assort?.Items != null)
            {
                foreach (var offer in new[]
                {
                    (Id: "6a7cbaf499651f6c10bf7c8d", Template: "689b880fff8b4adc420f5b56"),
                    (Id: "6a7cbaf499651f6c10bf7c8f", Template: "689479a4a733b1602007e2eb")
                })
                {
                    var id = new MongoId(offer.Id);
                    if (ragman.Assort.Items.Any(item => item.Id == id) ||
                        !peacekeeper.Assort.Items.Any(item => item.Id == id && item.Template == offer.Template) ||
                        peacekeeper.Assort.BarterScheme?.ContainsKey(id) != true)
                        continue;
                    ragman.Assort.BarterScheme.Remove(id);
                    ragman.Assort.LoyalLevelItems?.Remove(id);
                }
            }
        }
        new Harmony("com.samc137.isbspecialforces.iteminfo").Patch(method,
            prefix: new HarmonyMethod(typeof(ItemInfoCompatibility), nameof(BeforeFleaPrice)) { priority = Priority.First });
        return Task.CompletedTask;
    }

    private static bool BeforeFleaPrice(string? __0, ref double? __result)
    {
        if (__0 != null && MongoId.IsValidMongoId(__0))
            return true;
        __result = 0;
        return false;
    }
}
