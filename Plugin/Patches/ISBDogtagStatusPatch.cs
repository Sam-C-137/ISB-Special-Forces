using EFT.InventoryLogic;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBDogtagStatusPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(DogtagComponent).GetMethod("CG_Ctor4", BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(DogtagComponent __instance, ref string __result)
        {
            var item = __instance?.Item;
            if (item == null)
                return;
            if (!ISBDogtagState.ShouldDisplayAsISB(item.TemplateId.ToString()))
                return;
            __result = "[REDACTED]";
        }
    }
}
