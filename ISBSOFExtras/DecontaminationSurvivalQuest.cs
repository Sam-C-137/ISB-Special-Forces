using System;
using EFT;
using HarmonyLib;
using BepInEx.Logging;

namespace ISBSSOFExtra
{
    internal static class DecontaminationSurvivalQuest
    {
        private const string HarmonyId = "com.samc137.isbspecialforces.decontamination-survival";
        private static Func<Player, ExitStatus, string, string, bool> _eligible;
        private static ManualLogSource _log;
        internal static Func<Player, ExitStatus, string, bool> TenMinuteEligibility;
        internal static Func<Player, ExitStatus, string, bool> FiveMinuteEligibility;
        internal static Action<Player, ExitStatus> OfficeExtraction;
        internal static void Enable(Func<Player, ExitStatus, string, string, bool> eligible, ManualLogSource log)
        {
            _eligible = eligible;
            _log = log;
            new Harmony(HarmonyId).Patch(AccessTools.Method(typeof(Player), nameof(Player.OnGameSessionEnd), new[] { typeof(ExitStatus), typeof(float), typeof(string), typeof(string) }), prefix: new HarmonyMethod(typeof(DecontaminationSurvivalQuest), nameof(BeforeRaidEnd)) { priority = Priority.First });
        }

        private static void BeforeRaidEnd(Player __instance, ExitStatus exitStatus, string locationId, string exitName)
        {
            try
            {
                if (FiveMinuteEligibility?.Invoke(__instance, exitStatus, locationId) == true)
                    __instance.SpecialPlaceVisited("isb_cb_survived_decontamination_5min", 0);
                if (TenMinuteEligibility?.Invoke(__instance, exitStatus, locationId) == true)
                    __instance.SpecialPlaceVisited("isb_cb_survived_decontamination_10min", 0);
                OfficeExtraction?.Invoke(__instance, exitStatus);
                if (_eligible?.Invoke(__instance, exitStatus, locationId, exitName) == true)
                    __instance.SpecialPlaceVisited("isb_cb_survived_decontamination", 0);
            }
            catch (Exception ex)
            {
                _log?.LogError("ISB: could not record decontamination survival: " + ex.Message);
            }
        }

        internal static void Disable()
        {
            new Harmony(HarmonyId).UnpatchSelf();
            _eligible = null;
            OfficeExtraction = null;
            TenMinuteEligibility = null;
            FiveMinuteEligibility = null;
        }

        internal static bool QualifiesTenMinutes(bool localPlayer, double seconds, string status, string location) => QualifiesTimedSurvival(localPlayer, seconds, status, location, 600d);
        internal static bool QualifiesFiveMinutes(bool localPlayer, double seconds, string status, string location) => QualifiesTimedSurvival(localPlayer, seconds, status, location, 300d);
        private static bool QualifiesTimedSurvival(bool localPlayer, double seconds, string status, string location, double requiredSeconds) => localPlayer && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= requiredSeconds && (status == "Survived" || status == "Runner") && (string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) || location == "5b0fc42d86f7744a585f9105");
        internal static bool Qualifies(bool localPlayer, bool gasStarted, string status, string location, string exit)
        {
            return localPlayer && gasStarted && (status == "Survived" || status == "Runner") && (string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) || location == "5b0fc42d86f7744a585f9105") && string.Equals(exit, "lab_Vent", StringComparison.OrdinalIgnoreCase);
        }
    }
}

namespace ISBSSOFExtra
{
    internal sealed class DecontaminationSurvivalTimer
    {
        internal double Seconds { get; private set; }

        private double _last = -1d;
        private bool _wasActive;
        internal void Reset()
        {
            Seconds = 0d;
            _last = -1d;
            _wasActive = false;
        }

        internal void Update(double now, bool active)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d)
            {
                _last = -1d;
                _wasActive = false;
                return;
            }

            if (active && _wasActive && _last >= 0d && now >= _last)
                Seconds += now - _last;
            _last = now;
            _wasActive = active;
        }
    }
}
