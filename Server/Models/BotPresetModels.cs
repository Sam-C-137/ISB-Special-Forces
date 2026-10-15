using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
public class BotPresetConfig
{
    public bool enabled { get; set; } = true;
    public bool logs { get; set; } = false;
    public double useChance { get; set; } = 100d;
    public Dictionary<string, double> useChanceByWildType { get; set; } = new();
    public bool allowMixedMagsByDefault { get; set; } = false;
}

public sealed class ISBBotPresetAppearance
{
    public string? head { get; set; }
    public string? body { get; set; }
    public string? feet { get; set; }
    public string? hands { get; set; }
    public string? voice { get; set; }
}

public sealed class ISBBotPresetEntry
{
    public required string Name { get; init; }
    public string? BotName { get; init; }
    public required string Role { get; init; }
    public required string Period { get; init; }
    public required string Root { get; init; }
    public required List<Item> Items { get; init; }
    public ISBBotPresetAppearance? Appearance { get; init; }
    public bool AllowMixedMags { get; init; }
    public double Weight { get; init; } = 1d;
    public string SourceFile { get; init; } = string.Empty;
}
