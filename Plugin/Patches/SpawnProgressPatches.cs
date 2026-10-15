using EFT;
using EFT.Quests;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBSpawnProgressStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
        [PatchPostfix]
        private static void Postfix(GameWorld __instance) => ISBSpawnProgressBridge.Init(__instance, true);
    }

    internal sealed class ISBAmnestyDescriptionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(ConditionHandoverItem).GetProperty(nameof(Condition.FormattedDescription)).GetGetMethod();
        [PatchPrefix]
        private static bool Prefix(ConditionHandoverItem __instance, ref string __result)
        {
            if (!__instance.id.ToString().StartsWith("495342", StringComparison.Ordinal))
                return true;
            __result = "Hand over dollars as compensation for your “betrayal”";
            return false;
        }
    }
}
