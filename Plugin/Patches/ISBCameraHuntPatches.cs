using ISBSpecialForces.Components;
using MoreBotsAPI.Components;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBCameraHuntFirstTargetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(HuntManager).GetMethod(nameof(HuntManager.FindFirstHuntTarget));
        [PatchPrefix]
        private static bool Prefix(BotHuntManager hunter) => !ISBRevengeHuntController.TryRetarget(hunter) && !ISBCameraHuntController.TryRetarget(hunter) && !BlackDivisionEventHunt.TryRetarget(hunter);
    }

    internal sealed class ISBCameraHuntNewTargetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(HuntManager).GetMethod(nameof(HuntManager.FindNewHuntTarget));
        [PatchPrefix]
        private static bool Prefix(BotHuntManager hunter) => !ISBRevengeHuntController.TryRetarget(hunter) && !ISBCameraHuntController.TryRetarget(hunter) && !BlackDivisionEventHunt.TryRetarget(hunter);
    }

    internal sealed class ISBCameraHuntFollowersPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(BotHuntManager).GetMethod(nameof(BotHuntManager.GetFollowerManagers));
        [PatchPrefix]
        private static bool Prefix(BotHuntManager __instance, ref List<BotHuntManager> __result)
        {
            if (!ISBCameraHuntController.TryGetFollowers(__instance, out var followers))
                return true;
            __result = followers;
            return false;
        }
    }

    internal sealed class ISBCameraHuntRegroupPointPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(BotHuntManager).GetMethod(nameof(BotHuntManager.GetRegroupPoint));
        [PatchPrefix]
        private static bool Prefix(BotHuntManager __instance, ref Vector3 __result)
        {
            if (__instance == null || !ISBCameraHuntController.AppliesTo(__instance.botOwner))
                return true;
            ISBCameraHuntController.PrepareRegroup(__instance);
            if (!__instance.ignoreRegroup && __instance.botOwner.Boss?.IamBoss != true)
                return true;
            __result = __instance.botOwner.Position;
            return false;
        }
    }
}
