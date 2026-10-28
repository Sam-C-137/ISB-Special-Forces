using ISBSpecialForcesServer.Controllers;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using System.Reflection;
using System.IO;
using System.Text.Json;

namespace ISBSpecialForcesServer;
[Injectable(InjectionType.Singleton)]
public class ConfigController
{
    public MainConfig ModConfig;
    public AnomalousItemSpawnsConfig AnomalousSpawns => ModConfig.labsContainmentBreachEvent.anomalousItemSpawns;

    public readonly ModHelper _modHelper;
    public ConfigController(ModHelper modHelper)
    {
        _modHelper = modHelper;
        Reload();
    }

    public void Reload()
    {
        var pathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        IsbBotHealth.EnsureConfiguration(pathToMod);
        ModConfig = _modHelper.GetJsonDataFromFile<MainConfig>(pathToMod, "config.jsonc");
        IsbBotHealth.Validate(ModConfig.botHealth);
        ISBDiagnostics.Enabled = ModConfig.diagnosticLogs;
        ModConfig.labsContainmentBreachEvent = ContainmentBreachConfigFile.Load(pathToMod, ModConfig.labsContainmentBreachEvent);
        AnomalousItemSpawns.Validate(AnomalousSpawns);
        ContainmentMapLoot.Validate(ModConfig.labsContainmentBreachEvent.mapLoot);
        ModConfig.MigrateLegacyIcebreakerLocation();
    }

    public string GetGhostBotNamesJson()
    {
        var folder = Path.Combine(_modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db", "BlackDivisionBotPresets");
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        foreach (var role in ISB.Compatibility.GhostBotVariant.Names)
        {
            var path = Path.Combine(folder, role);
            if (!Directory.Exists(path))
                continue;
            foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file), options);
                void Add(JsonElement entry)
                {
                    if (entry.TryGetProperty("botName", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                        names[ISB.Compatibility.GhostBotVariant.BaseRoleValue(role) + "|" + name.GetString()!.Trim()] = "ScavRole/" + role;
                }

                Add(json.RootElement);
                if (json.RootElement.TryGetProperty("equipmentBuild", out var build) && build.ValueKind == JsonValueKind.Object)
                    Add(build);
                if (json.RootElement.TryGetProperty("equipmentBuilds", out var builds) && builds.ValueKind == JsonValueKind.Array)
                    foreach (var entry in builds.EnumerateArray())
                        Add(entry);
            }
        }

        foreach (var entry in Patches.GhostBotGenerationPatch.GeneratedRoles)
            names["profile|" + entry.Key] = entry.Value;
        return JsonSerializer.Serialize(names);
    }

    private readonly object _chanceSync = new();
    public string GetScp173ConfigJson(string? session = null, ISBSpawnProgressController? progress = null)
    {
        var folder = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var path = Path.Combine(folder, "nightmare_config.jsonc");
        if (!File.Exists(path))
            path = Path.Combine(folder, "peanut_config.jsonc");
        if (!File.Exists(path))
            path = Path.Combine(folder, "scp173_config.jsonc");
        if (!File.Exists(path))
            throw new FileNotFoundException("ISB Nightmare server configuration is missing.", path);
        if (new FileInfo(path).Length > 65536)
            throw new InvalidDataException("ISB Nightmare server configuration exceeds 64 KiB.");
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        var config = JsonSerializer.Deserialize<ISBSSOFExtra.Scp173Config>(File.ReadAllText(path), options) ?? throw new InvalidDataException("ISB Nightmare server configuration is empty.");
        config.Normalize();
        if (progress != null)
            config.chancePercent = (float)progress.Scp173Chance(session, config.chancePercent, config.enabled, config.maps);
        return JsonSerializer.Serialize(config, options);
    }

    public float GetContainmentChance()
    {
        lock (_chanceSync)
        {
            var folder = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            return ContainmentBreachConfigFile.Load(folder, ModConfig.labsContainmentBreachEvent).chance;
        }
    }

    public float SetContainmentChance(float chance)
    {
        lock (_chanceSync)
        {
            var folder = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            ContainmentBreachConfigFile.SetChance(folder, chance);
            ModConfig.labsContainmentBreachEvent.chance = chance;
            return chance;
        }
    }

    public string GetSainDefaultsJson()
    {
        var pathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var folder = Path.Combine(pathToMod, "sain_defaults");
        var configPath = Path.Combine(folder, "config.jsonc");
        if (!Directory.Exists(folder) || !File.Exists(configPath))
        {
            var legacyPath = Path.Combine(pathToMod, "sain_defaults.jsonc");
            return File.Exists(legacyPath) ? File.ReadAllText(legacyPath) : "{}";
        }

        try
        {
            var documentOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            using var configDocument = JsonDocument.Parse(File.ReadAllText(configPath), documentOptions);
            var configRoot = configDocument.RootElement;
            object enabledValue = TryCloneProperty(configRoot, "enabled", out var enabled) ? enabled : true;
            object logsValue = TryCloneProperty(configRoot, "logs", out var logs) ? logs : false;
            object migrationVersionValue = TryCloneProperty(configRoot, "migrationVersion", out var migrationVersion) ? migrationVersion : 1;
            var result = new Dictionary<string, object?>
            {
                ["enabled"] = enabledValue,
                ["logs"] = logsValue,
                ["migrationVersion"] = migrationVersionValue
            };
            var wildtypes = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(folder, "*.jsonc", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "config.jsonc", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var roleDocument = JsonDocument.Parse(File.ReadAllText(file), documentOptions);
                wildtypes[Path.GetFileNameWithoutExtension(file)] = roleDocument.RootElement.Clone();
            }

            result["wildtypes"] = wildtypes;
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return "{}";
        }
    }

    private static bool TryCloneProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value.Clone();
                return true;
            }
        }

        value = default;
        return false;
    }
}
