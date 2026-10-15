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
        protected static void PatchPrefix(Player __instance, EFT.Ballistics.DamageInfo damageInfo)
        {
            if (!ISBFikaAuthority.IsAiAuthority && !ISBAishiLoyaltyConditionState.IsEligiblePlayer(damageInfo.Player?.iPlayer))
                return;
            if (!ISBAishiLoyaltyConditionState.BreakOnHit)
                return;
            var aggressor = damageInfo.Player;
            if (aggressor == null)
                return;
            var aggressorPlayer = aggressor.iPlayer;
            if (!ISBAishiLoyaltyConditionState.ProtectsHuman(aggressorPlayer))
                return;
            if (__instance == null || !__instance.IsAI)
                return;
            var victimRole = __instance.Profile.Info.Settings.Role;
            if (!ISBAishiLoyaltyConditionState.IsProtectedFrom(victimRole))
                return;
            var damage = damageInfo.Damage;
            if (damage <= 0f)
                return;
            var botTotal = ISBAishiLoyaltyConditionDamageTracker.Accumulate(aggressorPlayer.ProfileId, __instance.ProfileId, damage);
            var factionTotal = ISBAishiLoyaltyConditionDamageTracker.GetFactionTotal(aggressorPlayer.ProfileId);
            var threshold = ISBAishiLoyaltyConditionState.DamageThreshold;
            var reached = ISBAishiLoyaltyConditionState.ThresholdPerFaction ? factionTotal >= threshold : botTotal >= threshold;
            ISBAishiLoyaltyConditionState.LogDamage(damage, botTotal, factionTotal, threshold, reached);
            if (!reached)
                return;
            ISBAishiLoyaltyConditionState.Break(victimRole, aggressorPlayer.ProfileId, registerRevenge: ISBAishiLoyaltyConditionState.IsEligiblePlayer(aggressorPlayer));
        }
    }
}
