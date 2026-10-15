using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBAishiLoyaltyConditionMarkOfUnknownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotsGroupMarkOfUnknown).GetMethod(nameof(BotsGroupMarkOfUnknown.HasMarkOfUnknown), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(BotsGroupMarkOfUnknown __instance, IPlayer player, ref bool __result)
        {
            if (!__result)
                return;
            if (!ISBAishiLoyaltyConditionState.ProtectsHuman(player))
                return;
            var initialBot = __instance.Groups?.InitialBot;
            if (initialBot?.Profile?.Info?.Settings == null || !ISBAishiLoyaltyConditionState.IsProtectedFrom(initialBot.Profile.Info.Settings.Role))
                return;
            __result = false;
        }
    }
}
