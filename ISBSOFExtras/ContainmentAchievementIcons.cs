using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
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
        internal static void Enable(ManualLogSource log)
        {
            if (_patch != null)
                return;
            try
            {
                _patch = new Harmony("com.samc137.isbsofextras.achievement-icons");
                _patch.Patch(AccessTools.Method(typeof(AchievementsSystem.AchievementIconView), "Show"), postfix: new HarmonyMethod(typeof(ContainmentAchievementIcons), nameof(Shown)));
            }
            catch (Exception ex)
            {
                Disable();
                log.LogError("ISB: achievement icon proportions could not be adjusted: " + ex.GetBaseException().Message);
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
            _patch?.UnpatchSelf();
            _patch = null;
        }
    }
}
