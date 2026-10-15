using System;

namespace ISBSSOFExtra
{
    internal static class DecontaminationVideoTimeline
    {
        internal const string FileName = "DeconCold.mp4";
        internal const double Duration = 692.7d;
        internal static readonly double[] AnnouncementStarts =
        {
            18.361d,
            216.389d,
            414.3835d,
            612.3782d,
            642.4275d,
            658.7275d,
            681.2582d
        };
        internal static string Clip(int index, bool russian = false) => "DecontaminationProcess" + (index + 1).ToString("00") + (russian ? "_RU.wav" : ".wav");
        internal static int Index(string clip)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(clip ?? string.Empty);
            for (int i = 0; i < AnnouncementStarts.Length; i++)
                if (string.Equals(name + ".wav", Clip(i), StringComparison.OrdinalIgnoreCase) || string.Equals(name + ".wav", Clip(i, true), StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        internal static double Start(int index, bool russian)
        {
            double start = AnnouncementStarts[index];
            if (russian && index == 4)
                start += .61d;
            if (russian && index == 6)
                start += 2.02d;
            return start;
        }

        internal static bool Matches(AnnouncementAudioDefinition[] entries, int index)
        {
            if (entries == null || index < 0 || index > entries.Length - AnnouncementStarts.Length)
                return false;
            bool russian = entries[index]?.audio?.EndsWith("_RU.wav", StringComparison.OrdinalIgnoreCase) == true;
            for (int i = 0; i < AnnouncementStarts.Length; i++)
                if (entries[index + i] == null || !string.Equals(entries[index + i].audio, Clip(i, russian), StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        internal static bool ValidStart(double start) => !double.IsNaN(start) && !double.IsInfinity(start) && start >= 0d;
    }
}
