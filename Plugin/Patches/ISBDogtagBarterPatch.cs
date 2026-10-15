using System;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using UnityEngine;
using HarmonyLib;
using SPT.Reflection.Patching;
using ISBSpecialForces.Components;
#if ISB_SPT41
using Requisite = EFT.Trading.Requisite;
using Assortment = EFT.Trading.Assortment;
#else
using Requisite = GClass2571;
using Assortment = TraderAssortmentControllerClass;

#endif
namespace ISBSpecialForces.Patches
{
    public sealed class ISBDogtagRequirementLabelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertyGetter(typeof(Requisite), "IsDogtagRequired");
        [PatchPrefix]
        private static bool Prefix(Requisite __instance, ref bool __result)
        {
            var item = __instance?.RequiredItem;
            if (item == null || !ISB.Compatibility.ISBDogtagLevels.Range(item.TemplateId.ToString(), out _, out _))
                return true;
            __result = false;
            return false;
        }
    }

    public sealed class ISBDogtagBarterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Requisite), "IsRequired");
        [PatchPrefix]
        private static bool Prefix(Requisite __instance, Item __0, ref bool __result)
        {
            var required = __instance.RequiredItem.TemplateId.ToString();
            var candidate = __0.TemplateId.ToString();
            if (ISBDogtagState.ShouldDisplayAsISB(required))
            {
                var identity = __0.GetItemComponent<DogtagComponent>();
                __result = ISBDogtagBarterPolicy.Matches(required, candidate, __instance.Level, (int)__instance.Side, identity == null ? (int? )null : identity.Level, identity == null ? (int? )null : (int)identity.Side);
                return false;
            }

            if (__instance.IsDogtagRequired && ISBDogtagState.ShouldDisplayAsISB(candidate))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
