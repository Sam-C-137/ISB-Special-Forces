using System;
using System.Collections.Generic;

namespace ISB.Compatibility
{
    internal static class GhostBotVariant
    {
        internal const string Name = "blackDivAssaultGhostBuster";
        internal const string LeadName = "blackDivGhostBusterLead";
        internal const string BaseName = "blackDivAssault";
        internal const int BaseValue = 848421;
        internal const string WedgeName = "bossWedgeGhostBuster";
        internal const string WedgeBaseName = "bossWedge";
        internal const int WedgeBaseValue = 848424;
        internal const string SpawnMarker = "isbGhostBuster";
        internal const double HealthMultiplier = 1.75d;
        internal static readonly string[] Names =
        {
            LeadName,
            "blackDivGhostBuster01",
            "blackDivGhostBuster02",
            "blackDivGhostBuster03",
            "blackDivGhostBuster04",
            WedgeName
        };
        private static readonly Dictionary<string, string> Aliases = BuildAliases();
        private static Dictionary<string, string> BuildAliases()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Names)
                aliases.Add(name, name);
            aliases.Add(Name, LeadName);
            return aliases;
        }

        internal static bool Is(string role) => CanonicalName(role) != null;
        internal static string CanonicalName(string role) => role != null && Aliases.TryGetValue(role, out var name) ? name : null;
        internal static string BaseRole(string role) => string.Equals(role, WedgeName, StringComparison.OrdinalIgnoreCase) ? WedgeBaseName : BaseName;
        internal static string AliasForBase(string role) => string.Equals(role, BaseName, StringComparison.OrdinalIgnoreCase) ? LeadName : string.Equals(role, WedgeBaseName, StringComparison.OrdinalIgnoreCase) ? WedgeName : null;
        internal static int BaseRoleValue(string role) => string.Equals(role, WedgeName, StringComparison.OrdinalIgnoreCase) ? WedgeBaseValue : BaseValue;
        internal static string Marker(string role) => SpawnMarker + ":" + CanonicalName(role);
        internal static string FromMarker(string marker, string role) => marker == SpawnMarker ? AliasForBase(role) : marker != null && marker.StartsWith(SpawnMarker + ":", StringComparison.Ordinal) ? CanonicalName(marker.Substring(SpawnMarker.Length + 1)) : null;
    }
}
