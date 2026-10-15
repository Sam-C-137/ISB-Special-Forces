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
            (WildSpawnType)13702,
            (WildSpawnType)13703,
            (WildSpawnType)13704,
            (WildSpawnType)13705,
            (WildSpawnType)13706,
            (WildSpawnType)13707,
            (WildSpawnType)13708,
            (WildSpawnType)13709,
            (WildSpawnType)13710,
            (WildSpawnType)13711,
            (WildSpawnType)13712,
            (WildSpawnType)13713,
            (WildSpawnType)13714,
            (WildSpawnType)13715,
            (WildSpawnType)13716,
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
            if (ISBSquadRoles.Contains(__instance.Profile.Info.Settings.Role))
                ISBSecretTeleport.TryQueue(__instance);
        }
    }
}
