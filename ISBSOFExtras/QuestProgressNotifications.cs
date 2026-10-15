using System;
using System.Collections.Generic;
using EFT;
using EFT.Quests;
using EFT.Communications;
using EFT.UI;
using Comfort.Common;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private readonly Dictionary<Condition, bool> _questProgress = new Dictionary<Condition, bool>();
        private float _questProgressAt;
        private float _questCacheAt;
        private readonly List<Action> _questChecks = new List<Action>();
        private bool _questProgressFailed;
        private void TickQuestProgressNotifications()
        {
            if (!_raidisActive || !CanPresentLocally || !IsLaboratoryPresentationAllowed() || _questProgressFailed || UnityEngine.Time.realtimeSinceStartup < _questProgressAt)
                return;
            _questProgressAt = UnityEngine.Time.realtimeSinceStartup + .5f;
            var book = _activeRaidWorld?.MainPlayer?.QuestController?.Quests;
            if (book == null)
                return;
            try
            {
                if (UnityEngine.Time.realtimeSinceStartup >= _questCacheAt)
                {
                    _questCacheAt = UnityEngine.Time.realtimeSinceStartup + 5f;
                    _questChecks.Clear();
                    foreach (var quest in book)
                    {
                        if (quest.QuestStatus != EQuestStatus.Started || !quest.Template.CanShowNotificationsInGame)
                            continue;
                        var checks = new List<Func<bool>>();
                        foreach (var pair in quest.ProgressCheckers)
                        {
                            if (pair.Key.IsNecessary || !(pair.Key is ConditionCounterCreator counter) || !IsEventVisit(counter))
                                continue;
                            var condition = pair.Key;
                            var checker = pair.Value;
                            checks.Add(() =>
                            {
                                bool done = checker.Test();
                                bool notify = _questProgress.TryGetValue(condition, out bool previous) && !previous && done;
                                _questProgress[condition] = done;
                                return notify;
                            });
                        }

                        if (checks.Count == 0)
                            continue;
                        _questChecks.Add(() =>
                        {
                            if (quest.QuestStatus != EQuestStatus.Started)
                                return;
                            bool notify = false;
                            foreach (var check in checks)
                                notify |= check();
                            if (!notify)
                                return;
                            Singleton<GUISounds>.Instance?.PlayUISound(EUISoundType.QuestSubTrackComplete);
                            NotificationManager.DisplayNotification(new NotificationQuest { Text = "Subtask completed:".Localized() + " " + quest.Template.Name });
                        });
                    }
                }

                foreach (var check in _questChecks)
                    check();
            }
            catch (Exception ex)
            {
                _questProgressFailed = true;
                Log.LogError("ISB: quest progress notification failed: " + ex.GetBaseException().Message);
            }
        }

        private static bool IsEventVisit(ConditionCounterCreator condition)
        {
            if (condition.Conditions == null)
                return false;
            foreach (var part in condition.Conditions)
                if (part is ConditionVisitPlace visit && visit.target != null && visit.target.StartsWith("isb_cb_", StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
