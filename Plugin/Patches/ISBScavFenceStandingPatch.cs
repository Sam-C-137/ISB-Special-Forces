using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Counters;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal class ISBScavFenceStandingPatch : ModulePatch
    {
        private static readonly ConditionalWeakTable<PlayerLoyaltyData, HashSet<string>> Rewarded = new ConditionalWeakTable<PlayerLoyaltyData, HashSet<string>>();
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(PlayerLoyaltyData), "MyPlayerIsSavage", new[] { typeof(Player) });
        [PatchPrefix]
        private static bool PatchPrefix(PlayerLoyaltyData __instance, Player victim)
        {
            var killer = __instance._player;
            var settings = victim?.Profile?.Info?.Settings;
            if (killer == null || !killer.IsYourPlayer || killer.IsAI || killer.Side != EPlayerSide.Savage || victim == null || !victim.IsAI || settings == null || !WildSpawnTypeExtensions.IsISB(settings.Role))
                return true;
            if (__instance._loyaltySetting == null || string.IsNullOrEmpty(victim.ProfileId))
                return false;
            var rewarded = Rewarded.GetOrCreateValue(__instance);
            if (!rewarded.Add(victim.ProfileId))
                return false;
            double standing = RewardFor(settings.StandingForKill);
            __instance._loyaltySetting.AddStanding(standing, EFenceStandingSource.PmcBotKill);
            return false;
        }

        internal static double RewardFor(double configured) => double.IsNaN(configured) || double.IsInfinity(configured) || configured <= 0 ? 0.01 : configured;
    }
}
