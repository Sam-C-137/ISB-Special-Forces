using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
#if ISB_SPT41
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Profile;
#else
using SPTarkov.Server.Core.Generators;
#endif
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using ISB.Compatibility;

namespace ISBSpecialForcesServer;
[Injectable]
public sealed class GhostBotController(BotController bots, BotGenerator generator, ProfileHelper profiles, ICloner cloner)
{
    private static readonly System.Reflection.MethodInfo Details = AccessTools.Method(typeof(BotController), "GetBotGenerationDetailsForWave") ?? throw new MissingMethodException("ISB GhostBuster native generation details unavailable.");
    private static readonly System.Reflection.MethodInfo Raid = AccessTools.Method(typeof(BotController), "GetMostRecentRaidSettings") ?? throw new MissingMethodException("ISB GhostBuster native raid settings unavailable.");
    public List<SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBase> Generate(MongoId session, GenerateBotsRequestData request)
    {
        var conditions = request?.Conditions;
        if (conditions == null || conditions.Count == 0 || conditions.Sum(condition => condition.Limit)is < 1 or > 32 || conditions.Any(condition => condition.Limit < 1 || !GhostBotVariant.Is(condition.Role) && GhostBotVariant.AliasForBase(condition.Role) == null))
            throw new InvalidOperationException("ISB GhostBuster received an invalid spawn request.");
        var result = new List<SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBase>();
        var pmc = profiles.GetPmcProfile(session);
        var raid = Raid.Invoke(bots, [session]);
        foreach (var condition in conditions)
        {
            var source = cloner.Clone(condition);
            source.Role = GhostBotVariant.CanonicalName(condition.Role) ?? GhostBotVariant.AliasForBase(condition.Role);
            source.Difficulty = "impossible";
            var details = (BotGenerationDetails)Details.Invoke(bots, [source, pmc, false, raid])!;
            details.BotCountToGenerate = condition.Limit;
            for (int i = 0; i < condition.Limit; i++)
                result.Add(generator.PrepareAndGenerateBot(session, cloner.Clone(details)));
        }

        return result;
    }
}

#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
#else
[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
#endif
public sealed class GhostBotRouter(JsonUtil json, HttpResponseUtil http, GhostBotController ghosts) : StaticRouter(json, [new RouteAction<GenerateBotsRequestData>("/ISB/ghostbots/generate", 
#if ISB_SPT41
        (url, info, session, output, cancellationToken) =>
#else
(url, info, session, output) => 
#endif
new ValueTask<string>(http.GetBody(ghosts.Generate(session, info))))]);
