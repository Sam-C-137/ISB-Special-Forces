namespace ISBSpecialForcesServer;
public static class FormationSpawnChance
{
    public static float Resolve(float? configured, IEnumerable<SpawnFormationConfig>? formations, float fallback = 0)
    {
        if (configured.HasValue)
            return Math.Clamp(float.IsFinite(configured.Value) ? configured.Value : 0, 0, 100);
        var pool = formations?.Where(f => f?.enabled == true && double.IsFinite(f.weight) && f.weight > 0).ToArray() ?? [];
        if (pool.Length == 0)
            return Math.Clamp(float.IsFinite(fallback) ? fallback : 0, 0, 100);
        var scale = pool.Max(f => f.weight);
        var total = pool.Sum(f => f.weight / scale);
        return (float)(pool.Sum(f => f.weight / scale * Math.Clamp(float.IsFinite(f.chance) ? f.chance : 0, 0, 100)) / total);
    }
}
