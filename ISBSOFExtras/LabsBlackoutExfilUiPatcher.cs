using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using BepInEx.Logging;
using EFT.Interactive;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal static class LabsBlackoutExfilUiPatcher
    {
        private static Harmony _harmony;
        private static readonly FieldInfo Point = AccessTools.Field(typeof(ExitTimerPanel), "_point");
        private static readonly Dictionary<UIElement, bool> Rows = new Dictionary<UIElement, bool>();
        private static readonly List<UIElement> Dead = new List<UIElement>();
        internal static void Enable(GameObject host, ManualLogSource log)
        {
            if (_harmony != null || host == null)
                return;
            try
            {
                if (Point == null)
                    throw new MissingFieldException(typeof(ExitTimerPanel).FullName, "_point");
                _harmony = new Harmony("com.samc137.isbsofextras.exfil-hud");
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var show in typeof(UIElement).GetMethods(flags).Where(method => method.Name == nameof(UIElement.ShowGameObject)))
                    _harmony.Patch(show, prefix: new HarmonyMethod(typeof(LabsBlackoutExfilUiPatcher), nameof(BeforeShow)));
                foreach (var show in typeof(ExitTimerPanel).GetMethods(flags).Where(method => method.Name == nameof(ExitTimerPanel.Show)))
                    _harmony.Patch(show, postfix: new HarmonyMethod(typeof(LabsBlackoutExfilUiPatcher), nameof(AfterExitShow)));
                _harmony.Patch(AccessTools.PropertySetter(typeof(ExfiltrationPoint), nameof(ExfiltrationPoint.Status)), postfix: new HarmonyMethod(typeof(LabsBlackoutExfilUiPatcher), nameof(Refresh)));
                _harmony.Patch(AccessTools.PropertySetter(typeof(TransitPoint), nameof(TransitPoint.Enabled)), postfix: new HarmonyMethod(typeof(LabsBlackoutExfilUiPatcher), nameof(Refresh)));
                foreach (var row in UnityEngine.Object.FindObjectsOfType<ExitTimerPanel>(true))
                    Track(row);
                foreach (var row in UnityEngine.Object.FindObjectsOfType<TransitTimerPanel>(true))
                    Track(row);
                Refresh();
            }
            catch (Exception ex)
            {
                Disable();
                log?.LogError("ISB: extraction HUD integration failed: " + ex.GetBaseException().Message);
            }
        }

        private static bool ShouldHide(UIElement row)
        {
            if (!BlackoutBridge.HasHiddenHudEntries)
                return false;
            if (row is TransitTimerPanel)
                return BlackoutBridge.HideDisabledTransitHudEntries;
            var point = row is ExitTimerPanel ? Point.GetValue(row) as ExfiltrationPoint : null;
            return point != null && !LabsFinalLockdown.PreservesVent(point.Settings?.Name) && (BlackoutBridge.IsExfilHidden(point.Settings?.Name) || point.Status == EExfiltrationStatus.NotPresent);
        }

        private static void Track(UIElement row)
        {
            if (row != null && !Rows.ContainsKey(row))
                Rows.Add(row, false);
        }

        private static bool BeforeShow(UIElement __instance)
        {
            if (!(__instance is ExitTimerPanel) && !(__instance is TransitTimerPanel))
                return true;
            Track(__instance);
            if (!ShouldHide(__instance))
            {
                Rows[__instance] = false;
                return true;
            }

            Rows[__instance] = true;
            if (__instance.gameObject.activeSelf)
                __instance.gameObject.SetActive(false);
            return false;
        }

        private static void AfterExitShow(ExitTimerPanel __instance)
        {
            Track(__instance);
            UpdateRow(__instance);
        }

        private static void UpdateRow(UIElement row)
        {
            bool hidden = ShouldHide(row);
            if (hidden && row.gameObject.activeSelf)
            {
                Rows[row] = true;
                row.gameObject.SetActive(false);
            }
            else if (!hidden && Rows[row])
            {
                var point = row is ExitTimerPanel ? Point.GetValue(row) as ExfiltrationPoint : null;
                if (row is ExitTimerPanel && (point == null || point.Status == EExfiltrationStatus.NotPresent))
                    return;
                Rows[row] = false;
                row.gameObject.SetActive(true);
            }
        }

        private static void Refresh()
        {
            Dead.Clear();
            foreach (var row in new List<UIElement>(Rows.Keys))
            {
                if (row == null)
                    Dead.Add(row);
                else
                    UpdateRow(row);
            }

            foreach (var row in Dead)
                Rows.Remove(row);
            Dead.Clear();
        }

        internal static void HideVisibleEntries(ManualLogSource log)
        {
            if (_harmony != null)
                Refresh();
        }

        internal static void RestoreVisibleEntries(ManualLogSource log)
        {
            if (_harmony != null)
                Refresh();
        }

        internal static void Disable()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            foreach (var pair in Rows)
                if (pair.Key != null && pair.Value)
                    pair.Key.gameObject.SetActive(true);
            Rows.Clear();
            Dead.Clear();
        }
    }
}
