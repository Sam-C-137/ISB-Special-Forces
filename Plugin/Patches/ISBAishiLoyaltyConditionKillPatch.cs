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

            if (!__instance.IsAI)
                return;

            var victimRole = __instance.Profile.Info.Settings.Role;

            if (!WildSpawnTypeExtensions.IsISB(victimRole))
                return;

            if (ISBAishiLoyaltyConditionState.IsCommander(victimRole))
                return;

            if (!ISBAishiLoyaltyConditionState.Holding)
                return;

            var localAggressor = ISBAishiLoyaltyConditionState.IsEligiblePlayer(aggressor);

            if (localAggressor)
                ISBReputationBridge.ApplyKillPenaltyOnce();

            Plugin.LogSource.LogInfo("[ISB AISHI LOYALTY] ISB operator killed. Aishi loyalty condition breaks instantly.");

            ISBAishiLoyaltyConditionState.Break(
                victimRole,
                aggressor.ProfileId,
                registerRevenge: localAggressor);
        }
    }
}
