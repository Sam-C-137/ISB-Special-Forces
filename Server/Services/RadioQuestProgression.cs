using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;

namespace ISBSpecialForces.Services;
internal static class RadioQuestProgression
{
    internal const string WarningQuestId = "f999ba37e8dda410954c0c1f";
    internal const string TensionQuestId = "4660ba9168903321ed40bfee";
    internal const string RewardQuestId = "ba02d1c72f3ca4c5d6625bf2";
    internal static bool ResetFailedAttempt(PmcData profile, MongoId acceptedQuestId)
    {
        if (acceptedQuestId != WarningQuestId && acceptedQuestId != TensionQuestId)
            return false;
        var quest = profile.Quests?.FirstOrDefault(q => q.QId == acceptedQuestId);
        if (quest == null || (int)quest.Status is not (5 or 6 or 7))
            return false;
        if (profile.TaskConditionCounters != null)
            foreach (var key in profile.TaskConditionCounters.Where(p => p.Value.SourceId == acceptedQuestId).Select(p => p.Key).ToArray())
                profile.TaskConditionCounters.Remove(key);
        quest.CompletedConditions = [];
        return true;
    }

    internal static bool CompleteReward(PmcData profile, MongoId changedQuestId, MongoId sessionId, ItemEventRouterResponse response, Action<MongoId> acceptQuest, Action<MongoId> completeQuest)
    {
        if (changedQuestId != TensionQuestId && changedQuestId != RewardQuestId)
            return false;
        if (profile.Quests == null || response.ProfileChanges == null || !response.ProfileChanges.TryGetValue(sessionId, out var changes))
            return false;
        lock (profile)
        {
            if (!profile.Quests.Any(q => q.QId == TensionQuestId && q.Status == QuestStatusEnum.Success))
                return false;
            var reward = profile.Quests.FirstOrDefault(q => q.QId == RewardQuestId);
            if (reward?.Status == QuestStatusEnum.Success)
                return false;
            if (reward == null || reward.Status is QuestStatusEnum.Locked or QuestStatusEnum.AvailableForStart)
            {
                if (changedQuestId != TensionQuestId || changes.Quests?.Any(q => q.Id == RewardQuestId) != true)
                    return false;
                acceptQuest(RewardQuestId);
                reward = profile.Quests.FirstOrDefault(q => q.QId == RewardQuestId);
            }

            if (reward?.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish)
                completeQuest(RewardQuestId);
            reward = profile.Quests.FirstOrDefault(q => q.QId == RewardQuestId);
            if (reward?.Status != QuestStatusEnum.Success)
                throw new InvalidOperationException("ISB: native quest completion did not finish the KW12 reward quest.");
            if (changes.Quests != null)
                for (int i = 0; i < changes.Quests.Count; i++)
                    if (changes.Quests[i].Id == RewardQuestId)
                        changes.Quests[i] = changes.Quests[i] with
                        {
                            Status = (int)QuestStatusEnum.Success,
                            SptStatus = QuestStatusEnum.Success
                        };
            changes.QuestsStatus ??= [];
            changes.QuestsStatus.RemoveAll(q => q.QId == RewardQuestId);
            changes.QuestsStatus.Add(reward);
            return true;
        }
    }
}
