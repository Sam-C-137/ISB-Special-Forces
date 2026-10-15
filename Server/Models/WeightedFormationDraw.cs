namespace ISBSpecialForcesServer;
public static class WeightedFormationDraw
{
    public static List<SpawnFormationConfig> Select(IEnumerable<SpawnFormationConfig> source, int limit, Func<double> random)
    {
        var pool = source.Where(f => f?.enabled == true).ToList();
        if (limit < 0)
            return pool;
        pool.RemoveAll(f => !double.IsFinite(f.weight) || f.weight <= 0);
        var selected = new List<SpawnFormationConfig>();
        for (int i = 0, count = Math.Min(limit, pool.Count); i < count; i++)
        {
            double scale = pool.Max(f => f.weight);
            double total = pool.Sum(f => f.weight / scale);
            double value = Math.Clamp(random(), 0, Math.BitDecrement(1d)) * total;
            int index = pool.Count - 1;
            for (int j = 0; j < pool.Count; j++)
            {
                value -= pool[j].weight / scale;
                if (value < 0)
                {
                    index = j;
                    break;
                }
            }

            selected.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return selected;
    }
}
