using BepInEx.Bootstrap;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBPackNStrapInventoryPatch : ModulePatch
    {
        internal const string PluginGuid = "com.wtt.packnstrap";

        internal static void Initialize()
        {
            if (Chainloader.PluginInfos.ContainsKey(PluginGuid))
                new ISBPackNStrapInventoryPatch().Enable();
        }

        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.ReplaceInventory));

        [PatchPostfix]
        private static void PatchPostfix(InventoryController __instance, Inventory __0)
        {
            RestoreBelt(__instance?.Profile as Profile, __0?.Equipment);
        }

        internal static bool RestoreBelt(Profile profile, InventoryEquipment equipment)
        {
            var settings = profile?.Info?.Settings;
            if (profile?.Info?.Side != EPlayerSide.Savage || settings == null || !WildSpawnTypeExtensions.IsISB(settings.Role))
                return false;
            var slot = equipment?.GetSlot(EquipmentSlot.ArmBand);
            var item = slot?.ContainedItem;
            if (item?.GetType().FullName != "PackNStrap.Core.Items.CustomBeltItemClass" || !item.IsContainer)
                return false;
            slot.Deleted = false;
            return true;
        }
    }
}
