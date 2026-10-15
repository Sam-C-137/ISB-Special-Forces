using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal static class LabsRenderTextureCompatibility
    {
        private static Harmony _harmony;

        internal static void Enable()
        {
            if (_harmony != null)
                return;
            _harmony = new Harmony("com.samc137.isbsofextras.labs-render-textures");
            var prefix = new HarmonyMethod(typeof(LabsRenderTextureCompatibility), nameof(BeforeSmallTarget));
            _harmony.Patch(AccessTools.Method(typeof(PrismEffects),
#if ISB_SPT41
                "OnRenderImageInternal"
#else
                "method_12"
#endif
            ), prefix: prefix);
            var bloom = Type.GetType("UnityStandardAssets.ImageEffects.Bloom, Assembly-CSharp-firstpass");
            var render = bloom?.GetMethod("OnRenderImageImpl", BindingFlags.Instance | BindingFlags.NonPublic);
            if (render != null)
                _harmony.Patch(render, prefix: prefix);
        }

        private static bool BeforeSmallTarget(RenderTexture source, RenderTexture destination)
        {
            if (source == null || source.width >= 32 && source.height >= 32)
                return true;
            var world = Singleton<GameWorld>.Instance;
            if (world == null || !string.Equals(world.MainPlayer?.Location, "laboratory", StringComparison.OrdinalIgnoreCase))
                return true;
            if (source.width > 0 && source.height > 0 && source.IsCreated() &&
                (destination != null && destination.width > 0 && destination.height > 0 || destination == null && Screen.width > 0 && Screen.height > 0))
            {
                var previous = RenderTexture.active;
                try { Graphics.Blit(source, destination); }
                finally { RenderTexture.active = previous; }
            }
            return false;
        }

        internal static void Dispose()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
