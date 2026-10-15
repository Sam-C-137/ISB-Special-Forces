using System;

namespace ISBSSOFExtra
{
    internal static class TakeoverAlarmChoice
    {
        internal const int Count = 5;
        internal static bool Valid(int index) => index >= 0 && index < Count;
        internal static string File(int index, string original)
        {
            if (!Valid(index))
                throw new ArgumentOutOfRangeException(nameof(index));
            return index == 0 ? (string.IsNullOrWhiteSpace(original) ? "ISB-PreLockdownAlarm.wav" : original.Trim()) : "ISB-PreLockdownAlarm" + (index + 1) + ".wav";
        }
    }
}
