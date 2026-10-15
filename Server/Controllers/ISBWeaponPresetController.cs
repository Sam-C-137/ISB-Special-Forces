using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using IOPath = System.IO.Path;
using HarmonyLib;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ISBSpecialForcesServer.Controllers;
internal sealed class ISBWepPresetSpawn
{
    public string Id { get; init; } = string.Empty;
    public string WildType { get; init; } = string.Empty;
    public string WeaponTpl { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public double Weight { get; init; } = 100;
    public bool Enabled { get; init; } = true;
    public Dictionary<string, Dictionary<string, List<string>>> Build { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ISBWepGenState
{
    public object SyncRoot { get; init; } = null!;
    public Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> OriginalMods { get; init; } = null!;
    public BotTypeInventory Inventory { get; init; } = null!;
    public string PresetId { get; init; } = string.Empty;
    public bool Restored { get; set; }
}

[Injectable(InjectionType.Singleton)]
public class ISBWeaponPresetController(TemplateTable templateTable, ModHelper modHelper, ISBLogger logger, IReadOnlyList<SptMod> modList, ConfigController configController)
{
    private static readonly IReadOnlyDictionary<string, string[][]> CollabPresetFolders = new Dictionary<string, string[][]>(StringComparer.OrdinalIgnoreCase)
    {
        {
            "ArmoryLoadouts",
            new[]
            {
                new[]
                {
                    "com.wtt.armory"
                }
            }
        },
        {
            "AllinOneLoadouts",
            new[]
            {
                new[]
                {
                    "com.epicrangetime.aio"
                }
            }
        },
        {
            "ECOLoadouts",
            new[]
            {
                new[]
                {
                    "com.wtt.ecoattachmentemporium"
                }
            }
        },
        {
            "TrueNorth-Loadouts",
            new[]
            {
                new[]
                {
                    "com.c11.truenorth4"
                }
            }
        },
        {
            "MassivesoftWeaponsLoadouts",
            new[]
            {
                new[]
                {
                    "com.massivesoft.massivesoftweapons"
                }
            }
        },
        {
            "KS-1Loadouts",
            new[]
            {
                new[]
                {
                    "com.spy.jsoc-swr"
                }
            }
        },
        {
            "ArtemLoadouts",
            new[]
            {
                new[]
                {
                    "com.crackbone.artem-wtt"
                }
            }
        },
        {
            "TGCLoadouts",
            new[]
            {
                new[]
                {
                    "com.emilanderss0n.tgc"
                }
            }
        },
        {
            "ManimalGasLoadouts",
            new[]
            {
                new[]
                {
                    "com.manimal.csgas"
                }
            }
        },
        {
            "CAGLoadouts",
            new[]
            {
                new[]
                {
                    "com.wtt.cag"
                }
            }
        },
        {
            "WAACSoloLoadouts",
            new[]
            {
                new[]
                {
                    "com.wtt.waac"
                }
            }
        },
        {
            "WAACLoadouts",
            new[]
            {
                new[]
                {
                    "com.wtt.waac",
                    "com.20fpsguy.femalevoice"
                }
            }
        },
        {
            "WAACWomLoadouts",
            new[]
            {
                new[]
                {
                    "com.wtt.waac",
                    "com.20fpsguy.femalevoice",
                    "dlc.femaleclothes"
                }
            }
        }
    };
    private readonly object _generationLock = new();
    private readonly Dictionary<(string Role, string WeaponTpl), List<ISBWepPresetSpawn>> _presets = new();
    private readonly Random _random = new();
    private HashSet<string>? _installedModGuids;
    private bool IsCollabFolderActive(string folder, string[][] requirementGroups)
    {
        if (!requirementGroups.Any(group => group.All(InstalledModGuids.Contains)))
            return false;
        if (!folder.StartsWith("WAAC", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!InstalledModGuids.Contains("com.wtt.waac"))
            return false;
        string selectedFolder;
        if (InstalledModGuids.Contains("com.20fpsguy.femalevoice") && InstalledModGuids.Contains("dlc.femaleclothes"))
            selectedFolder = folder.EndsWith("Presets", StringComparison.OrdinalIgnoreCase) ? "WAACWomPresets" : "WAACWomLoadouts";
        else if (InstalledModGuids.Contains("com.20fpsguy.femalevoice"))
            selectedFolder = folder.EndsWith("Presets", StringComparison.OrdinalIgnoreCase) ? "WAACPresets" : "WAACLoadouts";
        else
            selectedFolder = folder.EndsWith("Presets", StringComparison.OrdinalIgnoreCase) ? "WAACSoloPresets" : "WAACSoloLoadouts";
        return string.Equals(folder, selectedFolder, StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string> InstalledModGuids => _installedModGuids ??= modList.Select(mod => mod.ModMetadata.ModGuid).Where(guid => !string.IsNullOrWhiteSpace(guid)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal void RunSynchronized(Action action)
    {
        if (action is null)
            return;
        lock (_generationLock)
        {
            action();
        }
    }

    public void ApplyDefinWeapPreset(bool isNight)
    {
        lock (_generationLock)
        {
            _presets.Clear();
            try
            {
                if (templateTable.Items is not IDictionary templates)
                {
                    logger.Warn("[ISB WEAPON PRESETS] Templates Items unavailable.");
                    return;
                }

                var modRoot = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
                var presetRoot = IOPath.Combine(modRoot, "db", "WeaponPresets");
                var appearanceRoot = IOPath.Combine(modRoot, "db", "appearance");
                var loaded = new List<ISBWepPresetSpawn>();
                var collabFolderNames = CollabPresetFolders.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(presetRoot))
                {
                    loaded.AddRange(LoadPresets(presetRoot, isNight, "Default", collabFolderNames));
                }

                if (configController.ModConfig?.enableCollabLoadoutsAndAppearances == true)
                {
                    foreach (var(folder, requirementGroups)in CollabPresetFolders)
                    {
                        if (!IsCollabFolderActive(folder, requirementGroups))
                            continue;
                        if (configController.ModConfig?.IsCollabLoadoutEnabled(folder) != true)
                            continue;
                        var collabAppearanceRoot = IOPath.Combine(appearanceRoot, folder);
                        var legacyCollabRoot = IOPath.Combine(presetRoot, folder);
                        var collabPresetRoot = HasPresetRoleDirectories(collabAppearanceRoot) ? collabAppearanceRoot : legacyCollabRoot;
                        if (!Directory.Exists(collabPresetRoot))
                            continue;
                        var collabPresets = LoadPresets(collabPresetRoot, isNight, folder);
                        loaded.AddRange(collabPresets);
                        logger.Info($"[ISB WEAPON PRESETS] Active collab '{folder}' added " + $"{collabPresets.Count} preset(s) from '{collabPresetRoot}'.");
                    }
                }

                foreach (var preset in loaded.Where(x => x.Enabled))
                {
                    if (!ValidatePreset(preset, templates, out var error))
                    {
                        logger.Warn($"[ISB WEAPON PRESETS] Rejected '{preset.Id}': {error}");
                        continue;
                    }

                    var key = (preset.WildType.ToLowerInvariant(), preset.WeaponTpl.ToLowerInvariant());
                    if (!_presets.TryGetValue(key, out var list))
                    {
                        list = new List<ISBWepPresetSpawn>();
                        _presets[key] = list;
                    }

                    list.Add(preset);
                }
            }
            catch (Exception ex)
            {
                _presets.Clear();
                logger.Error($"[ISB WEAPON PRESETS] Failed to load presets: {ex}");
            }
        }
    }

    internal bool TryEarlyPresetGen(string role, MongoId weaponTpl, BotTypeInventory botTemplateInventory, out ISBWepGenState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(role) || _presets.Count == 0 || botTemplateInventory.Mods is null)
            return false;
        var roleLower = role.Trim().ToLowerInvariant();
        var weaponTplLower = weaponTpl.ToString().ToLowerInvariant();
        if (!_presets.TryGetValue((roleLower, weaponTplLower), out var choices) || choices.Count == 0)
            return false;
        Monitor.Enter(_generationLock);
        try
        {
            var selected = SelectWeighted(choices);
            var original = botTemplateInventory.Mods;
            var closedMods = CloneMods(original);
            foreach (var parentTpl in selected.Build.Keys)
                closedMods.Remove(ToMongoId(parentTpl));
            foreach (var parent in selected.Build)
            {
                if (parent.Value.Count == 0)
                    continue;
                var slotMap = new Dictionary<string, HashSet<MongoId>>(StringComparer.OrdinalIgnoreCase);
                foreach (var slot in parent.Value)
                {
                    var child = slot.Value.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                    if (child is null)
                        continue;
                    slotMap[slot.Key] = new HashSet<MongoId>
                    {
                        ToMongoId(child)
                    };
                }

                if (slotMap.Count > 0)
                    closedMods[ToMongoId(parent.Key)] = slotMap;
            }

            botTemplateInventory.Mods = closedMods;
            state = new ISBWepGenState
            {
                SyncRoot = _generationLock,
                OriginalMods = original,
                Inventory = botTemplateInventory,
                PresetId = selected.Id
            };
            logger.Info($"[ISB WEAPON PRESETS] Applied '{selected.Id}' to '{roleLower}' ({weaponTplLower}).");
            return true;
        }
        catch
        {
            Monitor.Exit(_generationLock);
            throw;
        }
    }

    internal void EndGeneration(ISBWepGenState? state)
    {
        if (state is null || state.Restored)
            return;
        state.Restored = true;
        try
        {
            state.Inventory.Mods = state.OriginalMods;
        }
        finally
        {
            Monitor.Exit(state.SyncRoot);
        }
    }

    private ISBWepPresetSpawn SelectWeighted(List<ISBWepPresetSpawn> choices)
    {
        var total = choices.Sum(x => x.Weight);
        var roll = _random.NextDouble() * total;
        foreach (var choice in choices)
        {
            roll -= choice.Weight;
            if (roll <= 0)
                return choice;
        }

        return choices[^1];
    }

    private static Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> CloneMods(Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> source)
    {
        return source.ToDictionary(parent => parent.Key, parent => parent.Value.ToDictionary(slot => slot.Key, slot => new HashSet<MongoId>(slot.Value), StringComparer.OrdinalIgnoreCase));
    }

    private static bool HasPresetRoleDirectories(string root)
    {
        if (!Directory.Exists(root))
            return false;
        foreach (var roleDirectory in Directory.GetDirectories(root))
        {
            if (Directory.GetDirectories(roleDirectory).Length > 0)
                return true;
        }

        return false;
    }

    private List<ISBWepPresetSpawn> LoadPresets(string presetRoot, bool isNight, string sourceName, ISet<string>? excludedTopLevelFolders = null)
    {
        var result = new List<ISBWepPresetSpawn>();
        var suffix = isNight ? "_night" : "_day";
        var directories = Directory.GetDirectories(presetRoot).Select(path => (Path: path, Folder: IOPath.GetFileName(path))).Where(x => !string.IsNullOrWhiteSpace(x.Folder)).Where(x => excludedTopLevelFolders is null || !excludedTopLevelFolders.Contains(x.Folder)).ToList();
        var baseRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in directories)
        {
            if (entry.Folder.EndsWith("_day", StringComparison.OrdinalIgnoreCase))
                baseRoles.Add(entry.Folder[..^4]);
            else if (entry.Folder.EndsWith("_night", StringComparison.OrdinalIgnoreCase))
                baseRoles.Add(entry.Folder[..^6]);
            else
                baseRoles.Add(entry.Folder);
        }

        foreach (var role in baseRoles)
        {
            var selected = directories.FirstOrDefault(x => string.Equals(x.Folder, role + suffix, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(selected.Path))
                selected = directories.FirstOrDefault(x => string.Equals(x.Folder, role, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(selected.Path))
                continue;
            logger.Info($"[ISB WEAPON PRESETS] Using {sourceName} folder " + $"'{selected.Folder}' for '{role}'.");
            foreach (var weaponDir in Directory.GetDirectories(selected.Path))
            {
                var weaponTpl = IOPath.GetFileName(weaponDir);
                foreach (var file in Directory.GetFiles(weaponDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var preset = ParsePreset(file, role, weaponTpl, sourceName);
                        if (preset is not null)
                            result.Add(preset);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"[ISB WEAPON PRESETS] Could not read '{file}': {ex.Message}");
                    }
                }
            }
        }

        return result;
    }

    private static ISBWepPresetSpawn? ParsePreset(string path, string role, string weaponTpl, string sourceName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (!root.TryGetProperty("build", out var buildElement))
            return null;
        var build = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(buildElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (build is null)
            return null;
        var weight = root.TryGetProperty("weight", out var w) && w.TryGetDouble(out var value) ? value : 100;
        var enabled = !root.TryGetProperty("enabled", out var e) || e.ValueKind != JsonValueKind.False;
        return new ISBWepPresetSpawn
        {
            Id = $"{sourceName}_{role}_{weaponTpl}_{IOPath.GetFileNameWithoutExtension(path)}",
            WildType = role,
            WeaponTpl = weaponTpl,
            SourcePath = path,
            Weight = weight,
            Enabled = enabled,
            Build = new Dictionary<string, Dictionary<string, List<string>>>(build, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool ValidatePreset(ISBWepPresetSpawn preset, IDictionary templates, out string error)
    {
        if (preset.Weight <= 0 || double.IsNaN(preset.Weight) || double.IsInfinity(preset.Weight))
        {
            error = "weight must be greater than zero.";
            return false;
        }

        if (!preset.Build.ContainsKey(preset.WeaponTpl))
        {
            error = $"build does not contain root weapon '{preset.WeaponTpl}'.";
            return false;
        }

        var allTpls = new HashSet<string>(preset.Build.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var parent in preset.Build)
            foreach (var slot in parent.Value)
            {
                var children = slot.Value.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (children.Count != 1)
                {
                    error = $"'{parent.Key}/{slot.Key}' must contain exactly one child.";
                    return false;
                }

                allTpls.Add(children[0]);
            }

        foreach (var tpl in allTpls)
        {
            if (!templates.Contains(ToMongoId(tpl)))
            {
                error = $"template '{tpl}' was not found.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static MongoId ToMongoId(string value) => new(value);
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class WeaponPresetGenerationPatch(ISBWeaponPresetController controller) : IOnLoad
{
    private static ISBWeaponPresetController _controller = null!;
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _controller = controller;
        var target = AccessTools.Method(typeof(BotWeaponGenerator), "GenerateWeaponByTpl");
        if (target is null)
            return Task.CompletedTask;
        var harmony = new Harmony("com.samc137.isbspecialforces.isbweppresetgenv1");
        harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(WeaponPresetGenerationPatch), nameof(Prefix))), postfix: new HarmonyMethod(AccessTools.Method(typeof(WeaponPresetGenerationPatch), nameof(Postfix))), finalizer: new HarmonyMethod(AccessTools.Method(typeof(WeaponPresetGenerationPatch), nameof(Finalizer))));
        return Task.CompletedTask;
    }

    private static void Prefix(MongoId weaponTpl, BotTypeInventory botTemplateInventory, BotGenerationDetails botGenerationDetails, ref ISBWepGenState? __state)
    {
        var role = botGenerationDetails.RoleLowercase;
        if (string.IsNullOrWhiteSpace(role))
            return;
        _controller.TryEarlyPresetGen(role, weaponTpl, botTemplateInventory, out __state);
    }

    private static void Postfix(ISBWepGenState? __state)
    {
        _controller.EndGeneration(__state);
    }

    private static Exception? Finalizer(Exception? __exception, ISBWepGenState? __state)
    {
        if (__exception is not null)
            _controller.EndGeneration(__state);
        return __exception;
    }
}
