using System;
using System.Collections.Generic;

namespace ISBSSOFExtra
{
    internal sealed class DecontaminationConfig
    {
        public bool videoTimeline { get; set; } = true;
        public bool screenVideo { get; set; } = true;
        public bool enabled { get; set; }
        public bool visualsEnabled { get; set; } = true;
        public bool damagePlayers { get; set; } = true;
        public bool damageBots { get; set; } = true;
        public float damagePerSecond { get; set; } = 12.5f;
        public float graceSeconds { get; set; } = 3f;
        public float fillSeconds { get; set; } = 12f;
        public string[] protectiveMasks { get; set; } = new[]
        {
            "5b432c305acfc40019478128",
            "2210de8d7b3e5d02b70e851a",
            "d42ca83bead35a4db91457a7",
            "eae1b78f028759df97ec2ba8",
            "60363c0c92ec1c31037959f5",
            "c544b1b34703aebab8ee0e5c",
            "689b880fff8b4adc420f5b56",
            "693ce88b2d491ae03c5326cd",
            "6939d6ae15efda5797902659",
            "6a279d1b7bb85ffaa461cd9e",
            "6a279c95534fb23d302b6b27",
            "689b404db49f27df1c0873f6",
            "6a090656da8c0b1a1fb424eb",
            "697df1cc71b3b5b6c5341071",
            "697df3bad7387ce0f2341072",
            "689cefd7c6d829d1c30a8404",
            "689b7d32e920d74a02e5a63c",
            "68a60e568c72b73e32842085",
            "68a8dac9242e2efa4a35ecc0",
            "59e7715586f7742ee5789605",
            "6954a787a8530d7fbd513bd8",
            "66326bfd46817c660d01514f",
            "66326bfd46817c660d015130",
            "66326bfd46817c660d01514e",
            "3401a3220a7355a3ad4b0f54",
            "a7d9ede064cc53bd8cc78bfe",
            "9a4a5d94afc658af9fb22ad6",
            "da716663ea505d11956d19f8",
            "7f3d1490eb7e5442947f28f5",
        };
        public GasArea[] areas { get; set; } = Array.Empty<GasArea>();
        public GasPoint[] emitters { get; set; } = Array.Empty<GasPoint>();
        public int maxActiveEmitters { get; set; } = 4;
        public int particlesPerEmitter { get; set; } = 16;
        public int ambientParticles { get; set; } = 32;
        public float visualDistance { get; set; } = 45f;

        internal DecontaminationConfig Normalize()
        {
            damagePerSecond = GasRules.Clamp(damagePerSecond, 0f, 500f, 12.5f);
            graceSeconds = GasRules.Clamp(graceSeconds, 0f, 60f, 3f);
            fillSeconds = GasRules.Clamp(fillSeconds, 1f, 120f, 12f);
            maxActiveEmitters = Math.Max(0, Math.Min(8, maxActiveEmitters));
            particlesPerEmitter = Math.Max(1, Math.Min(48, particlesPerEmitter));
            ambientParticles = Math.Max(0, Math.Min(96, ambientParticles));
            visualDistance = GasRules.Clamp(visualDistance, 5f, 80f, 45f);
            var masks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string mask in protectiveMasks ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(mask) && mask.Trim().Length == 24 && masks.Count < 64)
                    masks.Add(mask.Trim());
            protectiveMasks = new string[masks.Count];
            masks.CopyTo(protectiveMasks);
            var validAreas = new List<GasArea>();
            foreach (var area in areas ?? Array.Empty<GasArea>())
                if (area != null && area.Valid && validAreas.Count < 32)
                    validAreas.Add(area);
            areas = validAreas.ToArray();
            var points = new List<GasPoint>();
            foreach (var point in emitters ?? Array.Empty<GasPoint>())
                if (point != null && point.Valid && points.Count < 256)
                    points.Add(point);
            emitters = points.ToArray();
            return this;
        }

        internal bool Contains(float x, float y, float z)
        {
            foreach (var area in areas)
                if (area.Contains(x, y, z))
                    return true;
            return false;
        }
    }

    internal class GasPoint
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        internal bool Valid => GasRules.Finite(x) && GasRules.Finite(y) && GasRules.Finite(z) && Math.Abs(x) < 10000f && Math.Abs(y) < 10000f && Math.Abs(z) < 10000f;
    }

    internal sealed class GasArea : GasPoint
    {
        public float width { get; set; }
        public float height { get; set; }
        public float depth { get; set; }
        internal new bool Valid => base.Valid && GasRules.Finite(width) && GasRules.Finite(height) && GasRules.Finite(depth) && width > 0 && height > 0 && depth > 0 && width <= 2000 && height <= 2000 && depth <= 2000;

        internal bool Contains(float px, float py, float pz) => Math.Abs(px - x) <= width * .5f && Math.Abs(py - y) <= height * .5f && Math.Abs(pz - z) <= depth * .5f;
    }

    internal static class GasRules
    {
        internal static float ActivationDelay(string clip) => AnnouncementTriggerDelays.IsRussian(clip) ? 7.48f : 9.5f;
        internal const float TickInterval = .5f;
        internal const float ExtractionLockDelay = 15f;
        internal static float MaskedDamageRate(float seconds, float unmaskedDamage) => Math.Min(unmaskedDamage, .5f + Math.Min(4.5f, .5f * (float)Math.Floor(Math.Max(0f, seconds) / 60f)));
        internal const string TriggerClip = "DecontaminationProcess07";
        internal const string WarningClip = "DecontaminationProcess01";
        internal static bool MatchesClip(string clip, string expected)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(clip ?? string.Empty);
            return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase) || string.Equals(name, expected + "_RU", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static float Clamp(float value, float min, float max, float fallback) => Finite(value) ? Math.Max(min, Math.Min(max, value)) : fallback;
        internal static bool OwnsDamage(bool localPlayer, bool bot, bool authority) => bot ? authority : localPlayer;
        internal static float DamageSeconds(float previousExposure, float elapsed, float grace) => Math.Max(0f, previousExposure + elapsed - grace) - Math.Max(0f, previousExposure - grace);
        internal static float Step(double previous, double now) => (float)Math.Max(0d, Math.Min(1d, now - previous));
    }
}
