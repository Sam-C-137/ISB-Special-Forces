using System.Text.Json;
using System.Runtime.CompilerServices;
using ISBSpecialForces.Web;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Path = System.IO.Path;

namespace ISBSpecialForcesServer;
public static class IsbBotHealth
{
    public static readonly string[] Parts = ["Head", "Chest", "Stomach", "LeftArm", "RightArm", "LeftLeg", "RightLeg"];
    private static readonly ConditionalWeakTable<BotBase, object> Applied = new();
    public static void Validate(Dictionary<string, Dictionary<string, double>>? values)
    {
        if (values == null) throw new ArgumentException("Bot Health must be an object.");
        foreach (var role in values)
        {
            if (!IsbBotCatalog.KnownIsb(role.Key) || role.Value == null || role.Value.Count != Parts.Length || Parts.Any(p => !role.Value.ContainsKey(p)))
                throw new ArgumentException("Bot Health requires every limb for a known ISB wildtype.");
            if (role.Value.Values.Any(hp => !double.IsFinite(hp) || hp < 1 || hp > 100000 || hp != Math.Truncate(hp)))
                throw new ArgumentException("Limb health must be a whole number between 1 and 100000.");
        }
    }
    public static void EnsureConfiguration(string root)
    {
        var path = Path.Combine(root, "config.jsonc");
        var source = File.ReadAllText(path);
        using var document = JsonDocument.Parse(source, IsbConfigFiles.JsonOptions);
        if (document.RootElement.TryGetProperty("botHealth", out _)) return;
        using var stream = typeof(IsbBotHealth).Assembly.GetManifestResourceStream("ISBSpecialForces.Web.Defaults.config.jsonc")!;
        using var defaults = JsonDocument.Parse(stream, IsbConfigFiles.JsonOptions);
        var text = IsbConfigFiles.PatchJson(source, [new IsbChange("/botHealth", defaults.RootElement.GetProperty("botHealth").Clone())]);
        var temporary = path + ".health.tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Copy(path, path + ".health.bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static bool Apply(BotBase bot, Dictionary<string, Dictionary<string, double>> values)
    {
        if (bot?.Health?.BodyParts == null || values == null) return false;
        var name = IsbBotCatalog.NormalizeRole(bot.Info?.Settings?.Role ?? "");
        var settings = values.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        if (settings == null) return false;
        lock (bot)
        {
            if (Applied.TryGetValue(bot, out _)) return false;
            foreach (var part in bot.Health.BodyParts)
                if (part.Value?.Health != null && settings.TryGetValue(part.Key.ToString(), out var health))
                { part.Value.Health.Maximum = health; part.Value.Health.Current = health; }
            Applied.Add(bot, new object());
        }
        return true;
    }
}
