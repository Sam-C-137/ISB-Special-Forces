using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBForcedHostilityAddEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotsGroup).GetMethod(nameof(BotsGroup.AddEnemy), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        protected static bool PatchPrefix(BotsGroup __instance, IPlayer person, ref bool __result)
        {
            if (ISBEventHostility.TryGetBotRelation(__instance, person, out var enemy))
            {
                if (enemy) return true;
                __result = false;
                return false;
            }
            if (!ISBEventHostility.IsBlackDivisionGroup(__instance) || ISBEventHostility.IsBlackDivisionEnemy(__instance, person))
                return true;
            __result = false;
            return false;
        }
    }
}
