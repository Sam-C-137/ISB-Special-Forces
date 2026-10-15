using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;

namespace ISBSpecialForces.Services;
internal static class ContainmentQuestProgression
{
    internal const string EntryQuestId = "dd715e78ab984c14909da407";
    internal const string GateQuestId = "f1a90496778649e8b955e60d";
    internal static bool StartGateQuest(PmcData profile, MongoId acceptedQuestId, MongoId sessionId, ItemEventRouterResponse response, Action<MongoId> acceptQuest)
    {
        string[] companionIds = acceptedQuestId.ToString() switch
        {
            EntryQuestId => [GateQuestId],
            "10413f0d043844c6a69a88db" => ["637a389d84da48a4a34dfd17", "b8432fd547b146bfa8b77dd6"],
            "85baf5fc8723406a88b049a5" => ["52588eccea984354a13b53f4"],
            "8e039d03e80f41788f9b9819" => ["1184c00a9ec847be987be767"],
            "a5933b154fe34593947956c8" => ["cd993bfe6ed447dcaab85d04"],
            _ => []
        };
        if (profile.Quests == null || !profile.Quests.Any(q => q.QId == acceptedQuestId && q.Status == QuestStatusEnum.Started))
            return false;
        bool started = false;
        foreach (string id in companionIds)
            started |= StartCompanion(profile, id, sessionId, response, acceptQuest);
        return started;
    }

    private static bool StartCompanion(PmcData profile, MongoId companionId, MongoId sessionId, ItemEventRouterResponse response, Action<MongoId> acceptQuest)
    {
        if (profile.Quests == null)
            return false;
        var existing = profile.Quests.FirstOrDefault(q => q.QId == companionId);
        if (existing != null && existing.Status is not QuestStatusEnum.Locked and not QuestStatusEnum.AvailableForStart)
            return false;
        if (response.ProfileChanges == null || !response.ProfileChanges.TryGetValue(sessionId, out var changes) || changes.Quests == null)
            return false;
        int index = changes.Quests.FindIndex(q => q.Id == companionId);
        if (index < 0 && existing?.Status != QuestStatusEnum.AvailableForStart)
            return false;
        var definition = index >= 0 ? changes.Quests[index] : null;
        acceptQuest(companionId);
        var started = profile.Quests.FirstOrDefault(q => q.QId == companionId && q.Status == QuestStatusEnum.Started);
        if (started == null)
            throw new InvalidOperationException("ISB: native acceptance did not start the linked quest.");
        if (definition != null)
        {
            index = changes.Quests.FindIndex(q => q.Id == companionId);
            var updated = definition with
            {
                Status = (int)QuestStatusEnum.Started,
                SptStatus = QuestStatusEnum.Started
            };
            if (index >= 0)
                changes.Quests[index] = updated;
            else
                changes.Quests.Add(updated);
        }

        changes.QuestsStatus ??= [];
        changes.QuestsStatus.RemoveAll(q => q.QId == companionId);
        changes.QuestsStatus.Add(started);
        return true;
    }
}
