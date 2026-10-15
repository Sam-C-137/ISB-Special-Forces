using System.Runtime.CompilerServices;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
internal static class ContainmentHealth
{
    private static readonly ConditionalWeakTable<BotBase, object> Applied = new();
    private static readonly Lazy<HashSet<string>> Roles = new(() =>
    {
        using var stream = typeof(ContainmentHealth).Assembly.GetManifestResourceStream("ISBSpecialForces.ISBBotTypes.json") ?? throw new InvalidOperationException("ISB bot types are missing.");
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        return values.Keys.Concat(values.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
    });
    internal static bool Apply(BotBase bot, bool active, double multiplier, double blackDivisionMultiplier = 1.25)
    {
        if (!active || bot?.Health?.BodyParts == null || !(Roles.Value.Contains(bot.Info?.Settings?.Role ?? "") || ContainmentPocketLoot.IsBlackDivision(bot.Info?.Settings?.Role)))
            return false;
        if (ContainmentPocketLoot.IsBlackDivision(bot.Info?.Settings?.Role))
            multiplier = blackDivisionMultiplier;
        var role = ISBSpecialForces.Web.IsbBotCatalog.NormalizeRole(bot.Info?.Settings?.Role ?? "");
        if (role.Equals("ISBBossFirefly", StringComparison.OrdinalIgnoreCase) || role.Equals("ISBFollowerVipper", StringComparison.OrdinalIgnoreCase) || role.Equals("ISBFollowerLoghan", StringComparison.OrdinalIgnoreCase) || role.Equals("ISBFollowerSpearthrower", StringComparison.OrdinalIgnoreCase) || role.Equals("ISBFollowerZofia", StringComparison.OrdinalIgnoreCase) || role.Equals("ISBFollowerEvernight", StringComparison.OrdinalIgnoreCase) || role.Equals("ISBFollowerNemesis", StringComparison.OrdinalIgnoreCase))
            multiplier = 1.15;
        if (!double.IsFinite(multiplier) || multiplier <= 0 || multiplier > 5)
            return false;
        lock (bot)
        {
            if (Applied.TryGetValue(bot, out _))
                return false;
            var parts = bot.Health.BodyParts.Values.Select(p => p?.Health).Where(p => p != null).ToArray();
            if (parts.Length == 0 || parts.Any(p => !p!.Maximum.HasValue || !p.Current.HasValue || !double.IsFinite(p.Maximum.Value) || !double.IsFinite(p.Current.Value)))
                return false;
            Applied.Add(bot, new object ());
            foreach (var part in parts)
            {
                part!.Maximum = Math.Max(1d, part.Maximum!.Value * multiplier);
                part.Current = Math.Clamp(part.Current!.Value * multiplier, 0d, part.Maximum.Value);
            }

            return true;
        }
    }
}
