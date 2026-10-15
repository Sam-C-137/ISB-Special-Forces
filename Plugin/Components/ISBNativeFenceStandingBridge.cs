using Comfort.Common;
using EFT;
using EFT.Counters;
using System;

namespace ISBSpecialForces.Components
{
    internal static class ISBNativeFenceStandingBridge
    {
        private static bool _initialized;
        private static EFenceStandingSource _source;
        public static void Initialize(bool forceRetry = false)
        {
            if (!_initialized)
                _initialized = Enum.TryParse("ISBKill", out _source);
        }

        public static bool TryRecord(double delta)
        {
            return TryRecord(Singleton<GameWorld>.Instance?.MainPlayer?.Profile, delta);
        }

        internal static bool TryRecord(Profile profile, double delta)
        {
            if (ISBAishiRaidResultState.NativeStandingEventRecorded)
                return true;
            Initialize();
            var counters = profile?.EftStats?.SessionCounters;
            if (!_initialized || counters == null || double.IsNaN(delta) || double.IsInfinity(delta) || delta >= 0d)
                return false;
            counters.AddDouble(delta, CounterTag.FenceStanding, _source);
            ISBAishiRaidResultState.MarkNativeEventRecorded();
            return true;
        }
    }
}
