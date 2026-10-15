using System;
using System.Collections.Generic;
using System.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISB.MixedAmmo
{
    public static class MixedAmmoStackBuilder
    {
        public static List<Item> MagCustomStackVV(MongoId magazineId, int capacity, CaliberIntDefinition mix)
        {
            var plan = mix.mode.Equals("interleaved", StringComparison.OrdinalIgnoreCase) ? MagAltingAmmo(capacity, mix) : AmmoBlockin(capacity, mix);
            var result = new List<Item>(plan.Count);
            for (var i = 0; i < plan.Count; i++)
            {
                result.Add(new Item { Id = new MongoId(), Template = plan[i].Tpl, ParentId = magazineId, SlotId = "cartridges", Location = plan.Count > 1 ? (object)i : null, Upd = new Upd { StackObjectsCount = plan[i].Count } });
            }

            return result;
        }

        private static List<(string Tpl, int Count)> AmmoBlockin(int capacity, CaliberIntDefinition mix)
        {
            var totalRatio = mix.entries.Sum(e => Math.Max(1, e.ratio));
            var plan = new List<(string Tpl, int Count)>();
            var remaining = capacity;
            var ordered = mix.entries.OrderBy(e => e.position.Equals("lastShots", StringComparison.OrdinalIgnoreCase) ? 0 : e.position.Equals("firstShots", StringComparison.OrdinalIgnoreCase) ? 2 : 1).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                var count = i == ordered.Count - 1 ? remaining : (int)Math.Round(capacity * (Math.Max(1, entry.ratio) / (double)totalRatio));
                count = Math.Min(count, remaining);
                if (count <= 0)
                {
                    continue;
                }

                plan.Add((entry.tpl, count));
                remaining -= count;
            }

            if (remaining > 0 && plan.Count > 0)
            {
                var last = plan[^1];
                plan[^1] = (last.Tpl, last.Count + remaining);
            }

            return plan;
        }

        private static List<(string Tpl, int Count)> MagAltingAmmo(int capacity, CaliberIntDefinition mix)
        {
            var primary = mix.entries.OrderByDescending(e => e.ratio).First();
            var secondary = mix.entries.FirstOrDefault(e => e.tpl != primary.tpl) ?? primary;
            var maxStacks = Math.Max(2, mix.maxStacksPerMagazine);
            var primaryBlock = Math.Max(1, mix.interleaveBlockSize);
            var secondaryBlock = Math.Max(1, primaryBlock * secondary.ratio / Math.Max(1, primary.ratio));
            var plan = new List<(string Tpl, int Count)>();
            var remaining = capacity;
            var usePrimary = true;
            while (remaining > 0 && plan.Count < maxStacks - 1)
            {
                var tpl = usePrimary ? primary.tpl : secondary.tpl;
                var size = Math.Min(usePrimary ? primaryBlock : secondaryBlock, remaining);
                plan.Add((tpl, size));
                remaining -= size;
                usePrimary = !usePrimary;
            }

            if (remaining > 0)
            {
                plan.Add((primary.tpl, remaining));
            }

            return plan;
        }
    }
}
