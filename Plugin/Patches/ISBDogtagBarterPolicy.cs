using System;

namespace ISBSpecialForces.Patches
{
    public static class ISBDogtagBarterPolicy
    {
        public static bool Matches(string required, string candidate, int minimumLevel, int requiredSide, int? level, int? side)
        {
            if (!string.Equals(required, candidate, StringComparison.Ordinal))
                return false;
            return requiredSide == 0 || requiredSide == 4 || side == requiredSide;
        }
    }
}
