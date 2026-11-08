using System.Text.Json;
using System.Text.Json.Nodes;
using ISB.Compatibility;

namespace ISBSpecialForcesServer;
public sealed class ExternalFactionsConfig
{
    public LabsPreBlackoutFactionConfig chaosInsurgency { get; set; } = new();
}

public sealed class ExternalMapFactionsConfig
{
    public ExternalMapFactionConfig chaosInsurgency { get; set; } = new();
}

public sealed class ExternalMapFactionConfig
{
    public bool enabled { get; set; } = false;
    public Dictionary<string, MapConfig> locations { get; set; } = new();
}

public static class ExternalFactionConfiguration
{
    public const string SpawnTriggerPrefix = "isb_external_chaos_";
    public static bool IsLabsMap(string? map)
    {
        var key = new string((map ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key.StartsWith("laboratory", StringComparison.Ordinal) || key is "lab" or "labs" or "thelab" or "blackoutlabs"
            or "containmentbreachlabs" or "containementbreachlabs" or "labsblackout" or "labscontainmentbreach" or "labscontainementbreach" or "5b0fc42d86f7744a585f9105";
    }

    public static bool IsChaosFormation(SpawnFormationConfig? formation) => formation != null && GhostBotVariant.IsChaos(formation.boss)
        && formation.followers?.All(follower => follower != null && GhostBotVariant.IsChaos(follower.type)) != false;

    public static JsonObject EmptyMapConfiguration(JsonObject? maps)
    {
        var locations = new JsonObject();
        foreach (var key in maps?.Select(pair => pair.Key).Where(key => !IsLabsMap(key)) ?? Enumerable.Empty<string>())
            locations[key] = new JsonObject
            {
                ["patrol"] = new JsonObject { ["PatrolDrawLimit"] = 1, ["spawnChance"] = 0, ["EnableISBPatrols"] = false, ["formations"] = new JsonArray() },
                ["checkpoint"] = new JsonObject { ["CheckpointDrawLimit"] = 1, ["spawnChance"] = 0, ["enableISBHoldZones"] = false, ["formations"] = new JsonArray() },
                ["hunt"] = new JsonObject { ["HuntDrawLimit"] = 1, ["spawnChance"] = 0, ["enableHunt"] = false, ["formations"] = new JsonArray() }
            };
        return new JsonObject { ["chaosInsurgency"] = new JsonObject { ["enabled"] = false, ["locations"] = locations } };
    }

    public static bool Normalize(JsonObject root, bool containment)
    {
        bool changed = false;
        void Phase(JsonObject phase)
        {
            var external = phase["externalFactions"] as JsonObject;
            if (external == null)
            {
                external = new JsonObject();
                phase["externalFactions"] = external;
                changed = true;
            }
            var chaos = external["chaosInsurgency"] as JsonObject;
            if (chaos == null)
            {
                chaos = new JsonObject { ["enabled"] = true, ["patrols"] = new JsonArray(), ["checkpoints"] = new JsonArray(), ["hunts"] = new JsonArray() };
                external["chaosInsurgency"] = chaos;
                changed = true;
            }
            foreach (var faction in new[] { "isb", "blackDivision", "pmcs", "raiders" })
            {
                if (phase[faction] is not JsonObject source) continue;
                foreach (var kind in new[] { "patrols", "checkpoints", "hunts" })
                {
                    if (source[kind] is not JsonArray rows) continue;
                    foreach (var row in rows.OfType<JsonObject>().ToArray())
                    {
                        if (!GhostBotVariant.IsChaos(row["boss"]?.GetValue<string>() ?? "") ||
                            row["followers"] is JsonArray followers && followers.OfType<JsonObject>().Any(f => !GhostBotVariant.IsChaos(f["type"]?.GetValue<string>() ?? ""))) continue;
                        var target = chaos[kind] as JsonArray ?? throw new InvalidDataException("ISB: invalid external faction formation list: " + kind);
                        var id = row["id"]?.GetValue<string>();
                        var copy = row.DeepClone().AsObject();
                        if (source["enabled"]?.GetValue<bool>() == false) copy["enabled"] = false;
                        var existing = target.OfType<JsonObject>().FirstOrDefault(f => !string.IsNullOrEmpty(id) && string.Equals(f["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase));
                        if (existing != null && !JsonNode.DeepEquals(existing, copy))
                            throw new InvalidDataException("ISB: conflicting external faction formation: " + id);
                        if (existing == null) target.Add(copy);
                        rows.Remove(row);
                        changed = true;
                    }
                }
            }
        }
        if (containment)
        {
            Phase(root);
            foreach (var name in new[] { "labsChaos", "isbReinforcements" })
                if (root[name] is JsonObject phase) Phase(phase);
        }
        else if (root["labsBlackoutEvent"] is JsonObject blackout)
            foreach (var name in new[] { "blackoutActivated", "powerRestored" })
                if (blackout[name] is JsonObject phase) Phase(phase);
        return changed;
    }

    public static void MigrateFile(string path, bool containment)
    {
        lock (ContainmentBreachConfigFile.WriteLock)
        {
            if (!File.Exists(path)) return;
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new InvalidDataException("ISB: expected a configuration object: " + path);
            if (!Normalize(root, containment)) return;
            var temporary = path + ".external-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                var backup = path + ".pre-ci-wildtype.bak";
                if (!File.Exists(backup)) File.Copy(path, backup);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
