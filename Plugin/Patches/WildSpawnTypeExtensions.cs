using EFT;
using System.Collections.Generic;

namespace ISBSpecialForces
{
    public static class WildSpawnTypeExtensions
    {
        public static List<int> ISBTypeEnums = new List<int>
        {
            13701,
            13702,
            13703,
            13704,
            13705,
            13706,
            13707,
            13708,
            13709,
            13710,
            13711,
            13712,
            13713,
            13714,
            13715,
            13716
        };
        public static bool IsISB(WildSpawnType type)
        {
            return ISBTypeEnums.Contains((int)type);
        }
    }
}
