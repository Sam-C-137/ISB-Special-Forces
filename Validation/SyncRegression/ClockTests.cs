using System;
using System.Diagnostics;
using System.Threading;
using ISBSSOFExtra.Fika;

internal static class ClockTests
{
    internal static void Run()
    {
        double start = FikaAuthorityClock.LocalTime;
        var elapsed = Stopwatch.StartNew();
        UnityEngine.Time.realtimeSinceStartup = 90000f;
        Thread.Sleep(60);
        double end = FikaAuthorityClock.LocalTime;
        Tests.Check(end > start + .04d, "clock advances while no game frame is produced");
        Tests.Check(Math.Abs((end - start) - elapsed.Elapsed.TotalSeconds) < .05d, "clock follows continuous elapsed time");
        UnityEngine.Time.realtimeSinceStartup = 0f;
        Tests.Check(FikaAuthorityClock.LocalTime >= end, "scene time reset cannot reset the network clock");

        var frameClock = new FikaAuthorityClock();
        frameClock.Receive(FikaAuthorityClock.Stamp("A|announcement", 100d), 1000d, out _);
        double frameError = frameClock.Time(1001.45d) - 100.05d;
        Tests.Check(Math.Abs(frameError - 1.4d) < .000001d, "reproduces frame clock catch-up offset");

        var continuousClock = new FikaAuthorityClock();
        continuousClock.Receive(FikaAuthorityClock.Stamp("A|announcement", 100d), 1001.4d, out _);
        Tests.Check(Math.Abs(continuousClock.Time(1001.45d) - 100.05d) < .000001d, "continuous clock removes false one-second audio seek");
        continuousClock.Receive(FikaAuthorityClock.Stamp("S|snapshot", 101d), 1003d, out _);
        Tests.Check(Math.Abs(continuousClock.Time(1003d) - 101.6d) < .000001d, "delayed snapshot does not move the clock backwards");
        continuousClock.Receive(FikaAuthorityClock.Stamp("S|snapshot", 102d), 1003.41d, out _);
        Tests.Check(Math.Abs(continuousClock.Time(1003.41d) - 102.01d) < .000001d, "later low-latency packet keeps the established offset");
        continuousClock.Reset();
        Tests.Check(!continuousClock.Ready, "new session discards previous peer offset");
        Tests.Check(!continuousClock.Receive("ISBCLK|NaN|A", 10d, out _), "invalid remote clock rejected");
        Tests.Check(!continuousClock.Receive("ISBCLK|Infinity|A", 10d, out _), "infinite remote clock rejected");
    }
}
