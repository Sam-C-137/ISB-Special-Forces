using System;
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal static class BlackoutTargets
    {
        internal static bool IsFacilityComponent(Component component)
        {
            if (component == null || FacilityLightingInterop.IsManaged(component))
                return false;
            if (component is Renderer screen && OfficeScreenCanvas.IsTerminalScreen(screen))
                return false;
            var scene = component.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || !scene.name.StartsWith("Laboratory_", StringComparison.Ordinal))
                return false;
            for (var parent = component.transform; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<Player>() != null || parent.GetComponent<LootItem>() != null || parent.GetComponent<WeaponPrefab>() != null || parent.GetComponent<TacticalComboVisualController>() != null)
                    return false;
                if (parent.name.StartsWith(TakeoverSirens.RootPrefix, StringComparison.Ordinal) || parent.name.StartsWith("ISB_Prop_EmergencyIllumination", StringComparison.Ordinal) || parent.name.StartsWith("ISB_ContainmentBreachFire_", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
