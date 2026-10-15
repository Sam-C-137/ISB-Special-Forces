using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Services;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = int.MaxValue - 1)]
public sealed class ISBItemFilterWarmup(ItemFilterService filters) : IOnLoad
{
    public Task OnLoad()
    {
        var item = new MongoId("5449016a4bdc2d6f028b456f");
        filters.IsLootableItemBlacklisted(item);
        filters.IsItemBlacklisted(item);
        return Task.CompletedTask;
    }
}
