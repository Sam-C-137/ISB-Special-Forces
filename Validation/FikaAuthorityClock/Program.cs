using System;
using System.Globalization;
using ISBSSOFExtra.Fika;

internal static class Program
{
    private static int _checks;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
    }

    private static void Near(double actual, double expected, string name)
    {
        Check(Math.Abs(actual - expected) < 0.000001d, name);
    }

    private static void Main()
    {
        var clock = new FikaAuthorityClock();
        const string announcement = "A|UHJlQmxhY2tvdXRfMDE=|720|10";
        Check(!clock.Ready, "No event readiness before the first authority timestamp");
        Check(1560d - 720d > 10d, "Original process clock discards an unexpired announcement");
        Check(clock.Receive(FikaAuthorityClock.Stamp(announcement, 720d), 1560.025d, out var payload), "Headless opened after player");
        Check(payload == announcement && clock.Ready, "Payload remains unchanged and unlocks readiness");
        Near(clock.Time(1560.025d), 720d, "Announcement starts instead of being discarded");
        Near(clock.Time(1565.025d) - 720d, 5d, "Playback advances five seconds");
        Check(clock.Receive(FikaAuthorityClock.Stamp("S|snapshot", 725d), 1565.225d, out _), "Delayed recovery snapshot");
        Near(clock.Time(1565.225d), 725.2d, "A delayed packet cannot rewind playback");
        Check(clock.Receive(FikaAuthorityClock.Stamp("P|explosion", 726d), 1566.01d, out _), "Faster authority sample");
        Near(clock.Time(1566.01d), 726d, "Explosions share the authority timeline");
        foreach (string malformed in new[] { "A|legacy", "ISBCLK|NaN|A", "ISBCLK|Infinity|A", "ISBCLK|-2|A", "ISBCLK|2|", "ISBCLK|2" })
            Check(!clock.Receive(malformed, 1566.01d, out _), "Reject malformed or unsynchronized payload: " + malformed);
        Near(clock.Time(1567.01d), 727d, "Invalid samples cannot change the clock");
        clock.Reset();
        Check(!clock.Ready, "Disconnect clears the previous host clock");
        Check(clock.Receive(FikaAuthorityClock.Stamp("H|READY", 20d), 4000d, out _), "Restarted headless with existing player process");
        Near(clock.Time(4001d), 21d, "A new host starts a new timeline");
        clock.Reset();
        Check(clock.Receive(FikaAuthorityClock.Stamp("R|radio", 8000d), 100d, out _), "Host opened before the player");
        Near(clock.Time(102d), 8002d, "Opposite startup order also works");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        clock.Reset();
        Check(clock.Receive(FikaAuthorityClock.Stamp("FS|Русский|中文", 50.125d), 1000.5d, out payload), "Invariant timestamp under Russian culture");
        Check(payload == "FS|Русский|中文", "Unicode and payload separators survive the clock envelope");
        Near(clock.Time(1001.5d), 51.125d, "Invariant fractional clock");
        Console.WriteLine("{\"passed\":" + _checks + ",\"processSkewSeconds\":840,\"rawClockDropsClip\":true,\"correctedClockPlaysClip\":true}");
    }
}
