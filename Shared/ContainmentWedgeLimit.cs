using System;

namespace ISB.Compatibility
{
    public static class ContainmentWedgeLimit
    {
        public static bool IsWedge(string role) => string.Equals(role, "bossWedge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "bossWedgeGhostBuster", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "848424", StringComparison.Ordinal);
    }
}
