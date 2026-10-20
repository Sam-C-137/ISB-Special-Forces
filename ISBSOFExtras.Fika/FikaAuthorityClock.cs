using System;
using System.Globalization;
using System.Diagnostics;

namespace ISBSSOFExtra.Fika
{
    internal sealed class FikaAuthorityClock
    {
        private const string Prefix = "ISBCLK|";
        private static readonly long StartedAt = Stopwatch.GetTimestamp();
        internal static double LocalTime => (Stopwatch.GetTimestamp() - StartedAt) / (double)Stopwatch.Frequency;
        private double _offset;
        internal bool Ready { get; private set; }

        internal double Time(double localTime) => localTime + (Ready ? _offset : 0d);

        internal static string Stamp(string payload, double authorityTime)
        {
            return Prefix + authorityTime.ToString("R", CultureInfo.InvariantCulture) + "|" + payload;
        }

        internal bool Receive(string stampedPayload, double localTime, out string payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(stampedPayload) || !stampedPayload.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            int separator = stampedPayload.IndexOf('|', Prefix.Length);
            if (separator < 0 || separator == stampedPayload.Length - 1)
                return false;
            if (!double.TryParse(stampedPayload.Substring(Prefix.Length, separator - Prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out double authorityTime) || !Valid(authorityTime) || !Valid(localTime))
                return false;
            double offset = authorityTime - localTime;
            if (!Ready || offset > _offset)
                _offset = offset;
            Ready = true;
            payload = stampedPayload.Substring(separator + 1);
            return true;
        }

        internal void Reset()
        {
            Ready = false;
            _offset = 0d;
        }

        private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    }
}
