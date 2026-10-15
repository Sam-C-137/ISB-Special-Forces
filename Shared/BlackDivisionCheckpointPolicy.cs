using System;
using System.Globalization;

namespace ISB.Compatibility
{
    internal static class BlackDivisionCheckpointPolicy
    {
        internal const string PluginGuid = "com.blackdiv.tacticaltoaster";
        internal const string ContainmentTrigger = "isb_labs_containment_checkpoint";
        internal const string ChaosTrigger = "isb_labs_chaos_checkpoint";
        internal const string BlackoutTrigger = "isb_labs_blackout_checkpoint";
        internal const string PowerRestoredTrigger = "isb_labs_power_restored_checkpoint";
        internal static readonly int[] Roles =
        {
            848420,
            848421,
            848422,
            848423,
            848424,
            848426
        };
        internal static bool IsRole(int role) => Array.IndexOf(Roles, role) >= 0;
        internal static string FormationTrigger(string phaseTrigger, int formationIndex)
        {
            if (!IsPhaseTrigger(phaseTrigger) || formationIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(formationIndex));
            return phaseTrigger + "_bdcp_" + formationIndex.ToString(CultureInfo.InvariantCulture);
        }

        internal static bool IsPhaseTrigger(string trigger) => trigger == ContainmentTrigger || trigger == ChaosTrigger || trigger == BlackoutTrigger || trigger == PowerRestoredTrigger;
        internal static bool IsFormationTrigger(string phaseTrigger, string trigger)
        {
            if (!IsPhaseTrigger(phaseTrigger) || string.IsNullOrEmpty(trigger))
                return false;
            string prefix = phaseTrigger + "_bdcp_";
            return trigger.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(trigger.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= 0;
        }

        internal static bool UseBlackDivision(bool installed, bool phaseEnabled, bool factionEnabled, string selectionMode)
        {
            if (!installed || !phaseEnabled || !factionEnabled)
                return false;
            string mode = (selectionMode ?? string.Empty).Trim().ToLowerInvariant();
            return mode != "isb" && mode != "isbonly";
        }

        internal static bool ValidPoint(float x, float y, float z, float radius) => Finite(x) && Finite(y) && Finite(z) && Finite(radius) && radius > 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
