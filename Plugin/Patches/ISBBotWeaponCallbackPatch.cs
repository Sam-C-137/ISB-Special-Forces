using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBBotWeaponCallbackPatch : ModulePatch
    {
        private static readonly AccessTools.FieldRef<BotWeaponSelector, BotOwner> Owner = AccessTools.FieldRefAccess<BotWeaponSelector, BotOwner>("BotOwner_0");
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotWeaponSelector), nameof(BotWeaponSelector.OnWeaponTaken));
        [PatchPrefix]
        private static bool Prefix(BotWeaponSelector __instance)
        {
            BotOwner owner = Owner(__instance);
            return owner != null && owner.WeaponManager != null && !owner.WeaponManager.Disposed;
        }
    }
}
