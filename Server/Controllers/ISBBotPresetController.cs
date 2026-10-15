using ISB.Compatibility;
using ISBSpecialForcesServer.Patches;
using SPTarkov.Server.Core.Helpers;
using ISB.MixedAmmo;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Reflection;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace ISBSpecialForcesServer.Controllers;
[Injectable(InjectionType.Singleton)]
public class ISBBotPresetController(ConfigController configController, ModHelper modHelper, JsonUtil jsonUtil, ICloner cloner, ItemHelper itemHelper, ISBSpawnController spawnController, IReadOnlyList<SptMod> modList)
{
    private static readonly Dictionary<string, string> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["13701"] = "ISBSpecialForces",
        ["13702"] = "ISBBossFirefly",
        ["13703"] = "ISBFollowerVipper",
        ["13704"] = "ISBFollowerLoghan",
        ["13705"] = "ISBFollowerSpearthrower",
        ["13706"] = "ISBFollowerZofia",
        ["13707"] = "ISBBossCommander",
        ["13708"] = "ISBFollowerCommander",
        ["13709"] = "ISBTeamLeader",
        ["13710"] = "ISBSecondLeader",
        ["13711"] = "ISBScout",
        ["13712"] = "ISBHeavy",
        ["13713"] = "ISBAssassin",
        ["13714"] = "ISBMarksman",
        ["13715"] = "ISBFollowerEvernight",
        ["13716"] = "ISBFollowerNemesis",
        ["ISBSpecialForces"] = "ISBSpecialForces",
        ["ISBBossFirefly"] = "ISBBossFirefly",
        ["ISBFollowerVipper"] = "ISBFollowerVipper",
        ["ISBFollowerLoghan"] = "ISBFollowerLoghan",
        ["ISBFollowerSpearthrower"] = "ISBFollowerSpearthrower",
        ["ISBFollowerZofia"] = "ISBFollowerZofia",
        ["ISBBossCommander"] = "ISBBossCommander",
        ["ISBFollowerCommander"] = "ISBFollowerCommander",
        ["ISBTeamLeader"] = "ISBTeamLeader",
        ["ISBSecondLeader"] = "ISBSecondLeader",
        ["ISBScout"] = "ISBScout",
        ["ISBHeavy"] = "ISBHeavy",
        ["ISBAssassin"] = "ISBAssassin",
        ["ISBMarksman"] = "ISBMarksman",
        ["ISBFollowerEvernight"] = "ISBFollowerEvernight",
        ["ISBFollowerNemesis"] = "ISBFollowerNemesis"
    };
    private static readonly IReadOnlyDictionary<string, string[][]> CollabPresetFolders = new Dictionary<string, string[][]>(StringComparer.OrdinalIgnoreCase)
    {
        {
            "ArmoryPresets",
            new[]
            {
                new[]
                {
                    "com.wtt.armory"
                }
            }
        },
        {
            "AllinOnePresets",
            new[]
            {
                new[]
                {
                    "com.epicrangetime.aio"
                }
            }
        },
        {
            "ECOPresets",
            new[]
            {
                new[]
                {
                    "com.wtt.ecoattachmentemporium"
                }
            }
        },
        {
            "TrueNorth-Presets",
            new[]
            {
                new[]
                {
                    "com.c11.truenorth4"
                }
            }
        },
        {
            "MassivesoftWeaponsPresets",
            new[]
            {
                new[]
                {
                    "com.massivesoft.massivesoftweapons"
                }
            }
        },
        {
            "KS-1Presets",
            new[]
            {
                new[]
                {
                    "com.spy.jsoc-swr"
                }
            }
        },
        {
            "ArtemPresets",
            new[]
            {
                new[]
                {
                    "com.crackbone.artem-wtt"
                }
            }
        },
        {
            "TGCPresets",
            new[]
            {
                new[]
                {
                    "com.emilanderss0n.tgc"
                }
            }
        },
        {
            "ManimalGasPresets",
            new[]
            {
                new[]
                {
                    "com.manimal.csgas"
                }
            }
        },
        {
            "CAGPresets",
            new[]
            {
                new[]
                {
                    "com.wtt.cag"
                }
            }
        },
        {
            "WAACSoloPresets",
            new[]
            {
                new[]
                {
                    "com.wtt.waac"
                }
            }
        },
        {
            "WAACPresets",
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
            "WAACWomPresets",
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
    private readonly object _loadSync = new();
    private Dictionary<string, IReadOnlyList<ISBBotPresetEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, double> _useChanceByRole = new(StringComparer.OrdinalIgnoreCase);
    private double _defaultUseChance = 100d;
    private bool _loaded;
    private bool BlackDivisionPresetsEnabled => BlackDivisionPresetPolicy.Enabled(InstalledModGuids.Contains(BlackDivisionPresetPolicy.ModGuid), configController.ModConfig?.blackDivisionBotPresets);

    private BotPresetConfig? PresetConfigFor(string role) => GhostBotVariant.Is(role) || BlackDivisionPresetPolicy.IsRole(role) ? configController.ModConfig?.blackDivisionBotPresets : configController.ModConfig?.botPresets;
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
    public int LoadedPresetCount => _cache.Values.Sum(x => x.Count);

    public void LoadAllPresets()
    {
        lock (_loadSync)
        {
            if (_loaded)
                return;
            _loaded = true;
            GhostBotGenerationPatch.PresetsLoaded = true;
            if (configController.ModConfig?.botPresets?.enabled != true && !InstalledModGuids.Contains(BlackDivisionPresetPolicy.ModGuid))
            {
                return;
            }

            CacheUseChanceConfig();
            var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var root = IOPath.Combine(modPath, "db", "BotPresets");
            var temp = new Dictionary<string, List<ISBBotPresetEntry>>(StringComparer.OrdinalIgnoreCase);
            var filesRead = 0;
            if (configController.ModConfig?.botPresets?.enabled == true)
                LoadPresetRoot(root, temp, ref filesRead, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Collabs" });
            if (configController.ModConfig?.botPresets?.enabled == true && configController.ModConfig?.enableCollabPresets == true)
            {
                var collabsRoot = IOPath.Combine(root, "Collabs");
                foreach (var(folder, requirementGroups)in CollabPresetFolders)
                {
                    if (!IsCollabFolderActive(folder, requirementGroups))
                        continue;
                    if (configController.ModConfig?.IsCollabPresetEnabled(folder) != true)
                        continue;
                    var collabPresetRoot = IOPath.Combine(collabsRoot, folder);
                    if (!Directory.Exists(collabPresetRoot))
                        continue;
                    LoadPresetRoot(collabPresetRoot, temp, ref filesRead);
                }
            }

            if (InstalledModGuids.Contains(BlackDivisionPresetPolicy.ModGuid))
                LoadPresetRoot(IOPath.Combine(modPath, "db", "BlackDivisionBotPresets"), temp, ref filesRead, blackDivision: true);
            foreach (var role in GhostBotVariant.Names)
                GhostBotGenerationPatch.SetReady(role, temp.TryGetValue(MakeKey(role, "Day"), out var day) && day.Count > 0, temp.TryGetValue(MakeKey(role, "Night"), out var night) && night.Count > 0);
            _cache = temp.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ISBBotPresetEntry>)pair.Value.AsReadOnly(), StringComparer.OrdinalIgnoreCase);
        }
    }

    private int LoadPresetRoot(string root, Dictionary<string, List<ISBBotPresetEntry>> temp, ref int filesRead, ISet<string>? excludedRoleFolders = null, bool blackDivision = false)
    {
        if (!Directory.Exists(root))
            return 0;
        var added = 0;
        foreach (var roleDirectory in Directory.EnumerateDirectories(root))
        {
            var folderRole = IOPath.GetFileName(roleDirectory);
            if (folderRole.Equals(GhostBotVariant.Name, StringComparison.OrdinalIgnoreCase) || excludedRoleFolders?.Contains(folderRole) == true)
                continue;
            if (!TryNormalizeRole(folderRole, out var role))
            {
                continue;
            }

            if ((BlackDivisionPresetPolicy.IsRole(role) || GhostBotVariant.Is(role)) != blackDivision)
                continue;
            if (blackDivision && !GhostBotVariant.Is(role) && !BlackDivisionPresetsEnabled)
                continue;
            foreach (var period in new[]
            {
                "Day",
                "Night"
            }

            )
            {
                var periodDirectory = IOPath.Combine(roleDirectory, period);
                if (!Directory.Exists(periodDirectory))
                    continue;
                foreach (var file in Directory.EnumerateFiles(periodDirectory, "*", SearchOption.TopDirectoryOnly).Where(IsJsonFile).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    filesRead++;
                    try
                    {
                        var entries = ParseFile(file, role, period);
                        foreach (var entry in entries)
                        {
                            var key = MakeKey(role, period);
                            if (!temp.TryGetValue(key, out var list))
                            {
                                list = [];
                                temp[key] = list;
                            }

                            list.Add(entry);
                            added++;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        return added;
    }

    public bool TryApplyPreset(BotBase? bot, string? requestedRole = null)
    {
        if (bot is null)
            return false;
        if (!_loaded)
            LoadAllPresets();
        var rawRole = requestedRole ?? bot.Info?.Settings?.Role;
        if (!TryNormalizeRole(rawRole, out var role))
            return false;
        bool ghost = GhostBotVariant.Is(role);
        bool blackDivision = ghost || BlackDivisionPresetPolicy.IsRole(role);
        if (ghost ? !InstalledModGuids.Contains(BlackDivisionPresetPolicy.ModGuid) : blackDivision ? !BlackDivisionPresetsEnabled : configController.ModConfig?.botPresets?.enabled != true)
            return false;
        if (bot.Inventory is null)
            return false;
        var needsEquipment = bot.Inventory.Items?.Any(item => item.ParentId == bot.Inventory.Equipment.ToString() && item.SlotId is "FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster") != true;
        var period = spawnController.CurrentRaidIsNight() ? "Night" : "Day";
        if (!_cache.TryGetValue(MakeKey(role, period), out var presets) || presets.Count == 0)
        {
            if (blackDivision || !needsEquipment || !_cache.TryGetValue(MakeKey("ISBScout", period), out presets) || presets.Count == 0)
                return false;
        }

        var useChance = ghost || (needsEquipment && !blackDivision) ? 100d : ResolveUseChance(role);
        if (useChance <= 0d || (useChance < 100d && Random.Shared.NextDouble() * 100d >= useChance))
        {
            return false;
        }

        var preset = SelectWeighted(presets);
        if (preset is null)
            return false;
        if (bot.Inventory is null)
            return false;
        var clonedItems = cloner.Clone(preset.Items);
        if (clonedItems is null || clonedItems.Count == 0)
            return false;
        OptionalPresetLoot.RemoveUnavailable(clonedItems, id => itemHelper.GetItem(id).Key);
        RemoveDogtagTree(clonedItems);
        var idMap = new Dictionary<string, MongoId>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in clonedItems)
        {
            var oldId = item.Id.ToString();
            if (!idMap.TryAdd(oldId, new MongoId()))
                throw new InvalidOperationException($"Preset '{preset.Name}' contains duplicate item id '{oldId}'.");
        }

        if (!idMap.TryGetValue(preset.Root, out var newRoot))
            throw new InvalidOperationException($"Preset '{preset.Name}' root '{preset.Root}' does not exist in its item list.");
        foreach (var item in clonedItems)
        {
            var oldId = item.Id.ToString();
            item.Id = idMap[oldId];
            if (!string.IsNullOrWhiteSpace(item.ParentId) && idMap.TryGetValue(item.ParentId, out var newParent))
                item.ParentId = newParent.ToString();
        }

        bot.Inventory.Items = clonedItems;
        bot.Inventory.Equipment = newRoot;
        bot.Inventory.FastPanel = null;
        bot.Inventory.FavoriteItems = null;
        if (preset.AllowMixedMags)
            ApplyMixedMags(bot, role, clonedItems, newRoot);
        ApplyAppearance(bot, preset.Appearance);
        if (bot.Info != null && !string.IsNullOrWhiteSpace(preset.BotName))
            bot.Info.Nickname = preset.BotName;
        return true;
    }

    private void CacheUseChanceConfig()
    {
        _defaultUseChance = ClampChance(configController.ModConfig?.botPresets?.useChance ?? 100d);
        var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (bool blackDivision in new[]
        {
            false,
            true
        }

        )
        {
            var cfg = blackDivision ? configController.ModConfig?.blackDivisionBotPresets : configController.ModConfig?.botPresets;
            if (cfg?.useChanceByWildType == null)
                continue;
            foreach (var pair in cfg.useChanceByWildType)
                if (TryNormalizeRole(pair.Key, out var role) && BlackDivisionPresetPolicy.IsRole(role) == blackDivision)
                    normalized[role] = ClampChance(pair.Value);
        }

        _useChanceByRole = normalized;
    }

    private double ResolveUseChance(string role)
    {
        return _useChanceByRole.TryGetValue(role, out var roleChance) ? roleChance : BlackDivisionPresetPolicy.IsRole(role) ? ClampChance(PresetConfigFor(role)?.useChance ?? 100d) : _defaultUseChance;
    }

    private static double ClampChance(double chance)
    {
        if (double.IsNaN(chance) || double.IsNegativeInfinity(chance))
            return 0d;
        if (double.IsPositiveInfinity(chance))
            return 100d;
        return Math.Max(0d, Math.Min(100d, chance));
    }

    private IEnumerable<ISBBotPresetEntry> ParseFile(string file, string role, string period)
    {
        var text = File.ReadAllText(file);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            yield break;
        if (TryGetProperty(root, "equipmentBuilds", out var builds) && builds.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var build in builds.EnumerateArray())
            {
                index++;
                var entry = BuildEntry(build, build, role, period, file, $"{IOPath.GetFileNameWithoutExtension(file)} #{index}");
                if (entry is not null)
                    yield return entry;
            }

            yield break;
        }

        if (TryGetProperty(root, "equipmentBuild", out var equipmentBuild) && equipmentBuild.ValueKind == JsonValueKind.Object)
        {
            var entry = BuildEntry(equipmentBuild, root, role, period, file, IOPath.GetFileNameWithoutExtension(file));
            if (entry is not null)
                yield return entry;
            yield break;
        }

        var directEntry = BuildEntry(root, root, role, period, file, IOPath.GetFileNameWithoutExtension(file));
        if (directEntry is not null)
            yield return directEntry;
    }

    private ISBBotPresetEntry? BuildEntry(JsonElement buildElement, JsonElement metadataElement, string role, string period, string sourceFile, string fallbackName)
    {
        if (TryGetBool(metadataElement, "enabled", out var enabled) && !enabled)
            return null;
        var weight = TryGetDouble(metadataElement, "weight", out var configuredWeight) ? configuredWeight : TryGetDouble(buildElement, "weight", out var buildWeight) ? buildWeight : 1d;
        if (weight <= 0d || double.IsNaN(weight) || double.IsInfinity(weight))
            return null;
        if (!TryGetProperty(buildElement, "Items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            return null;
        if (!TryGetString(buildElement, "Root", out var rootId) || string.IsNullOrWhiteSpace(rootId))
            return null;
        List<Item>? items;
        try
        {
            items = jsonUtil.Deserialize<List<Item>>(itemsElement.GetRawText());
        }
        catch (Exception)
        {
            return null;
        }

        if (items is null || items.Count == 0)
            return null;
        if (!ValidateTree(items, rootId, sourceFile, out var validationError))
        {
            return null;
        }

        var name = TryGetString(metadataElement, "name", out var metadataName) && !string.IsNullOrWhiteSpace(metadataName) ? metadataName : TryGetString(buildElement, "Name", out var buildName) && !string.IsNullOrWhiteSpace(buildName) ? buildName : fallbackName;
        var appearance = ParseAppearance(metadataElement) ?? ParseAppearance(buildElement);
        var allowMixedMags = TryGetBool(metadataElement, "allowMixedMags", out var metadataMixedMags) ? metadataMixedMags : TryGetBool(buildElement, "allowMixedMags", out var buildMixedMags) ? buildMixedMags : PresetConfigFor(role)?.allowMixedMagsByDefault == true;
        return new ISBBotPresetEntry
        {
            Name = name,
            BotName = TryGetString(metadataElement, "botName", out var botName) && !string.IsNullOrWhiteSpace(botName) ? botName.Trim() : TryGetString(buildElement, "botName", out botName) && !string.IsNullOrWhiteSpace(botName) ? botName.Trim() : null,
            Role = role,
            Period = period,
            Root = rootId,
            Items = items,
            Appearance = appearance,
            AllowMixedMags = allowMixedMags,
            Weight = weight,
            SourceFile = sourceFile
        };
    }

    private static bool ValidateTree(List<Item> items, string rootId, string sourceFile, out string error)
    {
        error = string.Empty;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var id = item.Id.ToString();
            if (!ids.Add(id))
            {
                error = $"duplicate item id '{id}'";
                return false;
            }
        }

        if (!ids.Contains(rootId))
        {
            error = $"Root '{rootId}' is not present in Items";
            return false;
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ParentId))
                continue;
            if (!ids.Contains(item.ParentId))
            {
                error = $"item '{item.Id}' references missing parentId '{item.ParentId}'";
                return false;
            }
        }

        return true;
    }

    private static ISBBotPresetEntry? SelectWeighted(IReadOnlyList<ISBBotPresetEntry> presets)
    {
        if (presets.Count == 1)
            return presets[0];
        var total = 0d;
        foreach (var preset in presets)
            total += preset.Weight;
        if (total <= 0d)
            return null;
        var roll = Random.Shared.NextDouble() * total;
        foreach (var preset in presets)
        {
            roll -= preset.Weight;
            if (roll <= 0d)
                return preset;
        }

        return presets[^1];
    }

    private int ApplyMixedMags(BotBase bot, string role, List<Item> items, MongoId equipmentRoot)
    {
        if (items.Count == 0)
            return 0;
        var cartridgeGroups = items.Where(x => !string.IsNullOrWhiteSpace(x.ParentId) && string.Equals(x.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase)).GroupBy(x => x.ParentId!, StringComparer.OrdinalIgnoreCase).ToList();
        if (cartridgeGroups.Count == 0)
            return 0;
        var itemsById = items.ToDictionary(x => x.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var botKey = bot.Id?.ToString() ?? equipmentRoot.ToString();
        var remixed = 0;
        HashSet<string>? removedCartridges = null;
        List<Item>? replacementCartridges = null;
        foreach (var group in cartridgeGroups)
        {
            if (!itemsById.TryGetValue(group.Key, out var magazine))
                continue;
            var cartridges = group.ToList();
            var firstCartridge = cartridges.FirstOrDefault();
            if (firstCartridge is null)
                continue;
            var caliber = itemHelper.GetItem(firstCartridge.Template).Value?.Properties?.Caliber;
            if (string.IsNullOrWhiteSpace(caliber))
                continue;
            if (!ISBMixedAmmoState.TryGet(role, caliber, out var mix))
                continue;
            if (!ISBMixedAmmoState.RollForBot(botKey, caliber, mix.chance))
                continue;
            var filled = 0;
            foreach (var cartridge in cartridges)
                filled += Math.Max(0, (int)(cartridge.Upd?.StackObjectsCount ?? 0));
            if (filled <= 0)
                continue;
            removedCartridges ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            replacementCartridges ??= new List<Item>();
            foreach (var cartridge in cartridges)
                removedCartridges.Add(cartridge.Id.ToString());
            replacementCartridges.AddRange(MixedAmmoStackBuilder.MagCustomStackVV(magazine.Id, filled, mix));
            remixed++;
        }

        if (removedCartridges != null && replacementCartridges != null)
        {
            items.RemoveAll(x => removedCartridges.Contains(x.Id.ToString()));
            items.AddRange(replacementCartridges);
        }

        return remixed;
    }

    private static void RemoveDogtagTree(List<Item> items)
    {
        var removeIds = items.Where(x => string.Equals(x.SlotId, "Dogtag", StringComparison.OrdinalIgnoreCase)).Select(x => x.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removeIds.Count == 0)
            return;
        bool changed;
        do
        {
            changed = false;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.ParentId) || !removeIds.Contains(item.ParentId))
                    continue;
                if (removeIds.Add(item.Id.ToString()))
                    changed = true;
            }
        }
        while (changed);
        items.RemoveAll(x => removeIds.Contains(x.Id.ToString()));
    }

    private void ApplyAppearance(BotBase bot, ISBBotPresetAppearance? appearance)
    {
        if (appearance is null)
            return;
        bot.Customization ??= new Customization();
        TrySetCustomization(appearance.head, value => bot.Customization.Head = value, "head");
        TrySetCustomization(appearance.body, value => bot.Customization.Body = value, "body");
        TrySetCustomization(appearance.feet, value => bot.Customization.Feet = value, "feet");
        TrySetCustomization(appearance.hands, value => bot.Customization.Hands = value, "hands");
        TrySetCustomization(appearance.voice, value => bot.Customization.Voice = value, "voice");
    }

    private void TrySetCustomization(string? rawValue, Action<MongoId> setter, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;
        try
        {
            setter(new MongoId(rawValue.Trim()));
        }
        catch (Exception)
        {
        }
    }

    private static ISBBotPresetAppearance? ParseAppearance(JsonElement element)
    {
        if (!TryGetProperty(element, "appearance", out var appearance) || appearance.ValueKind != JsonValueKind.Object)
            return null;
        var result = new ISBBotPresetAppearance();
        var any = false;
        if (TryGetString(appearance, "head", out var head))
        {
            result.head = head;
            any = true;
        }

        if (TryGetString(appearance, "body", out var body))
        {
            result.body = body;
            any = true;
        }

        if (TryGetString(appearance, "feet", out var feet))
        {
            result.feet = feet;
            any = true;
        }

        if (TryGetString(appearance, "hands", out var hands))
        {
            result.hands = hands;
            any = true;
        }

        if (TryGetString(appearance, "voice", out var voice))
        {
            result.voice = voice;
            any = true;
        }

        return any ? result : null;
    }

    private static bool TryNormalizeRole(string? rawRole, out string role)
    {
        role = string.Empty;
        if (string.IsNullOrWhiteSpace(rawRole))
            return false;
        if (GhostBotVariant.Is(rawRole.Trim()))
        {
            role = GhostBotVariant.CanonicalName(rawRole.Trim());
            return true;
        }

        if (!RoleAliases.TryGetValue(rawRole.Trim(), out var normalized) && !BlackDivisionPresetPolicy.Aliases.TryGetValue(rawRole.Trim(), out normalized))
            return false;
        role = normalized;
        return true;
    }

    private static string MakeKey(string role, string period) => $"{role}|{period}";
    private static bool IsJsonFile(string path)
    {
        var extension = IOPath.GetExtension(path);
        return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".jsonc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(element, name, out var property))
            return false;
        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0d;
        return TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value);
    }
}
