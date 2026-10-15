using System.Text.Json;
using System.Text.Json.Nodes;

namespace ISBSpecialForcesServer;
public static class ContainmentBreachConfigFile
{
    public const string FileName = "containment_breach_config.jsonc";
    internal static readonly object WriteLock = new();
    public static void SetChance(string folder, float chance)
    {
        if (!float.IsFinite(chance) || chance < 0 || chance > 100)
            throw new ArgumentOutOfRangeException(nameof(chance), "Chance must be between 0 and 100.");
        lock (WriteLock)
        {
            var path = Path.Combine(folder, FileName);
            var bytes = File.ReadAllBytes(path);
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            var reader = new Utf8JsonReader(bytes.AsSpan(offset), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || !reader.ValueTextEquals("chance"))
                    continue;
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
                    throw new JsonException("Invalid event chance.");
                var start = offset + (int)reader.TokenStartIndex;
                var end = offset + (int)reader.BytesConsumed;
                var value = System.Text.Encoding.UTF8.GetBytes(chance.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var updated = new byte[start + value.Length + bytes.Length - end];
                bytes.AsSpan(0, start).CopyTo(updated);
                value.CopyTo(updated, start);
                bytes.AsSpan(end).CopyTo(updated.AsSpan(start + value.Length));
                using var parsed = JsonDocument.Parse(updated.AsMemory(offset), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var temporary = path + ".isb-chance-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, updated);
                    File.Move(temporary, path, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                return;
            }
    
            throw new JsonException("Root chance property is missing from " + FileName);
        }
    }

    public static LabsPreBlackoutEventConfig Load(string folder, LabsPreBlackoutEventConfig legacy)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            path = Path.Combine(folder, "containment_breach_config.json");
            if (!File.Exists(path))
                return legacy ?? new();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), new JsonNodeOptions { PropertyNameCaseInsensitive = true }, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? throw new JsonException("Expected a ContainmentBreach configuration object.");
            if (root.TryGetPropertyValue("isbReinforcements", out var reinforcements))
            {
                root.Remove("labsChaos");
                root["labsChaos"] = reinforcements?.DeepClone();
                root.Remove("isbReinforcements");
            }

            var result = root.Deserialize<LabsPreBlackoutEventConfig>(options) ?? throw new JsonException("Expected a ContainmentBreach configuration object.");
            LabsEventModes.Validate(result);
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("ISB: invalid " + path, exception);
        }
    }
}
