using System;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    internal static class ManagedSquadPopulation
    {
        internal static IEnumerable<(int Size, bool Normal, bool Reinforcement)> Allocate(int normal, int reinforcement, int squadSize)
        {
            int persistent = Math.Min(normal, reinforcement);
            foreach (int size in Groups(persistent, squadSize)) yield return (size, true, true);
            foreach (int size in Groups(normal - persistent, squadSize)) yield return (size, true, false);
            foreach (int size in Groups(reinforcement - persistent, squadSize)) yield return (size, false, true);
        }

        internal static IEnumerable<(int Size, bool Normal, bool Reinforcement)> AllocatePhaseSquads(int normal, int reinforcement, int squadSize)
        {
            var remaining = new List<int>(Groups(reinforcement, squadSize));
            foreach (int size in Groups(normal, squadSize))
            {
                int shared = remaining.IndexOf(size);
                if (shared >= 0) remaining.RemoveAt(shared);
                yield return (size, true, shared >= 0);
            }
            foreach (int size in remaining) yield return (size, false, true);
        }

        private static IEnumerable<int> Groups(int total, int size)
        {
            for (; total > 0; total -= size) yield return Math.Min(total, size);
        }

        internal static bool CanRefill(bool deployed, int global, int maximum, int faction, int target, int size) =>
            !deployed && size <= maximum - global && size <= target - faction;

        internal static bool CanRetire(int alive, int pending, int loading, bool activated, float sinceLastActivation) =>
            alive == 0 && pending == 0 && loading == 0 && (!activated || sinceLastActivation >= 1f);
    }
}
