using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AchievementsSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.UI;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBArenaAchievementIconPatch : ModulePatch
    {
        private static readonly HashSet<string> ArenaIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "1d9f3fb317d15e2a77957327",
            "9df3731674b1b81ebec38ce4",
            "c447fc786e04524e87d4ea8a"
        };
        private static readonly ConditionalWeakTable<AchievementIconView, Layout> Layouts = new ConditionalWeakTable<AchievementIconView, Layout>();
        private static readonly FieldInfo BorderField = AccessTools.Field(typeof(AchievementIconView), "_iconBorder");
        private static readonly FieldInfo BackgroundField = AccessTools.Field(typeof(AchievementIconView), "_iconBackground");
        private static readonly FieldInfo ImageField = AccessTools.Field(typeof(AchievementIconView), "_iconImage");
        protected override MethodBase GetTargetMethod()
        {
            return typeof(AchievementIconView).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(m => m.Name == "Show" && m.GetParameters().Length >= 3 && m.GetParameters()[0].ParameterType == typeof(bool) && m.GetParameters()[1].ParameterType.GetProperty("Id") != null);
        }

        [PatchPrefix]
        private static void BeforeShow(AchievementIconView __instance)
        {
            if (Layouts.TryGetValue(__instance, out var layout))
                layout.Restore();
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void AfterShow(AchievementIconView __instance, object __1)
        {
            var id = __1?.GetType().GetProperty("Id")?.GetValue(__1, null) as string;
            if (id == null || !ArenaIds.Contains(id))
                return;
            var image = ImageField.GetValue(__instance) as Image;
            var border = BorderField.GetValue(__instance) as Image;
            var background = BackgroundField.GetValue(__instance) as Image;
            if (image == null || border == null || background == null)
                return;
            var layout = Layouts.GetValue(__instance, _ => new Layout(image, border, background));
            layout.Apply();
        }

        private sealed class Layout
        {
            private readonly Image image, border, background;
            private readonly Vector2 sizeDelta;
            private readonly bool preserveAspect, borderEnabled, backgroundEnabled;
            public Layout(Image image, Image border, Image background)
            {
                this.image = image;
                this.border = border;
                this.background = background;
                sizeDelta = image.rectTransform.sizeDelta;
                preserveAspect = image.preserveAspect;
                borderEnabled = border.enabled;
                backgroundEnabled = background.enabled;
            }

            public void Apply()
            {
                border.enabled = false;
                background.enabled = false;
                image.preserveAspect = true;
                var bounds = border.rectTransform.rect.size;
                if (bounds.x > 0 && bounds.y > 0)
                {
                    var side = bounds.y * (112f / 86f) * 0.88f;
                    image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, side);
                    image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, side);
                }
            }

            public void Restore()
            {
                if (image == null || border == null || background == null)
                    return;
                image.rectTransform.sizeDelta = sizeDelta;
                image.preserveAspect = preserveAspect;
                border.enabled = borderEnabled;
                background.enabled = backgroundEnabled;
            }
        }
    }
}
