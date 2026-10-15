using Comfort.Common;
using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBAishiLoyaltyConditionAddEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotsGroup).GetMethod(nameof(BotsGroup.AddEnemy), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        protected static bool PatchPrefix(BotsGroup __instance, IPlayer person, EBotEnemyCause cause, ref bool __result)
        {
            if (ISBAishiLoyaltyConditionState.ShouldForceHostility(__instance, person))
                return true;
            var initialBot = __instance.InitialBot;
            if (initialBot == null)
                return true;
            var groupRole = initialBot.Profile.Info.Settings.Role;
            var groupIsISB = WildSpawnTypeExtensions.IsISB(groupRole);
            if (groupIsISB)
            {
                if (ISBAishiLoyaltyConditionState.IsCommander(groupRole))
                {
                    ISBAishiLoyaltyConditionState.LogCause(cause, groupRole, "PASS (commander skip)");
                    return true;
                }

                var protectedTarget = ISBAishiLoyaltyConditionState.ProtectsTarget(person);
                if (!protectedTarget)
                    return true;
                if (ISBRevengeBridge.HasPendingRevenge(__instance, person))
                {
                    ISBAishiLoyaltyConditionState.LogCause(cause, groupRole, "PASS (pending revenge)");
                    return true;
                }

                ISBAishiLoyaltyConditionState.LogCause(cause, groupRole, "LOCKED");
                __result = false;
                return false;
            }

            if (!ISBAishiLoyaltyConditionState.ExtendToPlayerAllies)
                return true;
            if (!ISBAishiLoyaltyConditionState.IsProtectedISBBot(person))
                return true;
            if (!ISBAishiLoyaltyConditionState.IsProtectedAllyGroup(__instance))
                return true;
            __result = false;
            return false;
        }
    }
}
