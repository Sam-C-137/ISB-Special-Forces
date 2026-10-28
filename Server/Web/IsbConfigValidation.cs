using System.Text.Json;
using ISBSpecialForcesServer;

namespace ISBSpecialForces.Web;
public static class IsbConfigValidation
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 128
    };
    public static void Validate(IsbDocument document, string text)
    {
        if (document.Format == "json")
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            ValidateFormations(json.RootElement);
        }

        if (document.RelativePath == "config.jsonc")
        {
            var value = JsonSerializer.Deserialize<MainConfig>(text, Options);
            if (value?.locations == null || value.debug == null)
                throw new ArgumentException("The configuration must retain locations and debug.");
            ValidateItemId(value.aishiLoyaltyCondition?.requiredItemTpl);
            IsbBotHealth.Validate(value.botHealth);
            if (value.dogtags?.dogtagTplByWildType != null)
                foreach (var pair in value.dogtags.dogtagTplByWildType)
                {
                    ValidateRole(pair.Key);
                    ValidateItemId(pair.Value);
                }
        }
        else if (document.RelativePath == "nightmare_config.jsonc")
        {
            var options = new JsonSerializerOptions(Options)
            {
                IncludeFields = true
            };
            var value = JsonSerializer.Deserialize<ISBSSOFExtra.Scp173Config>(text, options) ?? throw new ArgumentException("Nightmare configuration is empty.");
            value.Normalize();
        }
        else if (document.RelativePath.StartsWith("containment_breach_config.", StringComparison.Ordinal))
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
            if (root.TryGetPropertyValue("isbReinforcements", out var reinforcements))
            {
                root.Remove("labsChaos");
                root["labsChaos"] = reinforcements?.DeepClone();
                root.Remove("isbReinforcements");
            }

            var value = root.Deserialize<LabsPreBlackoutEventConfig>(Options) ?? throw new ArgumentException("Event configuration is empty.");
            LabsEventModes.Validate(value);
            AnomalousItemSpawns.Validate(value.anomalousItemSpawns);
            ContainmentMapLoot.Validate(value.mapLoot);
            ContainmentMapLoot.Validate(value.containmentCaseSpawns);
            foreach (var id in new[]
            {
                value.containmentBreachLastQuest
            }

            )
                if (string.IsNullOrWhiteSpace(id) || id.Length != 24 || !id.All(Uri.IsHexDigit))
                    throw new ArgumentException("Enter a valid quest ID with 24 hexadecimal characters.");
        }
    }

    private static void ValidateFormations(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == "spawnChance" && (!property.Value.TryGetDouble(out var chance) || !double.IsFinite(chance) || chance < 0 || chance > 100))
                    throw new ArgumentException("Spawn chance must be between 0 and 100.");
                if ((property.Name is "formations" or "patrols" or "checkpoints" or "hunts") && property.Value.ValueKind == JsonValueKind.Array)
                {
                    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var formation in property.Value.EnumerateArray())
                    {
                        if (formation.ValueKind != JsonValueKind.Object)
                            throw new ArgumentException("Each formation must be an object.");
                        if (formation.TryGetProperty("id", out var id) && !ids.Add(id.GetString() ?? ""))
                            throw new ArgumentException("Each formation in a category needs a unique identifier.");
                        ValidateRole(formation.TryGetProperty("boss", out var boss) ? boss.GetString() : null);
                        if (!formation.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Array || zones.EnumerateArray().Any(z => z.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(z.GetString())))
                            throw new ArgumentException("Choose BotZones for each formation.");
                        if (formation.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True && (property.Name == "checkpoints" || formation.TryGetProperty("checkpointRadius", out _)) && zones.GetArrayLength() == 0)
                            throw new ArgumentException("An enabled checkpoint needs at least one BotZone.");
                        if (formation.TryGetProperty("checkpointRadius", out var radius))
                        {
                            if (!radius.TryGetDouble(out var size) || !double.IsFinite(size) || size <= 0)
                                throw new ArgumentException("Checkpoint radius must be greater than zero.");
                            foreach (var axis in new[]
                            {
                                "x",
                                "y",
                                "z"
                            }

                            )
                                if (!formation.TryGetProperty(axis, out var coordinate) || !coordinate.TryGetDouble(out var position) || !double.IsFinite(position))
                                    throw new ArgumentException("Enter finite checkpoint coordinates.");
                        }

                        if (formation.TryGetProperty("followers", out var followers) && followers.ValueKind == JsonValueKind.Array)
                            foreach (var follower in followers.EnumerateArray())
                            {
                                ValidateRole(follower.GetProperty("type").GetString());
                                if (!follower.GetProperty("min").TryGetInt32(out var min) || !follower.GetProperty("max").TryGetInt32(out var max) || min < 0 || max < min)
                                    throw new ArgumentException("Use whole follower counts. Maximum must be greater than or equal to minimum.");
                            }
                    }
                }

                ValidateFormations(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray())
                ValidateFormations(child);
    }

    private static void ValidateItemId(string? value)
    {
        if (!string.IsNullOrEmpty(value) && (value.Length != 24 || !value.All(Uri.IsHexDigit)))
            throw new ArgumentException("Enter an item ID with 24 hexadecimal characters, or leave it empty for the default.");
    }

    private static void ValidateRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || (value.StartsWith("ISB", StringComparison.OrdinalIgnoreCase) && !IsbBotCatalog.KnownIsb(value)))
            throw new ArgumentException("Choose a valid bot type from the operator roster.");
    }
}
