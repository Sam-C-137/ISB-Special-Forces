using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBAishiLoyaltyConditionDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod(nameof(Player.ApplyDamageInfo), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        protected static void PatchPrefix(Player __instance, DamageInfoStruct damageInfo)
        {
            if (!ISBAishiLoyaltyConditionState.Holding)
                return;

            if (!ISBAishiLoyaltyConditionState.BreakOnHit)
                return;

            var aggressor = damageInfo.Player;

            if (aggressor == null)
                return;

            var aggressorPlayer = aggressor.iPlayer;

            if (!ISBAishiLoyaltyConditionState.IsEligibleAggressor(aggressorPlayer))
                return;

            if (__instance == null || !__instance.IsAI)
                return;

            var victimRole = __instance.Profile.Info.Settings.Role;

            if (!ISBAishiLoyaltyConditionState.IsProtectedFrom(victimRole))
                return;

            var damage = damageInfo.Damage;

            if (damage <= 0f)
                return;

            var botTotal = ISBAishiLoyaltyConditionDamageTracker.Accumulate(__instance.ProfileId, damage);
            var factionTotal = ISBAishiLoyaltyConditionDamageTracker.FactionTotal;

            var threshold = ISBAishiLoyaltyConditionState.DamageThreshold;

            var reached = ISBAishiLoyaltyConditionState.ThresholdPerFaction
                ? factionTotal >= threshold
                : botTotal >= threshold;

            ISBAishiLoyaltyConditionState.LogDamage(damage, botTotal, factionTotal, threshold, reached);

            if (!reached)
                return;

            ISBAishiLoyaltyConditionState.Break(
                victimRole,
                aggressorPlayer.ProfileId,
                registerRevenge: ISBAishiLoyaltyConditionState.IsEligiblePlayer(aggressorPlayer));
        }
    }
}
