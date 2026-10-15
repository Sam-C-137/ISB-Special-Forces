using System;

namespace ISBSpecialForces.Components
{
    public static class ISBAishiRaidResultState
    {
        public static bool ShowInRaidResults { get; private set; }
        public static double StandingBefore { get; private set; }
        public static double StandingAfter { get; private set; }
        public static double StandingDelta { get; private set; }
        public static bool NativeStandingEventRecorded { get; private set; }
        public static bool PendingServerConfirmation { get; private set; }
        public static bool HasStandingLoss => ShowInRaidResults && StandingDelta < -0.000001d;
        public static bool NeedsLegacyInjection => HasStandingLoss && !NativeStandingEventRecorded;

        public static void Reset()
        {
            ShowInRaidResults = false;
            StandingBefore = 0d;
            StandingAfter = 0d;
            StandingDelta = 0d;
            NativeStandingEventRecorded = false;
            PendingServerConfirmation = false;
        }

        public static void ArmExpectedPenalty(bool showInRaidResults, double standingBefore, double configuredLoss)
        {
            var loss = Math.Max(0d, configuredLoss);
            var standingAfter = Math.Max(0d, standingBefore - loss);
            ShowInRaidResults = showInRaidResults;
            StandingBefore = standingBefore;
            StandingAfter = standingAfter;
            StandingDelta = standingAfter - standingBefore;
            NativeStandingEventRecorded = false;
            PendingServerConfirmation = true;
        }

        public static void Set(bool showInRaidResults, double standingBefore, double standingAfter, double standingDelta)
        {
            ShowInRaidResults = showInRaidResults;
            StandingBefore = standingBefore;
            StandingAfter = standingAfter;
            StandingDelta = standingDelta;
            PendingServerConfirmation = false;
        }

        public static void MarkNativeEventRecorded()
        {
            NativeStandingEventRecorded = true;
        }
    }
}
