using SPTarkov.Server.Core.Models.Spt.Bots;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 12)]
public sealed class ISBContainmentHealthPatch(ConfigController config, ISBSpawnController spawns, ILogger<ISBContainmentHealthPatch> logger) : IOnLoad
{
    private static ISBContainmentHealthPatch? _instance;
    private int _reported;
    public Task OnLoad()
    {
        _instance = this;
        var target = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot)) ?? throw new MissingMethodException("ISB: BotGenerator.PrepareAndGenerateBot");
        new Harmony("com.samc137.isbspecialforces.containmenthealth").Patch(target, postfix: new HarmonyMethod(typeof(ISBContainmentHealthPatch), nameof(AfterGeneration)) { priority = Priority.Last, after = [ISBBotPresetGenerationPatch.HarmonyId] });
        return Task.CompletedTask;
    }

    private static void AfterGeneration(MongoId sessionId, BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        if (!GhostBotGenerationPatch.IsRequest(botGenerationDetails))
            _instance?.Apply(sessionId, __result);
    }

    private void Apply(MongoId sessionId, BotBase bot)
    {
        if (bot == null)
            return;
        try
        {
            ContainmentHealth.Apply(bot, spawns.GetLabsPreBlackoutEventStatus(sessionId.ToString()).active, config.ModConfig.labsContainmentBreachEvent?.isbHealthMultiplier ?? 0.6, config.ModConfig.labsContainmentBreachEvent?.blackDivisionHealthMultiplier ?? 1.25);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _reported, 1) == 0)
                logger.LogError(ex, "ISB: could not apply ContainmentBreach bot health.");
        }
    }
}
