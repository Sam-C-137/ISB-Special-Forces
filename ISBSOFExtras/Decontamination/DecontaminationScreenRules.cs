using System;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal static class DecontaminationScreenRules
    {
        internal const string MaterialPrefix = "ISB_DecontaminationScreen_";
        private static string OriginalMaterialName(string name)
        {
            name = (name ?? string.Empty).Replace(" (Instance)", string.Empty);
            if (name.StartsWith(MaterialPrefix, StringComparison.Ordinal))
                name = name.Substring(MaterialPrefix.Length);
            const string emergencyPrefix = "ISB_Prop_Emergency_";
            if (name.StartsWith(emergencyPrefix, StringComparison.Ordinal))
                name = name.Substring(emergencyPrefix.Length);
            return name;
        }

        internal static bool Match(string renderer, string material, int slot, out Rect uv, out bool atlas)
        {
            uv = new Rect(0f, 0f, 1f, 1f);
            atlas = false;
            if (string.IsNullOrEmpty(renderer) || string.IsNullOrEmpty(material) || renderer.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            material = OriginalMaterialName(material);
            switch (material.ToLowerInvariant())
            {
                case "lab_monitor_1":
                case "lcd_screen_error":
                    return true;
                case "tv_lcd_display_dif":
                case "tv_lcd_display_not_broken":
                case "tv_lcd_display_emissive_video":
                    uv = new Rect(.01339f, .43155f, .98717f, .55370f);
                    return true;
                case "lab_glow":
                case "lab_glow_renovation":
                    uv = new Rect(-.20129f, .18079f, 1.39563f, .63574f);
                    return slot == 1 && renderer.StartsWith("Lab_recreation_LED_LOD", StringComparison.OrdinalIgnoreCase);
                case "lab_screens_wall_mountains_renovation":
                    if (renderer.StartsWith("Lab_recreation_LED_Big_01", StringComparison.OrdinalIgnoreCase))
                        uv = new Rect(0f, 0f, .385643f, 1f);
                    else if (renderer.StartsWith("Lab_recreation_LED_Big_02", StringComparison.OrdinalIgnoreCase))
                        uv = new Rect(.00256f, .00324f, .99549f, .99430f);
                    return true;
                case "laptop_d":
                    uv = new Rect(.54f, .386f, .442f, .253f);
                    atlas = true;
                    return true;
                case "pc_laptop_d":
                    uv = new Rect(.465f, .171f, .395f, .251f);
                    atlas = true;
                    return true;
                case "pc_monitorultrawide":
                    uv = new Rect(.006f, .006f, .929f, .395f);
                    atlas = true;
                    return true;
                case "lab_glow2":
                case "lab_sls_plastic":
                    return slot == 1 && renderer.StartsWith("Projection_Screen_LOD", StringComparison.OrdinalIgnoreCase);
                default:
                    if (slot == 1 && renderer.StartsWith("Lab_recreation_LED_LOD", StringComparison.OrdinalIgnoreCase))
                    {
                        uv = new Rect(-.20129f, .18079f, 1.39563f, .63574f);
                        return true;
                    }

                    if (slot == 1 && renderer.StartsWith("Projection_Screen_LOD", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (slot != 0)
                        return false;
                    if (renderer.StartsWith("pc_laptop_d_LOD", StringComparison.OrdinalIgnoreCase))
                    {
                        uv = new Rect(.465f, .171f, .395f, .251f);
                        atlas = true;
                        return true;
                    }

                    if (renderer.StartsWith("laptop_d_LOD", StringComparison.OrdinalIgnoreCase))
                    {
                        uv = new Rect(.54f, .386f, .442f, .253f);
                        atlas = true;
                        return true;
                    }

                    if (renderer.StartsWith("pc_monitorultrawide_LOD", StringComparison.OrdinalIgnoreCase))
                    {
                        uv = new Rect(.006f, .006f, .929f, .395f);
                        atlas = true;
                        return true;
                    }

                    if (renderer.StartsWith("Lab_recreation_LED_Big_01", StringComparison.OrdinalIgnoreCase))
                    {
                        uv = new Rect(0f, 0f, .385643f, 1f);
                        return true;
                    }

                    if (renderer.StartsWith("Lab_recreation_LED_Big_02", StringComparison.OrdinalIgnoreCase))
                    {
                        uv = new Rect(.00256f, .00324f, .99549f, .99430f);
                        return true;
                    }

                    return renderer.Equals("lab_monitor", StringComparison.OrdinalIgnoreCase) || renderer.StartsWith("lab_monitor (", StringComparison.OrdinalIgnoreCase) || renderer.StartsWith("lab_monitor_LOD", StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static float AspectRatio(string renderer, string material, Vector3 scale, Vector3 boundsSize = default(Vector3))
        {
            material = OriginalMaterialName(material).ToLowerInvariant();
            float ratio;
            float horizontal = Math.Abs(scale.x);
            float vertical = Math.Abs(scale.z);
            switch (material)
            {
                case "lab_monitor_1":
                    ratio = boundsSize.y > .001f && boundsSize.z > .001f ? boundsSize.y / boundsSize.z : 1f;
                    horizontal = Math.Abs(scale.y);
                    break;
                case "lcd_screen_error":
                    ratio = 11.22f;
                    break;
                case "laptop_d":
                    ratio = 1.71f;
                    break;
                case "pc_laptop_d":
                    ratio = 1.56f;
                    break;
                case "pc_monitorultrawide":
                    ratio = 2.35f;
                    break;
                case "lab_glow":
                case "lab_glow_renovation":
                    ratio = 2.21f;
                    break;
                case "lab_screens_wall_mountains_renovation":
                    ratio = boundsSize.x > .001f && boundsSize.z > .001f ? boundsSize.x / boundsSize.z : renderer.StartsWith("Lab_recreation_LED_Big_01", StringComparison.OrdinalIgnoreCase) ? 1.56f : renderer.StartsWith("Lab_recreation_LED_Big_02", StringComparison.OrdinalIgnoreCase) ? 2.30f : 4.34f;
                    break;
                case "lab_glow2":
                case "lab_sls_plastic":
                    ratio = 1.725f;
                    break;
                default:
                    ratio = 1.783f;
                    break;
            }

            if (horizontal > .0001f && vertical > .0001f)
                ratio *= horizontal / vertical;
            return Mathf.Clamp(Mathf.Round(ratio * 100f) / 100f, .1f, 32f);
        }

        internal static Rect FitVideo(Rect area, float screenAspect)
        {
            const float videoAspect = 1280f / 648f;
            float width = screenAspect > videoAspect ? videoAspect / screenAspect : 1f;
            float height = screenAspect < videoAspect ? screenAspect / videoAspect : 1f;
            return new Rect(area.x + area.width * (1f - width) * .5f, area.y + area.height * (1f - height) * .5f, area.width * width, area.height * height);
        }
    }
}
