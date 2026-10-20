using System;

namespace ISBSpecialForces.Components
{
    public static class ISBEventPopulationPolicy
    {
        public const int MaximumBots = 17;
        public const int MinimumIsb = 4;

        public static int Capacity(int loaded, int alive) => Math.Max(0, MaximumBots - Math.Max(Math.Max(0, loaded), Math.Max(0, alive)));

        public static int Capacity(int loaded, int alive, int isbAlive, bool isb, int reserve)
        {
            int available = Capacity(loaded, alive);
            int held = isb ? 0 : Math.Max(0, Math.Min(MinimumIsb, reserve) - Math.Max(0, isbAlive));
            return Math.Max(0, available - held);
        }

        public static int MissingIsb(int alive, int pending, int reserve) => Math.Max(0, Math.Min(MinimumIsb, reserve) - Math.Max(0, alive) - Math.Max(0, pending));
    }
}
