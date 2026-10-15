using EFT.InventoryLogic;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBDogtagLevelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(DogtagComponent).GetMethod("CG_Ctor2", BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(DogtagComponent __instance, ref string __result)
        {
            var item = __instance?.Item;
            if (item == null)
                return;
            if (!ISBDogtagState.ShouldDisplayAsISB(item.TemplateId.ToString()))
                return;
            __result = ISB.Compatibility.ISBDogtagLevels.Choose(item.TemplateId.ToString(), item.Id.ToString(), __instance.Level).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
