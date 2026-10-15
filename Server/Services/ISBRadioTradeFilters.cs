using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;

namespace ISBSpecialForces.Services;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader)]
public sealed class ISBRadioTradeFilters(DatabaseService database) : IOnLoad
{
    public Task OnLoad()
    {
        var radioId = new MongoId("6aa5ff857a930fba7793141b");
        foreach (var trader in database.GetTraders().Values)
        {
            if (trader.Base == null)
                continue;
            trader.Base.ItemsBuyProhibited ??= new ItemBuyData
            {
                Category = [],
                IdList = []
            };
            trader.Base.ItemsBuyProhibited.IdList ??= [];
            trader.Base.ItemsBuyProhibited.IdList.Add(radioId);
        }

        return Task.CompletedTask;
    }
}
