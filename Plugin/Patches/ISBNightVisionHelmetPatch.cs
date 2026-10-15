using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBNightVisionHelmetPatch : ModulePatch
    {
        private static readonly Action<BotNightVisionData, bool> SetUsingNow = (Action<BotNightVisionData, bool>)Delegate.CreateDelegate(typeof(Action<BotNightVisionData, bool>), typeof(BotNightVisionData).GetProperty(nameof(BotNightVisionData.UsingNow)).GetSetMethod(true));
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotNightVisionData), "method_1");
        [PatchPrefix]
        private static bool Prefix(BotNightVisionData __instance, BotOwner ___BotOwner_0, Slot ___SlotHeadwear)
        {
            var settings = ___BotOwner_0?.Profile?.Info?.Settings;
            if (settings == null || !ShouldKeepHelmet(settings.Role, __instance.TradableItem, ___SlotHeadwear?.ContainedItem))
                return true;
            var togglable = __instance.NightVisionItem?.Togglable;
            if (togglable == null)
                return true;
            if (togglable.On)
                _ = ___BotOwner_0.GetPlayer.InventoryController.TryRunNetworkTransaction(togglable.Set(false, true, false));
            SetUsingNow(__instance, false);
            return false;
        }

        internal static bool ShouldKeepHelmet(WildSpawnType role, Item movingItem, Item equippedHelmet) => (int)role >= 13701 && (int)role <= 13716 && movingItem != null && ReferenceEquals(movingItem, equippedHelmet);
    }
}
