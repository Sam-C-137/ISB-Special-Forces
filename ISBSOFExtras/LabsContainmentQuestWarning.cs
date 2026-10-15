using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.UI;
using HarmonyLib;
using QuestClass = EFT.Quests.Quest;

namespace ISBSSOFExtra
{
    internal static class LabsContainmentQuestWarning
    {
        private const string FirstQuestId = "dd715e78ab984c14909da407";
        private static readonly HashSet<QuestView> Pending = new HashSet<QuestView>();
        private static Harmony _harmony;
        private static MethodInfo _startQuest;
        private static ManualLogSource _log;
        private static QuestView _approved;
        private static int _generation;
        internal static void Enable(ManualLogSource log)
        {
            _log = log;
            if (_harmony != null)
                return;
            try
            {
                _startQuest = AccessTools.Method(typeof(QuestView), "StartQuest", Type.EmptyTypes);
                if (_startQuest == null)
                    throw new MissingMethodException("QuestView.StartQuest");
                _harmony = new Harmony("com.samc137.isbsofextras.containment-quest-warning");
                _harmony.Patch(_startQuest, prefix: new HarmonyMethod(typeof(LabsContainmentQuestWarning), nameof(BeforeStart)));
                var acceptTask = AccessTools.Method(typeof(QuestView), "StartQuest", new[] { typeof(QuestClass) });
                if (acceptTask == null || acceptTask.ReturnType != typeof(Task))
                    throw new MissingMethodException("QuestView.StartQuest(QuestClass)");
                _harmony.Patch(acceptTask, postfix: new HarmonyMethod(typeof(LabsContainmentQuestWarning), nameof(AfterStart)));
            }
            catch (Exception ex)
            {
                Disable();
                _log?.LogError("ISB: first quest confirmation could not be installed: " + ex.GetBaseException().Message);
            }
        }

        private static void AfterStart(QuestClass __0, ref Task __result)
        {
            if (__0?.Id == FirstQuestId && __result != null)
                __result = RefreshAfterAcceptance(__result, __0, _generation);
        }

        private static async Task RefreshAfterAcceptance(Task acceptance, QuestClass quest, int generation)
        {
            await acceptance;
            if (_harmony != null && generation == _generation && (quest.QuestStatus == EFT.Quests.EQuestStatus.Started || quest.QuestStatus == EFT.Quests.EQuestStatus.AvailableForFinish))
                LabsVariantMenu.RequestStateRefresh();
        }

        private static bool BeforeStart(QuestView __instance)
        {
            if (__instance == null || __instance.QuestId != FirstQuestId || ReferenceEquals(_approved, __instance))
                return true;
            if (Pending.Add(__instance))
                ShowConfirmations(__instance, _generation);
            return false;
        }

        private static async void ShowConfirmations(QuestView view, int generation)
        {
            bool Valid() => _harmony != null && generation == _generation && view != null && view.isActiveAndEnabled && view.QuestId == FirstQuestId && ItemUiContext.Instance != null;
            async Task<bool> Ask(int step)
            {
                await Task.Yield();
                if (!Valid())
                    return false;
                string body = step == 0 ? Text("isb_cb_accept_warning", "DO YOU REALLY WANT TO ACCEPT THIS? THIS WILL CHANGE LABS.\n\nWhile any mission in this operation is active, the ContainmentBreach event is guaranteed in Labs. Continue?") : step == 2 ? Text("isb_cb_accept_event_guaranteed", "While a Containment Breach mission is active, the event is guaranteed in Labs. Do you wish to continue?") : Text("isb_cb_accept_confirm", "ARE YOU SURE?\n\nConfirm again to accept the mission and enable this change in Labs. Cancel to leave the mission unaccepted.");
                return await ItemUiContext.Instance.ShowMessageWindow(out _, body, Text("isb_cb_accept_title", "ContainmentBreach"), true);
            }

            try
            {
                if (!await QuestAcceptanceConfirmation.Confirm(Ask, Valid))
                    return;
                _approved = view;
                try
                {
                    _startQuest.Invoke(view, null);
                }
                finally
                {
                    _approved = null;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log?.LogError("ISB: first quest confirmation failed: " + ex.GetBaseException().Message);
            }
            finally
            {
                Pending.Remove(view);
            }
        }

        private static string Text(string key, string fallback) => ISB.Compatibility.ISBLocalizedText.Get(key, fallback);
        internal static void Disable()
        {
            _generation++;
            _harmony?.UnpatchSelf();
            _harmony = null;
            _approved = null;
            Pending.Clear();
        }
    }
}
