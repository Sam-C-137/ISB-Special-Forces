using EFT;
using HarmonyLib;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBAishiLoyaltyConditionPlayerEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotsGroup).GetMethod(nameof(BotsGroup.IsPlayerEnemy), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        protected static void PatchPostfix(BotsGroup __instance, IPlayer player, ref bool __result)
        {
            if (ISBEventHostility.TryGetBotRelation(__instance, player, out var enemy))
            {
                __result = enemy;
                return;
            }
            if (ISBEventHostility.IsBlackDivisionGroup(__instance))
            {
                __result = ISBEventHostility.IsBlackDivisionEnemy(__instance, player);
                return;
            }
            if (ISBAishiLoyaltyConditionState.ShouldForceHostility(__instance, player))
            {
                __result = true;
                return;
            }

            if (!__result)
                return;
            var protectedTarget = ISBAishiLoyaltyConditionState.ProtectsTarget(player);
            if (!protectedTarget)
                return;
            var initialBot = __instance._initialBot;
            if (initialBot == null)
                return;
            var role = initialBot.Profile.Info.Settings.Role;
            if (!ISBAishiLoyaltyConditionState.IsProtectedFrom(role))
                return;
            if (ISBRevengeBridge.HasPendingRevenge(__instance, player))
                return;
            __result = false;
        }
    }
}
