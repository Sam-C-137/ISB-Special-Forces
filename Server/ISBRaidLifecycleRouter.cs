using ISBSpecialForcesServer.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace ISBSpecialForces;
[Injectable(TypePriority = OnLoadOrder.GameCallbacks - 1)]
public sealed class ISBRaidLifecycleRouter(JsonUtil jsonUtil, ISBSpawnController spawnController, AishiLoyaltyConditionController loyaltyController, ISBRevengeController revengeController, ISBSpawnProgressController progress) : StaticRouter(jsonUtil, [new RouteAction<GetRaidConfigurationRequestData>("/client/raid/configuration", (url, info, sessionID, output) =>
{
    spawnController.SetRaidHourFromConfig(info);
    spawnController.AdjustISBSpawnsForRaid(info, sessionID);
    return new ValueTask<string>(output ?? string.Empty);
}), new RouteAction<GetRaidConfigurationRequestData>("/client/raid/configuration-by-profile", (url, info, sessionID, output) =>
{
    spawnController.SetRaidHourFromConfig(info);
    spawnController.AdjustISBSpawnsForRaid(info, sessionID);
    return new ValueTask<string>(output ?? string.Empty);
}), new RouteAction("/client/match/local/end", (url, info, sessionID, output) =>
{
    revengeController.EndRaid(sessionID);
    progress.Finish(sessionID);
    loyaltyController.EndRaid(sessionID);
    spawnController.EndRaid(sessionID);
    return new ValueTask<object>(output ?? string.Empty);
})]);
