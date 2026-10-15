using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using System.Linq;

namespace ISBSSOFExtra
{
    internal static class LabsFinalLockdown
    {
        internal const string Vent = "lab_Vent";
        private static Harmony _harmony;
        private static GameWorld _world;
        private static GameWorld _windowWorld;
        private static float _windowEnd;
        private static readonly Dictionary<ExfiltrationPoint, ExitState> WindowPoints = new Dictionary<ExfiltrationPoint, ExitState>();
        private sealed class ExitState
        {
            internal EExfiltrationType Type;
            internal float Time, Start;
            internal bool Reusable;
            internal EExfiltrationStatus Status;
        }

        internal static bool Active => _world != null && ReferenceEquals(_world, Singleton<GameWorld>.Instance);
        internal static bool WindowActive => _windowWorld != null && ReferenceEquals(_windowWorld, Singleton<GameWorld>.Instance);
        private static float RaidTime => Singleton<AbstractGame>.Instance.PastTime;
        internal static bool WindowFinished => WindowActive && RaidTime > _windowEnd && WindowPoints.Keys.All(point => point == null || point.Status != EExfiltrationStatus.Countdown);
        internal static bool WindowExpired => WindowActive && RaidTime > _windowEnd + 1f;
        internal static string WindowNames => string.Join(",", WindowPoints.Keys.Where(point => point != null).Select(point => point.Settings.Name));

        internal static bool AvailableForWindow(ExfiltrationPoint point) => point?.Settings != null && point.Settings.Name != Vent && !BlackoutBridge.IsExfilHidden(point.Settings.Name) && (point.Status == EExfiltrationStatus.RegularMode || point.Status == EExfiltrationStatus.Countdown);
        internal static void BeginWindow(float remaining, ManualLogSource log, string names = null)
        {
            if (Active || WindowActive || remaining <= 0f)
                return;
            Prepare();
            var points = ExfiltrationControllerClass.Instance?.ExfiltrationPoints;
            if (points == null || Singleton<AbstractGame>.Instance == null)
                throw new InvalidOperationException("Labs extraction timers are not ready.");
            var selected = names == null ? null : new HashSet<string>(names.Split(','), StringComparer.Ordinal);
            _windowWorld = Singleton<GameWorld>.Instance;
            _windowEnd = RaidTime + Math.Min(remaining, GasRules.ExtractionLockDelay);
            try
            {
                foreach (var point in points)
                {
                    if (point?.Settings == null || point.Settings.Name == Vent || (selected == null ? !AvailableForWindow(point) : !selected.Contains(point.Settings.Name)))
                        continue;
                    WindowPoints.Add(point, new ExitState { Type = point.Settings.ExfiltrationType, Time = point.Settings.ExfiltrationTime, Start = point.ExfiltrationStartTime, Reusable = point.Reusable, Status = point.Status });
                    foreach (var player in point.Entered.ToArray())
                        point.OnCancelExtraction?.Invoke(point, player);
                    point.Settings.ExfiltrationType = EExfiltrationType.SharedTimer;
                    point.Settings.ExfiltrationTime = GasRules.ExtractionLockDelay;
                    point.Reusable = false;
                    point.ExfiltrationStartTime = _windowEnd - GasRules.ExtractionLockDelay;
                    point.Status = EExfiltrationStatus.Countdown;
                    BlackoutBridge.ShowExfil(point.Settings.Name, log);
                }
            }
            catch
            {
                ResetWindow(true);
                throw;
            }
        }

        private static void ResetWindow(bool restoreStatus)
        {
            _windowWorld = null;
            foreach (var entry in WindowPoints)
            {
                var point = entry.Key;
                if (point == null || point.Settings == null)
                    continue;
                point.Settings.ExfiltrationType = entry.Value.Type;
                point.Settings.ExfiltrationTime = entry.Value.Time;
                point.Reusable = entry.Value.Reusable;
                if (restoreStatus && point.Status == EExfiltrationStatus.Countdown)
                {
                    point.Status = entry.Value.Status;
                    point.ExfiltrationStartTime = entry.Value.Start;
                }
            }

            WindowPoints.Clear();
        }

        internal static void Prepare()
        {
            var harmony = _harmony ?? new Harmony("com.samc137.isbsofextras.decontamination-exits");
            try
            {
                var patched = new HashSet<MethodBase>(harmony.GetPatchedMethods());
                var targets = new HashSet<MethodInfo>();
                targets.Add(AccessTools.PropertySetter(typeof(ExfiltrationPoint), nameof(ExfiltrationPoint.Status)));
                targets.Add(AccessTools.Method(typeof(ExfiltrationPoint), nameof(ExfiltrationPoint.Proceed)));
                var points = ExfiltrationControllerClass.Instance?.ExfiltrationPoints;
                if (points != null)
                    foreach (var point in points)
                    {
                        if (point == null)
                            continue;
                        targets.Add(AccessTools.PropertySetter(point.GetType(), nameof(ExfiltrationPoint.Status)));
                        targets.Add(AccessTools.Method(point.GetType(), nameof(ExfiltrationPoint.Proceed)));
                    }

                foreach (var target in targets)
                    if (!patched.Contains(target))
                        harmony.Patch(target, prefix: new HarmonyMethod(typeof(LabsFinalLockdown), target.Name == "set_Status" ? nameof(SetStatus) : nameof(CanProceed)));
                var enter = AccessTools.Method(typeof(ExfiltrationPoint), "OnTriggerEnter");
                if (!patched.Contains(enter))
                    harmony.Patch(enter, prefix: new HarmonyMethod(typeof(LabsFinalLockdown), nameof(CanEnter)));
                var transit = AccessTools.PropertySetter(typeof(TransitPoint), nameof(TransitPoint.Enabled));
                if (!patched.Contains(transit))
                    harmony.Patch(transit, prefix: new HarmonyMethod(typeof(LabsFinalLockdown), nameof(SetTransitEnabled)));
                _harmony = harmony;
            }
            catch
            {
                harmony.UnpatchSelf();
                _harmony = null;
                throw;
            }
        }

        internal static void Activate(ManualLogSource log)
        {
            Prepare();
            var points = ExfiltrationControllerClass.Instance?.ExfiltrationPoints;
            if (points == null)
                throw new InvalidOperationException("Labs extraction points are not ready.");
            bool ventFound = false;
            foreach (var point in points)
                if (point?.Settings?.Name == Vent)
                {
                    ventFound = true;
                    break;
                }

            if (!ventFound)
                throw new InvalidOperationException("Ventilation Shaft extraction is missing.");
            _world = Singleton<GameWorld>.Instance;
            foreach (var point in points)
            {
                if (point?.Settings == null)
                    continue;
                if (point.Settings.Name == Vent)
                {
                    if (point.Status == EExfiltrationStatus.NotPresent)
                        point.Status = EExfiltrationStatus.UncompleteRequirements;
                    BlackoutBridge.ShowExfil(Vent, log);
                }
                else
                    BlackoutBridge.DisableAndHideExfil(point.Settings.Name, log);
            }

            foreach (var transit in LocationScene.GetAllObjects<TransitPoint>(false))
                if (transit != null)
                    transit.Enabled = false;
            TransitControllerAbstractClass.DisableTransitPoints();
            BlackoutBridge.IndexDisabledExfilsAndTransits(log);
            ResetWindow(false);
        }

        private static bool _evacuationReleased;
        internal static void ReleaseEvacuationGates()
        {
            if (WindowActive)
                ResetWindow(true);
            _evacuationReleased = true;
        }

        private static bool EvacuationGate(string name) => _evacuationReleased && (name == "lab_Parking_Gate" || name == "lab_Hangar_Gate");
        internal static bool PreservesVent(string name) => Active && name == Vent;
        internal static bool Blocks(ExfiltrationPoint point) => Active && point?.Settings != null && point.Settings.Name != Vent && !EvacuationGate(point.Settings.Name);
        private static void SetStatus(ExfiltrationPoint __instance, ref EExfiltrationStatus __0)
        {
            if (Blocks(__instance))
                __0 = EExfiltrationStatus.NotPresent;
            else if (PreservesVent(__instance?.Settings?.Name) && __0 == EExfiltrationStatus.NotPresent)
                __0 = __instance.Status == EExfiltrationStatus.NotPresent ? EExfiltrationStatus.UncompleteRequirements : __instance.Status;
        }

        private static bool CanProceed(ExfiltrationPoint __instance) => !Blocks(__instance);
        private static bool CanEnter(ExfiltrationPoint __instance) => !Blocks(__instance) && !(WindowActive && WindowPoints.ContainsKey(__instance) && RaidTime >= _windowEnd);
        private static void SetTransitEnabled(ref bool __0)
        {
            if (Active)
                __0 = false;
        }

        internal static void Reset()
        {
            _world = null;
            _evacuationReleased = false;
            ResetWindow(true);
        }

        internal static void Disable()
        {
            Reset();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
