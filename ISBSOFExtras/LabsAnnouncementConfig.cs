using System;
using System.Collections.Generic;
using System.IO;

namespace ISBSSOFExtra
{
    internal static class AnnouncementTriggerDelays
    {
        internal const float CameraTrackingSeconds = 7.99f;
        internal const float CameraHuntSeconds = 8.02f;
        internal const float GateBSeconds = 8.54f;
        internal const float LabsChaosSeconds = 5.88f;
        internal static bool IsRussian(string audio) => Path.GetFileNameWithoutExtension(audio ?? string.Empty).EndsWith("_RU", StringComparison.OrdinalIgnoreCase);
        internal static bool IsCameraTracking(string audio) => MatchesVariant(audio, "CameraStart");
        internal static bool IsCameraHunt(string audio) => MatchesVariant(audio, "CameraEnemies");
        private static bool MatchesVariant(string audio, string expected)
        {
            string name = Path.GetFileNameWithoutExtension(audio ?? string.Empty);
            return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase) || string.Equals(name, expected + "_RU", StringComparison.OrdinalIgnoreCase);
        }
        internal static float EvacuationGates(string audio) => IsRussian(audio) ? 12.24f : 12.85f;
        internal static float FinalLockdown(string audio) => GasRules.ExtractionLockDelay;
        internal static float GetDelay(string audio)
        {
            string name = Path.GetFileNameWithoutExtension(audio ?? string.Empty);
            if (IsCameraTracking(name))
                return IsRussian(name) ? 5.9f : CameraTrackingSeconds;
            if (IsCameraHunt(name))
                return IsRussian(name) ? 8.0f : CameraHuntSeconds;
            if (string.Equals(name, "ISBGateB", StringComparison.OrdinalIgnoreCase))
                return GateBSeconds;
            if (string.Equals(name, "ISB-LabsChaos", StringComparison.OrdinalIgnoreCase))
                return LabsChaosSeconds;
            if (GasRules.MatchesClip(name, GasRules.WarningClip))
                return IsRussian(name) ? 9.04f : 11.24f;
            if (GasRules.MatchesClip(name, "DecontaminationProcess05"))
                return EvacuationGates(name);
            if (GasRules.MatchesClip(name, GasRules.TriggerClip))
                return FinalLockdown(name);
            return 0f;
        }
    }

    internal sealed class AnnouncementAudioDefinition
    {
        public bool enabled { get; set; } = true;
        public string audio { get; set; } = string.Empty;
        public float nextDelay { get; set; }
        public float? afterDecontaminationSeconds { get; set; }
    }

    internal sealed class AnnouncementSequenceDefinition
    {
        public bool enabled { get; set; } = true;
        public float weight { get; set; } = 1f;
        public float initialDelay { get; set; }
        public AnnouncementAudioDefinition[] audios { get; set; } = Array.Empty<AnnouncementAudioDefinition>();
    }

    internal sealed class LabsAnnouncementsDefinition
    {
        public AnnouncementSequenceDefinition[] regular { get; set; } = Array.Empty<AnnouncementSequenceDefinition>();
        public AnnouncementSequenceDefinition[] blackout { get; set; } = Array.Empty<AnnouncementSequenceDefinition>();
    }

    internal sealed class PreBlackoutSequenceDefinition
    {
        public bool enabled { get; set; } = true;
        public float weight { get; set; } = 1f;
        public string preBlackout { get; set; } = string.Empty;
        public float afterBlackoutDelay { get; set; } = 0.75f;
        public AnnouncementAudioDefinition[] afterBlackout { get; set; } = Array.Empty<AnnouncementAudioDefinition>();
    }

    internal sealed class LabsAnnouncementPlan
    {
        public string PreBlackout { get; set; } = string.Empty;
        public float InitialDelay { get; set; }
        public AnnouncementAudioDefinition[] Audios { get; set; } = Array.Empty<AnnouncementAudioDefinition>();
    }

    internal static class LabsAnnouncementConfig
    {
        internal static LabsAnnouncementsDefinition Normalize(LabsAnnouncementsDefinition definition, Func<string, bool> audioExists)
        {
            return new LabsAnnouncementsDefinition
            {
                regular = NormalizeSequences(definition?.regular, audioExists),
                blackout = NormalizeSequences(definition?.blackout, audioExists)
            };
        }

        internal static PreBlackoutSequenceDefinition[] NormalizeEvents(IEnumerable<PreBlackoutSequenceDefinition> definitions, Func<string, bool> audioExists)
        {
            var result = new List<PreBlackoutSequenceDefinition>();
            if (definitions == null)
                return result.ToArray();
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.enabled || NormalizeWeight(definition.weight) == 0f)
                    continue;
                string preBlackout = NormalizeAudioName(definition.preBlackout, audioExists);
                if (preBlackout == null)
                    continue;
                result.Add(new PreBlackoutSequenceDefinition { weight = definition.weight, preBlackout = preBlackout, afterBlackoutDelay = NormalizeDelay(definition.afterBlackoutDelay), afterBlackout = NormalizeAudios(definition.afterBlackout, audioExists) });
            }

            return result.ToArray();
        }

        internal static LabsAnnouncementPlan Select(LabsAnnouncementsDefinition definition, PreBlackoutSequenceDefinition[] events, bool blackout, bool containmentBreach, bool eventEnabled, Func<float> random)
        {
            if (blackout && containmentBreach && eventEnabled)
            {
                var selected = SelectWeighted(events, sequence => sequence.enabled ? sequence.weight : 0f, random);
                if (selected == null)
                    return new LabsAnnouncementPlan();
                return new LabsAnnouncementPlan
                {
                    PreBlackout = selected.preBlackout,
                    InitialDelay = selected.afterBlackoutDelay,
                    Audios = selected.afterBlackout
                };
            }

            var sequences = blackout ? definition?.blackout : definition?.regular;
            var selectedSequence = SelectWeighted(sequences, sequence => sequence.enabled ? sequence.weight : 0f, random);
            if (selectedSequence == null)
                return new LabsAnnouncementPlan();
            return new LabsAnnouncementPlan
            {
                InitialDelay = selectedSequence.initialDelay,
                Audios = selectedSequence.audios
            };
        }

        private static T SelectWeighted<T>(T[] sequences, Func<T, float> getWeight, Func<float> random)
            where T : class
        {
            if (sequences == null)
                return null;
            double total = 0d;
            foreach (var sequence in sequences)
                if (sequence != null)
                    total += NormalizeWeight(getWeight(sequence));
            if (total <= 0d)
                return null;
            double remaining = random() * total;
            T last = null;
            foreach (var sequence in sequences)
            {
                if (sequence == null)
                    continue;
                float weight = NormalizeWeight(getWeight(sequence));
                if (weight == 0f)
                    continue;
                last = sequence;
                if (remaining < weight)
                    return sequence;
                remaining -= weight;
            }

            return last;
        }

        private static float NormalizeWeight(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 0f : value;
        }

        private static AnnouncementSequenceDefinition[] NormalizeSequences(AnnouncementSequenceDefinition[] definitions, Func<string, bool> audioExists)
        {
            var result = new List<AnnouncementSequenceDefinition>();
            if (definitions == null)
                return result.ToArray();
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.enabled || NormalizeWeight(definition.weight) == 0f)
                    continue;
                var audios = NormalizeAudios(definition.audios, audioExists);
                if (audios.Length == 0)
                    continue;
                result.Add(new AnnouncementSequenceDefinition { weight = definition.weight, initialDelay = NormalizeDelay(definition.initialDelay), audios = audios });
            }

            return result.ToArray();
        }

        private static AnnouncementAudioDefinition[] NormalizeAudios(AnnouncementAudioDefinition[] definitions, Func<string, bool> audioExists)
        {
            var result = new List<AnnouncementAudioDefinition>();
            if (definitions == null)
                return result.ToArray();
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.enabled)
                    continue;
                string audio = NormalizeAudioName(definition.audio, audioExists);
                if (audio == null)
                    continue;
                result.Add(new AnnouncementAudioDefinition { audio = audio, nextDelay = NormalizeDelay(definition.nextDelay), afterDecontaminationSeconds = definition.afterDecontaminationSeconds.HasValue ? (float?)NormalizeDelay(definition.afterDecontaminationSeconds.Value) : null });
            }

            return result.ToArray();
        }

        private static string NormalizeAudioName(string value, Func<string, bool> audioExists)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string name = value.Trim();
            if (!name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf('|') >= 0 || !audioExists(name))
                return null;
            return name;
        }

        private static float NormalizeDelay(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(86400f, value));
        }
    }
}
