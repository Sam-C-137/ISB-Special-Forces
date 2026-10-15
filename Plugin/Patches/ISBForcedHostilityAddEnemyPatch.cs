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
            if (!ISBForcedHostilityState.Enabled)
                return true;
            if (person == null || !person.IsAI)
                return true;
            var initialBot = __instance.InitialBot;
            if (initialBot == null)
                return true;
            if (!WildSpawnTypeExtensions.IsISB(initialBot.Profile.Info.Settings.Role))
                return true;
            var role = person.Profile?.Info?.Settings?.Role;
            if (role == null)
                return true;
            if (!ISBForcedHostilityState.IsFriendlyToISB(role.Value))
                return true;
            __result = false;
            return false;
        }
    }
}
