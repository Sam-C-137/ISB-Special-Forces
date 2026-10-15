using System.Reflection;
using System.Text.Json;

namespace ISBSpecialForces.Web;
public static class IsbBotCatalog
{
    private static readonly Lazy<Dictionary<int, string>> Registry = new(() =>
    {
        using var stream = typeof(IsbBotCatalog).Assembly.GetManifestResourceStream("ISBSpecialForces.ISBBotTypes.json") ?? throw new InvalidOperationException("ISB operator roster is unavailable.");
        return JsonSerializer.Deserialize<Dictionary<int, string>>(stream)!;
    });
    private static readonly (string Role, string Label)[] DisplayOrder = [("ISBBossFirefly", "Firefly (White Tusk)"), ("ISBFollowerVipper", "Vipper (White Tusk)"), ("ISBFollowerLoghan", "Loghan (White Tusk)"), ("ISBFollowerSpearthrower", "Spearthrower (White Tusk)"), ("ISBFollowerZofia", "Zofia (White Tusk)"), ("ISBFollowerEvernight", "Evernight (White Tusk)"), ("ISBFollowerNemesis", "Nemesis (White Tusk)"), ("ISBBossCommander", "Vengeful Evernight (The Chosen One)"), ("ISBFollowerCommander", "Vengeful Nemesis (The Dark Lord)"), ("ISBTeamLeader", "Team Leader"), ("ISBSecondLeader", "Specialist"), ("ISBScout", "Scout"), ("ISBAssassin", "Assassin"), ("ISBMarksman", "Marksman"), ("ISBHeavy", "Heavy"), ("ISBSpecialForces", "Operative")];
    private static readonly (string Role, string Label)[] BlackDivisionDisplayOrder = [("blackDivAssault", "Black Division · Operative"), ("blackDivLead", "Black Division · Lead (Unused)"), ("blackDivBreacher", "Black Division · Breacher (Unused)"), ("blackDivSupport", "Black Division · Support (Unused)"), ("bossWedge", "Black Division · Wedge"), ("blackDivIb", "Black Division · Icebreaker"), ("blackDivGhostBusterLead", "Black Division · Ghost Buster Lead"), ("blackDivGhostBuster01", "Black Division · Ghost Buster 01"), ("blackDivGhostBuster02", "Black Division · Ghost Buster 02"), ("blackDivGhostBuster03", "Black Division · Ghost Buster 03"), ("blackDivGhostBuster04", "Black Division · Ghost Buster 04"), ("bossWedgeGhostBuster", "Black Division · Wedge Ghost Buster")];
    public static object[] All => DisplayOrder.Where(p => KnownIsb(p.Role)).Concat(BlackDivisionDisplayOrder).Select(p => (object)new { value = p.Role, label = p.Label }).ToArray();

    public static string NormalizeRole(string value) => int.TryParse(value, out var number) && Registry.Value.TryGetValue(number, out var name) ? name : value;
    public static bool KnownIsb(string value) => Registry.Value.Values.Contains(value, StringComparer.OrdinalIgnoreCase);
}
