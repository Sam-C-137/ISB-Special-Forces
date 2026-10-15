using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBAishiLoyaltyConditionKillPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod(nameof(Player.OnBeenKilledByAggressor), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(Player __instance, IPlayer aggressor)
        {
            if (__instance == null)
                return;
            if (!ISBAishiLoyaltyConditionState.IsEligibleAggressor(aggressor))
                return;
            if (!ISBFikaAuthority.IsAiAuthority && !ISBAishiLoyaltyConditionState.IsEligiblePlayer(aggressor))
                return;
            if (!__instance.IsAI)
                return;
            var victimRole = __instance.Profile.Info.Settings.Role;
            if (!WildSpawnTypeExtensions.IsISB(victimRole))
                return;
            if (ISBAishiLoyaltyConditionState.IsCommander(victimRole))
                return;
            var localAggressor = ISBAishiLoyaltyConditionState.IsEligiblePlayer(aggressor);
            if (localAggressor)
                ISBReputationBridge.ApplyKillPenaltyOnce();
            ISBAishiLoyaltyConditionState.Break(victimRole, aggressor.ProfileId, registerRevenge: localAggressor);
            if (localAggressor)
                ISBRevengeBridge.RegisterKill(victimRole, __instance.ProfileId);
            else
                ISBAishiParticipantBridge.Report(aggressor.ProfileId, victimRole, __instance.ProfileId);
        }
    }
}
