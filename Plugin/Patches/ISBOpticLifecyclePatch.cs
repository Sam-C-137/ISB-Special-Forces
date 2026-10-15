using System.Reflection;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using ISBSpecialForces.Components;
using UnityEngine;
using UnityEngine.Rendering;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBOpticLifecyclePatch : ModulePatch
    {
        private static readonly AccessTools.FieldRef<ScopeZoomHandler, IAdjustableOpticData> OpticData = AccessTools.FieldRefAccess<ScopeZoomHandler, IAdjustableOpticData>("_adjustableOpticData");
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(OpticComponentUpdater), "LateUpdate");
        internal static bool Ready(ScopeZoomHandler scope) => scope != null && OpticData(scope) != null;
        [PatchPrefix]
        private static bool Prefix(OpticSight ____opticSight, ScopeZoomHandler ____scopeZoomHandler)
        {
            return ISBFikaAuthority.CanPresentLocally && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && ____opticSight != null && ____opticSight.CameraData != null && (!____opticSight.CameraData.IsAdjustableOptic || Ready(____scopeZoomHandler));
        }
    }

    internal sealed class ISBOpticEnablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(OpticSight), "OnEnable");
        [PatchPrefix]
        private static bool Prefix(OpticSight __instance)
        {
            return ISBFikaAuthority.CanPresentLocally && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && CameraManager.Instance?.OpticCameraManager != null && __instance.GetComponentInParent<WeaponPrefab>() != null && __instance.ScopeData != null && __instance.LensRenderer != null;
        }
    }

    internal sealed class ISBOpticDisablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(OpticSight), "OnDisable");
        [PatchPrefix]
        private static bool Prefix(OpticSight __instance)
        {
            bool ready = ISBFikaAuthority.CanPresentLocally && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && CameraManager.Instance?.OpticCameraManager != null && __instance.GetComponentInParent<WeaponPrefab>() != null && __instance.ScopeData != null && __instance.LensRenderer != null;
            if (!ready)
            {
                var state = OpticSight.OpticSightState.Value;
                if (ReferenceEquals(state.OpticSight, __instance))
                {
                    state.IsEnabled = false;
                    state.OpticSight = null;
                    OpticSight.OpticSightState.Value = state;
                }
            }
            return ready;
        }
    }

    internal sealed class ISBOpticCameraInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(AccessTools.Property(typeof(CameraManager), "OpticCameraManager").PropertyType, "Init");
        [PatchPrefix]
        private static void Prefix()
        {
            var state = OpticSight.OpticSightState.Value;
            if (state.IsEnabled && (state.OpticSight == null || state.OpticSight.ScopeData == null))
            {
                state.IsEnabled = false;
                state.OpticSight = null;
                OpticSight.OpticSightState.Value = state;
            }
        }
    }

    internal sealed class ISBOpticReticleTransformPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(OpticRetrice), nameof(OpticRetrice.UpdateTransform));
        [PatchPrefix]
        private static bool Prefix(OpticRetrice __instance, OpticSight __0)
        {
            return ISBFikaAuthority.CanPresentLocally && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && __instance.Renderer != null && __0 != null && __0.ScopeData != null && __0.ScopeData.Reticle != null;
        }
    }
}
