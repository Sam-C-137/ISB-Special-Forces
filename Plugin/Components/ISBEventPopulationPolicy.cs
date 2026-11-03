using System;

namespace ISBSpecialForces.Components
{
    public static class ISBEventPopulationPolicy
    {
        public const int MaximumBots = 26;
        public const int MinimumIsb = 6;
        public static int Capacity(int loaded, int alive) => Math.Max(0, MaximumBots - Math.Max(Math.Max(0, loaded), Math.Max(0, alive)));
        public static int MissingIsb(int alive, int pending, int reserve) => Math.Max(0, Math.Min(MinimumIsb, reserve) - Math.Max(0, alive) - Math.Max(0, pending));
    }
}
