using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EFT;
using EFT.Achievements;
using EFT.UI;
using HarmonyLib;
using UnityEngine.UI;

namespace ISBSSOFExtra
{
    internal static class ContainmentAchievementIcons
    {
        private sealed class Original
        {
            internal bool Preserve;
        }

        private static readonly ConditionalWeakTable<Image, Original> Originals = new ConditionalWeakTable<Image, Original>();
        private static readonly FieldInfo Icon = AccessTools.Field(typeof(AchievementsSystem.AchievementIconView), "_iconImage");
        private static Harmony _patch;
        private const string Victoria = "1d9f3fb317d15e2a77957327";
        private static AchievementsScreen _screen;
        private static AchievementsController _controller;
        private static Profile _profile;
        private static EAchievementsSortType _sort;
        private static bool _ascending;
        private static ManualLogSource _log;
        private static bool _reported;
        internal static void Enable(ManualLogSource log)
        {
            if (_patch != null)
                return;
            _log = log;
            try
            {
                _patch = new Harmony("com.samc137.isbsofextras.achievement-icons");
                _patch.Patch(AccessTools.Method(typeof(AchievementsSystem.AchievementIconView), "Show"), postfix: new HarmonyMethod(typeof(ContainmentAchievementIcons), nameof(Shown)));
                _patch.Patch(AccessTools.Method(typeof(AchievementsScreen), "Show"), postfix: new HarmonyMethod(typeof(ContainmentAchievementIcons), nameof(ScreenShown)));
                _patch.Patch(AccessTools.Method(typeof(AchievementsScreen), "ShowAchievements"), prefix: new HarmonyMethod(typeof(ContainmentAchievementIcons), nameof(Sorted)));
                _patch.Patch(AccessTools.Method(typeof(AchievementsScreen), "Close"), postfix: new HarmonyMethod(typeof(ContainmentAchievementIcons), nameof(ScreenClosed)));
            }
            catch (Exception ex)
            {
                Disable();
                log.LogError("ISB: achievement icon proportions could not be adjusted: " + ex.GetBaseException().Message);
            }
        }

        private static void ScreenShown(AchievementsScreen __instance, Profile __0, AchievementsController __1)
        {
            _screen = __instance;
            _profile = __0;
            _controller = __1;
            LabsVariantMenu.RequestStateRefresh();
        }

        private static void Sorted(EAchievementsSortType __0, bool __1)
        {
            _sort = __0;
            _ascending = __1;
        }

        private static void ScreenClosed(AchievementsScreen __instance)
        {
            if (_screen != __instance)
                return;
            _screen = null;
            _profile = null;
            _controller = null;
        }

        internal static void ApplyServerState(LabsMenuState state)
        {
            if (state == null || state.victoriaEarnedAt <= 0 || _profile == null || _controller == null
                || !string.Equals(_profile.Id, state.profile, StringComparison.Ordinal))
                return;
            try
            {
                var achievement = _controller.AchievementsBook.GetConditional(Victoria);
                if (achievement == null || achievement.IsDone())
                    return;
                _profile.AchievementsData[Victoria] = state.victoriaEarnedAt;
                if (!_controller.UnlockAchievementForced(Victoria))
                    return;
                achievement.StatusData.UnlockDate = state.victoriaEarnedAt;
                if (_screen != null && _screen.gameObject.activeInHierarchy)
                    _screen.ShowAchievements(_sort, _ascending);
                _reported = false;
            }
            catch (Exception ex)
            {
                if (_reported)
                    return;
                _reported = true;
                _log.LogWarning("ISB: Victoria achievement display could not be refreshed: " + ex.GetBaseException().Message);
            }
        }

        private static void Shown(AchievementsSystem.AchievementIconView __instance, object __1)
        {
            var image = Icon.GetValue(__instance) as Image;
            if (image == null || __1 == null)
                return;
            string id = AccessTools.Property(__1.GetType(), "Id")?.GetValue(__1, null) as string;
            if (id == "1d9f3fb317d15e2a77957327" || id == "9df3731674b1b81ebec38ce4")
            {
                Originals.GetValue(image, value => new Original { Preserve = value.preserveAspect });
                image.preserveAspect = true;
            }
            else if (Originals.TryGetValue(image, out var original))
            {
                image.preserveAspect = original.Preserve;
                Originals.Remove(image);
            }
        }

        internal static void Disable()
        {
            _screen = null;
            _profile = null;
            _controller = null;
            _patch?.UnpatchSelf();
            _patch = null;
        }
    }
}
