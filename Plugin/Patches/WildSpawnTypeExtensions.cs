using EFT;
using System.Collections.Generic;

namespace ISBSpecialForces
{
    public static class WildSpawnTypeExtensions
    {
        public static List<int> ISBTypeEnums = new List<int> { 13701, 13709, 13710, 13707, 13708 };

        public static bool IsISB(WildSpawnType type)
        {
            return ISBTypeEnums.Contains((int)type);
        }
    }
}