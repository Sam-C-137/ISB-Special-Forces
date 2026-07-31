using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class CommanderTeleportPatch : ModulePatch
    {
        private static readonly HashSet<WildSpawnType> ISBSquadRoles = new HashSet<WildSpawnType>
        {
            (WildSpawnType)13701,
            (WildSpawnType)13707,
            (WildSpawnType)13708,
            (WildSpawnType)13709,
            (WildSpawnType)13710,
        };

        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotOwner).GetMethod(nameof(BotOwner.method_10), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(BotOwner __instance)
        {
            if (__instance?.Profile?.Info?.Settings == null)
                return;

            var clashManager = MonoBehaviourSingleton<ISBBlackDivClashManager>.Instance;
            clashManager?.RegisterBot(__instance);

            if (ISBSquadRoles.Contains(__instance.Profile.Info.Settings.Role))
                ISBSecretTeleport.TryQueue(__instance);
        }
    }
}
