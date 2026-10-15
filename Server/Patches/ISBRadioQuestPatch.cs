using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using ISBSpecialForces.Services;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader)]
public sealed class ISBRadioQuestPatch(ILogger<ISBRadioQuestPatch> logger) : IOnLoad
{
    private static ILogger<ISBRadioQuestPatch>? _logger;
    public Task OnLoad()
    {
        _logger = logger;
        var harmony = new Harmony("com.samc137.isbspecialforces.kw12-quests");
        harmony.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest), [typeof(PmcData), typeof(AcceptQuestRequestData), typeof(MongoId)]), prefix: new HarmonyMethod(typeof(ISBRadioQuestPatch), nameof(BeforeAccept)) { priority = Priority.First }, postfix: new HarmonyMethod(typeof(ISBRadioQuestPatch), nameof(AfterAccept)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.CompleteQuest), [typeof(PmcData), typeof(CompleteQuestRequestData), typeof(MongoId)]), postfix: new HarmonyMethod(typeof(ISBRadioQuestPatch), nameof(AfterComplete)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static void BeforeAccept(PmcData __0, AcceptQuestRequestData __1)
    {
        RadioQuestProgression.ResetFailedAttempt(__0, __1.QuestId);
    }

    private static void AfterAccept(QuestController __instance, PmcData __0, AcceptQuestRequestData __1, MongoId __2, ItemEventRouterResponse __result)
    {
        if (__1.QuestId == RadioQuestProgression.RewardQuestId)
            Advance(__instance, __0, __1.QuestId, __2, __result);
    }

    private static void AfterComplete(QuestController __instance, PmcData __0, CompleteQuestRequestData __1, MongoId __2, ItemEventRouterResponse __result)
    {
        if (__1.QuestId == RadioQuestProgression.TensionQuestId)
            Advance(__instance, __0, __1.QuestId, __2, __result);
    }

    private static void Advance(QuestController controller, PmcData profile, MongoId questId, MongoId sessionId, ItemEventRouterResponse response)
    {
        try
        {
            RadioQuestProgression.CompleteReward(profile, questId, sessionId, response, id => controller.AcceptQuest(profile, new AcceptQuestRequestData { QuestId = id, Type = "Quest" }, sessionId), id => controller.CompleteQuest(profile, new CompleteQuestRequestData { QuestId = id }, sessionId));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ISB: the KW12 reward quest could not be completed automatically.");
        }
    }
}
