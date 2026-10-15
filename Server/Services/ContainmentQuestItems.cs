using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForces.Services;
internal static class ContainmentQuestItems
{
    internal static void FilterRespirators(IDictionary<MongoId, Quest>? quests, Func<string, bool> containsItem)
    {
        if (quests == null || !quests.TryGetValue("1f6a892b5dda4241868ffd09", out var quest))
            return;
        foreach (var condition in quest.Conditions?.AvailableForFinish ?? [])
        {
            if (condition.ConditionType != "HandoverItem" || condition.Target?.List == null)
                continue;
            var available = condition.Target.List.Where(containsItem).ToList();
            if (available.Count == 0)
                throw new InvalidOperationException("ISB: no configured respirators are available for the ContainmentBreach quest.");
            condition.Target.List.Clear();
            condition.Target.List.AddRange(available);
        }
    }
}
