using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ISBSSOFExtra
{
    internal static class AishiGasMaskCompatibility
    {
        private static readonly HashSet<string> FullMasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "7f3d1490eb7e5442947f28f5"
        };
        private static Harmony _harmony;

        internal static void Enable(ManualLogSource log)
        {
            if (_harmony != null || !Chainloader.PluginInfos.ContainsKey("com.manimal.csgas"))
                return;
            var type = AccessTools.TypeByName("Manimal.CSGas.Gas.CsGasCloud");
            var method = type == null ? null : AccessTools.Method(type, "GetProtection", new[] { typeof(Player) });
            if (method == null || !method.IsStatic || !method.ReturnType.IsEnum ||
                !Enum.IsDefined(method.ReturnType, "Full") || !Enum.IsDefined(method.ReturnType, "Respirator"))
            {
                log?.LogError("ISB: installed CS Gas has no compatible mask-protection query.");
                return;
            }
            var harmony = new Harmony("com.samc137.isbsofextras.aishi-gas-masks");
            try
            {
                var prefix = typeof(AishiGasMaskCompatibility).GetMethod(nameof(BeforeProtection), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(method.ReturnType);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                _harmony = harmony;
            }
            catch (Exception error)
            {
                harmony.UnpatchSelf();
                log?.LogError("ISB CS Gas mask compatibility failed: " + error.GetBaseException().Message);
            }
        }

        internal static int Classify(string templateId)
        {
                return 1;
            return templateId != null && FullMasks.Contains(templateId) ? 2 : 0;
        }

        private static bool BeforeProtection<T>(Player __0, ref T __result) where T : struct
        {
            int protection;
            try
            {
                protection = Math.Max(Classify(__0?.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem?.TemplateId.ToString()), Classify(__0?.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem?.TemplateId.ToString()));
            }
            catch
            {
                return true;
            }
            if (protection == 0)
                return true;
            __result = protection == 1 ? Values<T>.Respirator : Values<T>.Full;
            return false;
        }

        private static class Values<T> where T : struct
        {
            internal static readonly T Full = (T)Enum.Parse(typeof(T), "Full");
            internal static readonly T Respirator = (T)Enum.Parse(typeof(T), "Respirator");
        }

        internal static void Dispose()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
