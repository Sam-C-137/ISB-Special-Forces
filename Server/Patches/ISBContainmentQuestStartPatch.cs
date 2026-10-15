using HarmonyLib;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Models.Enums;
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
public sealed class ISBContainmentQuestStartPatch(ILogger<ISBContainmentQuestStartPatch> logger, EventOutputHolder eventOutput) : IOnLoad
{
    private static ILogger<ISBContainmentQuestStartPatch>? _logger;
    private static EventOutputHolder _output = null!;
    public Task OnLoad()
    {
        _logger = logger;
        _output = eventOutput;
        var target = AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest), [typeof(PmcData), typeof(AcceptQuestRequestData), typeof(MongoId)]);
        if (target == null)
            throw new MissingMethodException("ISB: QuestController.AcceptQuest was not found.");
        new Harmony("com.samc137.isbspecialforces.containment-quest-start").Patch(target, prefix: new HarmonyMethod(typeof(ISBContainmentQuestStartPatch), nameof(BeforeAccept)), postfix: new HarmonyMethod(typeof(ISBContainmentQuestStartPatch), nameof(AfterAccept)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static bool BeforeAccept(PmcData __0, AcceptQuestRequestData __1, MongoId __2, ref ItemEventRouterResponse __result)
    {
        if (__1.QuestId.ToString()is not ("f1a90496778649e8b955e60d" or "637a389d84da48a4a34dfd17" or "b8432fd547b146bfa8b77dd6" or "52588eccea984354a13b53f4" or "1184c00a9ec847be987be767" or "cd993bfe6ed447dcaab85d04"))
            return true;
        var existing = __0.Quests?.FirstOrDefault(q => q.QId == __1.QuestId);
        if (existing == null || existing.Status is QuestStatusEnum.Locked or QuestStatusEnum.AvailableForStart)
            return true;
        __result = _output.GetOutput(__2);
        var changes = __result.ProfileChanges[__2];
        changes.QuestsStatus ??= [];
        changes.QuestsStatus.RemoveAll(q => q.QId == __1.QuestId);
        changes.QuestsStatus.Add(existing);
        return false;
    }

    private static void AfterAccept(QuestController __instance, PmcData __0, AcceptQuestRequestData __1, MongoId __2, ItemEventRouterResponse __result)
    {
        try
        {
            ContainmentQuestProgression.StartGateQuest(__0, __1.QuestId, __2, __result, id => __instance.AcceptQuest(__0, new AcceptQuestRequestData { QuestId = id, Type = __1.Type }, __2));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ISB: linked Containment quest could not be started after accepting {QuestId}.", __1.QuestId);
        }
    }
}
