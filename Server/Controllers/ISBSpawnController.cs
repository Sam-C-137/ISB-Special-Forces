using ISB.Compatibility;
using ISBSpecialForcesServer.Patches;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.InRaid;
using MoreBotsServer;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using System.Linq;
using System.Reflection;
using ISB.MixedAmmo;
using IOPath = System.IO.Path;

namespace ISBSpecialForcesServer.Controllers;
public class ISBAppearanceOverride
{
    public Dictionary<string, double>? body { get; set; }
    public Dictionary<string, double>? feet { get; set; }
    public Dictionary<string, double>? hands { get; set; }
    public Dictionary<string, double>? head { get; set; }
    public Dictionary<string, double>? voice { get; set; }
    public Dictionary<string, Dictionary<string, double>>? equipment { get; set; }
    public Dictionary<string, Dictionary<string, double>>? ammo { get; set; }
    public Dictionary<string, Dictionary<string, List<string>>>? mods { get; set; }
    public Dictionary<string, CaliberIntDefinition>? mixedAmmo { get; set; }
}

[Injectable(InjectionType.Singleton)]
public class ISBSpawnController(JsonUtil jsonUtil, RandomUtil randomUtil, ConfigController configController, BotTable botTable, LocationTable locationTable, ISBLogger logger, ModHelper modHelper, WeatherHelper weatherHelper, TimeUtil timeUtil, IReadOnlyList<SptMod> modList, ISBWeaponPresetController weaponPresetController, ISBQuestTruceController questTruceController, FikaRaidContext fikaRaidContext, LabsVariantController labsVariants, ISBSpawnProgressController progress)
{
    private const string SecretSpawnTriggerPrefix = "isb_secret_";
    private const string LabsBlackoutTriggerPrefix = "isb_labs_blackout_";
    private const string LabsChaosTriggerPrefix = "isb_labs_chaos_";
    private const string LabsPowerRestoredTriggerPrefix = "isb_labs_power_restored_";
    private const string BlackDivisionModGuid = "com.blackdiv.tacticaltoaster";
    private enum SpawnFormationKind
    {
        Patrol,
        Checkpoint,
        Hunt
    }

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

    private HashSet<string> InstalledModGuids
    {
        get
        {
            _installedModGuids ??= modList.Select(m => m.ModMetadata.ModGuid).Where(g => !string.IsNullOrEmpty(g)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _installedModGuids;
        }
    }

    private static readonly string[] ISBBotTypes = ["ISBSpecialForces", "ISBBossFirefly", "ISBFollowerVipper", "ISBFollowerLoghan", "ISBFollowerSpearthrower", "ISBBossCommander", "ISBFollowerCommander", "ISBTeamLeader", "ISBSecondLeader", "ISBScout", "ISBHeavy", "ISBFollowerZofia", "ISBAssassin", "ISBMarksman", "ISBFollowerEvernight", "ISBFollowerNemesis"];
    private static readonly Dictionary<string, string[][]> ColabFolders = new(StringComparer.OrdinalIgnoreCase)
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
    private readonly object _labsPreBlackoutSync = new();
    private readonly Dictionary<string, string> _officeCodes = new(StringComparer.Ordinal);
    private readonly ISB.Compatibility.OfficeKteBriefing _officeBriefings = new();
    private readonly object _spawnAdjustmentSync = new();
    private readonly Dictionary<string, bool> _labsPreBlackoutEventBySession = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BossLocationSpawn>> _preBlackoutEventSpawnRefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BossLocationSpawn>> _labsBlackoutEventSpawnRefs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BossLocationSpawn>> _suppressedBlackDivisionSpawns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BossLocationSpawn>> _suppressedPmcSpawns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BossLocationSpawn>> _suppressedRaiderSpawns = new(StringComparer.OrdinalIgnoreCase);
    private bool _latestLabsPreBlackoutEventActive;
    private sealed record LockdownRaidPlan(string EventSpawnsJson, bool BlockPmcs, bool BlockRaiders);
    private readonly Dictionary<string, LockdownRaidPlan> _lockdownRaidPlans = new(StringComparer.Ordinal);
    private static float _lastRaidHour = -1f;
    private sealed class BotTypeBaseline
    {
        public string Appearance;
        public string Ammo;
        public string Equipment;
        public string Mods;
    }

    private readonly object _appearanceSync = new();
    private readonly Dictionary<string, BotTypeBaseline> _botTypeBaselines = new();
    private string? _appliedAppearanceSuffix;
    private void RestoreBaseline(string key, BotType botType_db, string suffix)
    {
        if (!_botTypeBaselines.TryGetValue(key, out var baseline))
        {
            baseline = new BotTypeBaseline
            {
                Appearance = jsonUtil.Serialize(botType_db.BotAppearance),
                Ammo = jsonUtil.Serialize(botType_db.BotInventory?.Ammo),
                Equipment = jsonUtil.Serialize(botType_db.BotInventory?.Equipment),
                Mods = jsonUtil.Serialize(botType_db.BotInventory?.Mods)
            };
            _botTypeBaselines[key] = baseline;
            LogDebug($"[ISB] {key}: baseline snapshot captured before first override.");
            return;
        }

        botType_db.BotAppearance = jsonUtil.Deserialize<Appearance>(baseline.Appearance);
        if (botType_db.BotInventory is not null)
        {
            botType_db.BotInventory.Ammo = jsonUtil.Deserialize<Dictionary<string, Dictionary<MongoId, double>>>(baseline.Ammo);
            botType_db.BotInventory.Equipment = jsonUtil.Deserialize<Dictionary<EquipmentSlots, Dictionary<MongoId, double>>>(baseline.Equipment);
            botType_db.BotInventory.Mods = jsonUtil.Deserialize<Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>>(baseline.Mods);
        }

        LogDebug($"[ISB] {key}: baseline restored before applying {suffix} overrides.");
    }

    private void LogDebug(string message)
    {
        if (configController.ModConfig.dayNight?.logs == true)
            logger.Warn(message);
    }

    public void SetRaidHourFromConfig(GetRaidConfigurationRequestData? info)
    {
        lock (_appearanceSync)
        {
            try
            {
                if (info is null)
                {
                    logger.Warn("[ISB] /raid/configuration: null.");
                    return;
                }

                var mapId = (info.Location ?? "").ToLowerInvariant();
                var timeVariant = info.TimeVariant.ToString().ToUpperInvariant();
                if (mapId.Contains("factory4_night"))
                {
                    _lastRaidHour = 3f;
                    LogDebug($"[ISB] ISB Evernight Squadron Map Detected: {mapId} => Forced nighttime operations equipment (03:00)");
                    ApplyDayNightAppearance();
                    return;
                }

                if (mapId.Contains("factory4_day"))
                {
                    _lastRaidHour = 15f;
                    LogDebug($"[ISB] ISB Sol Squadron Map Detected: {mapId} => Forced daytime operations equipment (15:00)");
                    ApplyDayNightAppearance();
                    return;
                }

                if (mapId.Contains("laboratory"))
                {
                    _lastRaidHour = 3f;
                    LogDebug($"[ISB] ISB Evernight Squadron Map Detected: {mapId} => Forced nighttime operations equipment (Fixed Loadout)");
                    ApplyDayNightAppearance();
                    return;
                }

                if (mapId.Contains("labyrinth"))
                {
                    _lastRaidHour = 3f;
                    LogDebug($"[ISB] ISB Evernight Squadron Map Detected: {mapId} => Forced nighttime operations equipment (Fixed Loadout)");
                    ApplyDayNightAppearance();
                    return;
                }

                if (string.Equals(mapId, IcebreakerMapId, StringComparison.Ordinal))
                {
                    _lastRaidHour = 3f;
                    LogDebug($"[ISB] ISB Evernight Squadron Map Detected: {mapId} => Forced nighttime operations equipment (Icebreaker Fixed Loadout)");
                    ApplyDayNightAppearance();
                    return;
                }

                int baseHour;
                try
                {
                    var now = timeUtil.GetTimeStamp();
                    var inRaidTime = weatherHelper.GetInRaidTime(now);
                    baseHour = inRaidTime.Hour;
                }
                catch (Exception ex)
                {
                    logger.Warn($"[ISB] GetInRaidTime failed: {ex.Message}. Using server time.");
                    baseHour = DateTime.Now.Hour;
                }

                int hour = baseHour;
                if (timeVariant.Contains("PAST"))
                    hour = (baseHour + 12) % 24;
                _lastRaidHour = hour;
                LogDebug($"[ISB] /raid/configuration -> Map={mapId}, TimeVariant={timeVariant}, " + $"baseHour={baseHour}h, finalHour={hour}h => {(IsNightTime() ? "EVERNIGHT" : "EVERYDAY")}");
                ApplyDayNightAppearance();
            }
            catch (Exception ex)
            {
                logger.Warn($"[ISB] Process /raid/configuration failed: {ex.Message}");
            }
        }
    }

    public bool CurrentRaidIsNight() => IsNightTime();
    private bool IsNightTime()
    {
        var dn = configController.ModConfig.dayNight;
        if (dn?.ForceNightAppearance == true)
            return true;
        if (dn?.ForceDayAppearance == true)
            return false;
        if (_lastRaidHour < 0)
            return false;
        var nightStart = dn?.nightStartHour ?? 21;
        var nightEnd = dn?.nightEndHour ?? 6;
        if (nightStart > nightEnd)
            return _lastRaidHour >= nightStart || _lastRaidHour < nightEnd;
        return _lastRaidHour >= nightStart && _lastRaidHour < nightEnd;
    }

    private void ApplyDayNightAppearance()
    {
        lock (_appearanceSync)
        {
            var suffix = IsNightTime() ? "Night" : "Day";
            if (configController.ModConfig.dayNight?.ForceNightAppearance == true)
                LogDebug($"[ISB] ForceNightAppearance active: Forced ISB NIGHT Loadouts.");
            else if (configController.ModConfig.dayNight?.ForceDayAppearance == true)
                LogDebug($"[ISB] ForceDayAppearance active: Forced ISB DAYTIME Loadouts.");
            LogDebug($"Appearance selection: {suffix}; hour={_lastRaidHour:F1}; forceNight={configController.ModConfig.dayNight?.ForceNightAppearance}; forceDay={configController.ModConfig.dayNight?.ForceDayAppearance}.");
            if (string.Equals(_appliedAppearanceSuffix, suffix, StringComparison.Ordinal))
            {
                return;
            }

            var applied = false;
            weaponPresetController.RunSynchronized(() =>
            {
                applied = ApplyDayNightAppearanceCore(suffix);
            });
            if (applied)
                _appliedAppearanceSuffix = suffix;
        }
    }

    private bool ApplyDayNightAppearanceCore(string suffix)
    {
        if (botTable.Types is null)
        {
            logger.Warn("[ISB] tables.Bots.Types null, appearance aborted.");
            return false;
        }

        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var appearanceDir = IOPath.Combine(modFolder, "db", "appearance");
        ISBMixedAmmoState.ClearAll();
        foreach (var botType in ISBBotTypes)
        {
            var filePath = IOPath.Combine(appearanceDir, $"{botType}_{suffix}.jsonc");
            if (!File.Exists(filePath))
            {
                LogDebug($"[ISB] Appearance not found, skiping: {filePath}");
                continue;
            }

            ISBAppearanceOverride? data;
            try
            {
                data = jsonUtil.Deserialize<ISBAppearanceOverride>(File.ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                logger.Error($"[ISB] Failed to read {filePath}: {ex.Message}");
                continue;
            }

            if (data is null)
            {
                logger.Warn($"[ISB] Data null {filePath}, skiping.");
                continue;
            }

            var key = botType.ToLowerInvariant();
            if (!botTable.Types.ContainsKey(key))
            {
                logger.Warn($"[ISB] Bot type not found on DB: {key}");
                continue;
            }

            var botType_db = botTable.Types[key];
            if (botType_db is null)
            {
                logger.Warn($"[ISB] Null entry on {key}, skiping.");
                continue;
            }

            RestoreBaseline(key, botType_db, suffix);
            var current = botType_db.BotAppearance;
            botTable.Types.TryGetValue("isbspecialforces", out var sharedBot);
            var sharedAppearance = sharedBot?.BotAppearance;
            var newAppearance = new Appearance
            {
                Body = PickPool(data.body, current?.Body, sharedAppearance?.Body),
                Feet = PickPool(data.feet, current?.Feet, sharedAppearance?.Feet),
                Hands = PickPool(data.hands, current?.Hands, sharedAppearance?.Hands),
                Head = PickPool(data.head, current?.Head, sharedAppearance?.Head),
                Voice = PickPool(data.voice, current?.Voice, sharedAppearance?.Voice),
            };
            botType_db.BotAppearance = newAppearance;
            if (data.equipment is { Count: > 0 })
                ApplyEquipmentOverride(botType_db, data.equipment, key, suffix);
            if (data.ammo is { Count: > 0 })
                ApplyAmmoOverride(botType_db, data.ammo, key, suffix);
            if (data.mods is { Count: > 0 })
                ApplyModsOverride(botType_db, data.mods, key, suffix);
            if (data.mixedAmmo is { Count: > 0 })
            {
                foreach (var caliberEntry in data.mixedAmmo)
                {
                    if (caliberEntry.Value?.entries == null || caliberEntry.Value.entries.Count == 0)
                        logger.Warn($"[ISB] {key} ({suffix}): mixedAmmo '{caliberEntry.Key}' has no entries. Check for PascalCase keys, the parser only accepts lowercase (chance, mode, entries, tpl, ratio).");
                }

                ISBMixedAmmoState.Merge(key, data.mixedAmmo);
                LogDebug($"[ISB] {key} ({suffix}): mixed ammo registered for {data.mixedAmmo.Count} caliber(s).");
            }

            ApplyColabGear(botType_db, appearanceDir, botType, suffix, key);
            LogDebug($"[ISB] Appearance {suffix} applied to {key} " + $"(body={newAppearance.Body.Count}, feet={newAppearance.Feet.Count}, " + $"hourOfDay={_lastRaidHour:F1}).");
        }

        weaponPresetController.ApplyDefinWeapPreset(IsNightTime());
        return true;
    }

    private void ApplyColabGear(BotType botType_db, string appearanceDir, string botType, string suffix, string key)
    {
        if (configController.ModConfig?.enableCollabLoadoutsAndAppearances != true)
            return;
        var appliedColabs = new List<string>();
        foreach (var(folder, requirementGroups)in ColabFolders)
        {
            if (!IsCollabFolderActive(folder, requirementGroups))
                continue;
            if (configController.ModConfig?.IsCollabLoadoutEnabled(folder) != true)
                continue;
            var filePath = IOPath.Combine(appearanceDir, folder, $"{botType}_{suffix}.jsonc");
            if (!File.Exists(filePath))
                continue;
            ISBAppearanceOverride? colabData;
            try
            {
                colabData = jsonUtil.Deserialize<ISBAppearanceOverride>(File.ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                logger.Warn($"[ISB] {key}: colab folder reading error {folder}: {ex.Message}");
                continue;
            }

            var applied = false;
            if (colabData?.equipment is { Count: > 0 })
            {
                ApplyEquipmentOverride(botType_db, colabData.equipment, key, suffix, additive: true);
                applied = true;
            }

            if (colabData?.ammo is { Count: > 0 })
            {
                ApplyAmmoOverride(botType_db, colabData.ammo, key, suffix, additive: true);
                applied = true;
            }

            if (colabData?.mods is { Count: > 0 })
            {
                ApplyModsOverride(botType_db, colabData.mods, key, suffix, additive: true);
                applied = true;
            }

            if (colabData?.mixedAmmo is { Count: > 0 })
            {
                ISBMixedAmmoState.Merge(key, colabData.mixedAmmo);
                applied = true;
            }

            if (!applied)
                continue;
            appliedColabs.Add(folder);
        }

        if (appliedColabs.Count > 0)
            LogDebug($"[ISB] {key} ({suffix}): Mod Loadout active. {appliedColabs.Count} folder(s): " + string.Join(", ", appliedColabs));
    }

    private void ApplyEquipmentOverride(BotType botType_db, Dictionary<string, Dictionary<string, double>> slotData, string key, string suffix, bool additive = false)
    {
        try
        {
            var inventory = botType_db.BotInventory;
            var currentEquipment = inventory?.Equipment;
            if (inventory is null || currentEquipment is null)
            {
                LogDebug($"[ISB] {key}: BotInventory.Equipment null, skiping.");
                return;
            }

            var equipment = new Dictionary<EquipmentSlots, Dictionary<MongoId, double>>();
            foreach (var entry in currentEquipment)
            {
                equipment[entry.Key] = entry.Value is null ? new Dictionary<MongoId, double>() : new Dictionary<MongoId, double>(entry.Value);
            }

            foreach (var slotEntry in slotData)
            {
                if (!Enum.TryParse<EquipmentSlots>(slotEntry.Key, ignoreCase: true, out var slot))
                {
                    logger.Warn($"[ISB] {key}: slot unknown '{slotEntry.Key}', skiping.");
                    continue;
                }

                if (slotEntry.Value is not { Count: > 0 })
                    continue;
                Dictionary<MongoId, double> pool;
                if (additive && equipment.TryGetValue(slot, out var existing) && existing is not null)
                    pool = existing;
                else
                    pool = new Dictionary<MongoId, double>();
                foreach (var item in slotEntry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key))
                    {
                        MongoId id = item.Key;
                        pool[id] = item.Value;
                    }
                }

                equipment[slot] = pool;
            }

            inventory.Equipment = equipment;
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] {key}: failed to set equipment: {ex.Message}");
        }
    }

    private void ApplyAmmoOverride(BotType botType_db, Dictionary<string, Dictionary<string, double>> ammoData, string key, string suffix, bool additive = false)
    {
        try
        {
            var inventory = botType_db.BotInventory;
            var currentAmmo = inventory?.Ammo;
            if (inventory is null || currentAmmo is null)
            {
                LogDebug($"[ISB] {key}: BotInventory.Ammo null, skiping ammo.");
                return;
            }

            var ammo = new Dictionary<string, Dictionary<MongoId, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in currentAmmo)
            {
                ammo[entry.Key] = entry.Value is null ? new Dictionary<MongoId, double>() : new Dictionary<MongoId, double>(entry.Value);
            }

            foreach (var caliberEntry in ammoData)
            {
                if (string.IsNullOrWhiteSpace(caliberEntry.Key))
                    continue;
                if (caliberEntry.Value is not { Count: > 0 })
                    continue;
                var caliber = caliberEntry.Key;
                Dictionary<MongoId, double> pool;
                if (additive && ammo.TryGetValue(caliber, out var existing) && existing is not null)
                    pool = existing;
                else
                    pool = new Dictionary<MongoId, double>();
                foreach (var round in caliberEntry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(round.Key))
                    {
                        MongoId id = round.Key;
                        pool[id] = round.Value;
                    }
                }

                ammo[caliber] = pool;
            }

            inventory.Ammo = ammo;
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] {key}: failed to apply ammo: {ex.Message}");
        }
    }

    private void ApplyModsOverride(BotType botType_db, Dictionary<string, Dictionary<string, List<string>>> modsData, string key, string suffix, bool additive = false)
    {
        try
        {
            var inventory = botType_db.BotInventory;
            var currentMods = inventory?.Mods;
            if (inventory is null || currentMods is null)
            {
                LogDebug($"[ISB] {key}: BotInventory.Mods null, skiping mods.");
                return;
            }

            var mods = new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>();
            foreach (var parent in currentMods)
            {
                var slotCopy = new Dictionary<string, HashSet<MongoId>>(StringComparer.OrdinalIgnoreCase);
                if (parent.Value is not null)
                {
                    foreach (var slot in parent.Value)
                    {
                        slotCopy[slot.Key] = slot.Value is null ? new HashSet<MongoId>() : new HashSet<MongoId>(slot.Value);
                    }
                }

                mods[parent.Key] = slotCopy;
            }

            foreach (var parentEntry in modsData)
            {
                if (string.IsNullOrWhiteSpace(parentEntry.Key))
                    continue;
                if (parentEntry.Value is not { Count: > 0 })
                    continue;
                MongoId parentId = parentEntry.Key;
                Dictionary<string, HashSet<MongoId>> slotMap;
                if (additive && mods.TryGetValue(parentId, out var existingParent) && existingParent is not null)
                    slotMap = existingParent;
                else
                    slotMap = new Dictionary<string, HashSet<MongoId>>(StringComparer.OrdinalIgnoreCase);
                foreach (var slotEntry in parentEntry.Value)
                {
                    if (string.IsNullOrWhiteSpace(slotEntry.Key))
                        continue;
                    if (slotEntry.Value is not { Count: > 0 })
                        continue;
                    var slot = slotEntry.Key;
                    HashSet<MongoId> set;
                    if (additive && slotMap.TryGetValue(slot, out var existingSet) && existingSet is not null)
                        set = existingSet;
                    else
                        set = new HashSet<MongoId>();
                    foreach (var tpl in slotEntry.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(tpl))
                        {
                            MongoId id = tpl;
                            set.Add(id);
                        }
                    }

                    slotMap[slot] = set;
                }

                mods[parentId] = slotMap;
            }

            inventory.Mods = mods;
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] {key}: failed to apply mods: {ex.Message}");
        }
    }

    private static Dictionary<MongoId, double> PickPool(Dictionary<string, double>? overridePool, Dictionary<MongoId, double>? currentPool, Dictionary<MongoId, double>? sharedPool)
    {
        var result = new Dictionary<MongoId, double>();
        if (overridePool is { Count: > 0 })
        {
            foreach (var kvp in overridePool)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                {
                    MongoId id = kvp.Key;
                    result[id] = kvp.Value;
                }
            }

            return result;
        }

        var sourcePool = currentPool is { Count: > 0 } ? currentPool : sharedPool;
        if (sourcePool is { Count: > 0 })
        {
            foreach (var kvp in sourcePool)
                result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    private const string IcebreakerMapId = "icebreaker";
    private const string IcebreakerModGuid = "com.manimal.icebreaker";
    public void AdjustAllISBSpawns()
    {
        try
        {
            ApplyDayNightAppearance();
            var mainConfig = configController.ModConfig;
            foreach (var map in mainConfig.locations.Keys)
            {
                if (string.Equals(map, IcebreakerMapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                AdjustISBSpawnsForMapInternal(map, mainConfig);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error adjusting ISB spawns: {ex.Message}");
            throw;
        }
    }

    public void AdjustISBSpawnsForRaid(GetRaidConfigurationRequestData? info, string? sessionId = null)
    {
        sessionId = fikaRaidContext.EventSession(sessionId) ?? sessionId;
        if (info is null || string.IsNullOrWhiteSpace(info.Location))
        {
            logger.Warn("[ISB] Unable to reapply spawns: raid location is missing.");
            return;
        }

        var requestedMap = NormalizeMapId(info.Location);
        progress.Prepare(sessionId, requestedMap, string.Equals(info.Side?.ToString(), "Savage", StringComparison.OrdinalIgnoreCase));
        var configuredMap = configController.ModConfig.locations.Keys.FirstOrDefault(map => NormalizeMapId(map) == requestedMap);
        if (configuredMap is null)
        {
            logger.Info($"No ISB spawn configuration found for {info.Location}. Skipping raid spawn adjustment.");
            return;
        }

        if (string.Equals(configuredMap, IcebreakerMapId, StringComparison.OrdinalIgnoreCase))
        {
            EndRaid(sessionId);
            SetLabsPreBlackoutEventState(sessionId, false);
            AdjustIcebreakerSpawns();
            return;
        }

        var labsEventActive = string.Equals(NormalizeMapId(configuredMap), NormalizeMapId("laboratory"), StringComparison.Ordinal) && RollLabsPreBlackoutEvent(sessionId);
        if (!labsEventActive && !string.Equals(NormalizeMapId(configuredMap), NormalizeMapId("laboratory"), StringComparison.Ordinal))
            SetLabsPreBlackoutEventState(sessionId, false);
        lock (_spawnAdjustmentSync)
        {
            if (labsEventActive && !string.IsNullOrWhiteSpace(sessionId) && _lockdownRaidPlans.ContainsKey(sessionId))
                return;
            AdjustISBSpawnsForMapInternal(configuredMap, configController.ModConfig, labsEventActive);
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _lockdownRaidPlans.Remove(sessionId);
                if (labsEventActive)
                {
                    var eventSpawns = new List<BossLocationSpawn>();
                    if (_preBlackoutEventSpawnRefs.TryGetValue(configuredMap, out var initial))
                        eventSpawns.AddRange(initial);
                    if (_labsBlackoutEventSpawnRefs.TryGetValue(configuredMap, out var triggered))
                        eventSpawns.AddRange(triggered);
                    var control = configController.ModConfig.labsContainmentBreachEvent?.populationControl ?? new LabsPreBlackoutPopulationControlConfig();
                    _lockdownRaidPlans[sessionId] = new LockdownRaidPlan(jsonUtil.Serialize(eventSpawns) ?? throw new InvalidOperationException("Could not snapshot Lockdown formations."), control.enabled && control.disableNormalPMCs, control.enabled && control.disableNormalRaiders);
                }
            }
        }
    }

    public LabsPreBlackoutEventStatus GetLabsPreBlackoutEventStatus(string? sessionId)
    {
        sessionId = fikaRaidContext.EventSession(sessionId) ?? sessionId;
        lock (_labsPreBlackoutSync)
        {
            var active = string.IsNullOrWhiteSpace(sessionId) ? _latestLabsPreBlackoutEventActive : _labsPreBlackoutEventBySession.TryGetValue(sessionId, out var sessionActive) && sessionActive;
            return new LabsPreBlackoutEventStatus
            {
                active = active,
                performanceMode = active && configController.ModConfig.labsContainmentBreachEvent?.performanceMode == true,
                variant = labsVariants.RaidMode(sessionId),
                officeAccess = GetOfficeAccess(sessionId, active),
                blackDivisionInstalled = InstalledModGuids.Contains(BlackDivisionModGuid),
                labsChaosEnabled = active && configController.ModConfig.labsContainmentBreachEvent?.labsChaos?.enabled == true,
                disableFire = active && configController.ModConfig.labsContainmentBreachEvent?.disableFire == true,
                disableSirens = active && configController.ModConfig.labsContainmentBreachEvent?.disableSirens == true,
                disableDestroyedVehicles = active && configController.ModConfig.labsContainmentBreachEvent?.disableDestroyedVehicles == true
            };
        }
    }

    private ISB.Compatibility.OfficeAccessState? GetOfficeAccess(string? sessionId, bool active)
    {
        var config = configController.ModConfig.labsContainmentBreachEvent?.officeAccess;
        if (config == null || string.IsNullOrWhiteSpace(sessionId) || !ISB.Compatibility.OfficeAccessState.Available(active, InstalledModGuids.Contains("com.manimal.labsboiler"), config))
            return null;
        if (!_officeCodes.TryGetValue(sessionId, out var code))
        {
            do
            {
                code = randomUtil.GetInt(0, 9999).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            }
            while (code == ISB.Compatibility.OfficeAccessState.FixedCode);
            _officeCodes[sessionId] = code;
        }

        return new ISB.Compatibility.OfficeAccessState
        {
            config = config,
            raidCode = code,
            kteBriefing = _officeBriefings.GetOrCreate(sessionId, (minimum, maximum) => randomUtil.GetInt(minimum, maximum))
        };
    }

    public void PrepareLabsLocationForGeneration(string sessionId, string map, bool isPmc)
    {
        if (NormalizeMapId(map) != NormalizeMapId("laboratory"))
            return;
        sessionId = fikaRaidContext.EventSession(sessionId) ?? sessionId;
        lock (_labsPreBlackoutSync)
            if (_labsPreBlackoutEventBySession.ContainsKey(sessionId))
                return;
        AdjustISBSpawnsForRaid(new GetRaidConfigurationRequestData { Location = map, Side = isPmc ? SideType.Pmc : SideType.Savage }, sessionId);
    }

    public void FilterLockdownRaidPopulation(string sessionId, string map, LocationBase location)
    {
        sessionId = fikaRaidContext.EventSession(sessionId) ?? sessionId;
        if (location == null || string.IsNullOrEmpty(sessionId) || !ISBLockdownPopulation.Applies(NormalizeMapId(map), true))
            return;
        LockdownRaidPlan? plan;
        lock (_spawnAdjustmentSync)
            _lockdownRaidPlans.TryGetValue(sessionId, out plan);
        if (plan == null)
            return;
        var eventSpawns = jsonUtil.Deserialize<List<BossLocationSpawn>>(plan.EventSpawnsJson) ?? throw new InvalidOperationException("ISB Lockdown event snapshot could not be read.");
        ISBLockdownPopulation.Apply(location, eventSpawns, plan.BlockPmcs, plan.BlockRaiders);
    }

    public void EndRaid(string? sessionId)
    {
        if (fikaRaidContext.EventSession(sessionId) != sessionId)
            return;
        labsVariants.EndRaid(sessionId);
        lock (_labsPreBlackoutSync)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _labsPreBlackoutEventBySession.Remove(sessionId);
                _officeCodes.Remove(sessionId);
                _officeBriefings.EndRaid(sessionId);
            }

            _latestLabsPreBlackoutEventActive = false;
        }

        lock (_spawnAdjustmentSync)
            if (!string.IsNullOrWhiteSpace(sessionId))
                _lockdownRaidPlans.Remove(sessionId);
    }

    private bool RollLabsPreBlackoutEvent(string? sessionId)
    {
        lock (_labsPreBlackoutSync)
        {
            if (!string.IsNullOrWhiteSpace(sessionId) && _labsPreBlackoutEventBySession.TryGetValue(sessionId, out var existing))
            {
                _latestLabsPreBlackoutEventActive = existing;
                return existing;
            }

            var variant = labsVariants.BeginRaid(sessionId);
            if (variant != "legacy")
            {
                var selectedActive = variant == "containment";
                if (selectedActive)
                    progress.ContainmentChance(sessionId, 100, true);
                SetLabsPreBlackoutEventState(sessionId, selectedActive);
                logger.Info($"Labs variant: {variant}; ContainmentBreach={selectedActive} (session decision).");
                return selectedActive;
            }

            var eventConfig = configController.ModConfig.labsContainmentBreachEvent ?? new LabsPreBlackoutEventConfig();
            if (!eventConfig.enabled)
            {
                _latestLabsPreBlackoutEventActive = false;
                if (!string.IsNullOrWhiteSpace(sessionId))
                    _labsPreBlackoutEventBySession[sessionId] = false;
                return false;
            }

            var questOwner = fikaRaidContext.QuestOwner(sessionId);
            var questForced = !string.IsNullOrWhiteSpace(questOwner) && eventConfig.questForceConditions?.ShouldForceEvent(questId => questTruceController.TryGetQuestStatus(questOwner, questId)) == true;
            var chance = progress.ContainmentChance(sessionId, questForced ? 100 : Math.Clamp(eventConfig.chance, 0, 100), true);
            var roll = randomUtil.GetInt(0, 10000) / 100f;
            var active = chance >= 100f || (chance > 0f && roll < chance);
            logger.Info($"ContainmentBreach: chance={chance:F2}%, roll={roll:F2}, questForced={questForced}, active={active}.");
            _latestLabsPreBlackoutEventActive = active;
            if (!string.IsNullOrWhiteSpace(sessionId))
                _labsPreBlackoutEventBySession[sessionId] = active;
            return active;
        }
    }

    private void SetLabsPreBlackoutEventState(string? sessionId, bool active)
    {
        lock (_labsPreBlackoutSync)
        {
            _latestLabsPreBlackoutEventActive = active;
            if (!string.IsNullOrWhiteSpace(sessionId))
                _labsPreBlackoutEventBySession[sessionId] = active;
        }
    }

    private static string NormalizeMapId(string value)
    {
        return new string (value.Where(char.IsLetterOrDigit).Select(character => char.ToLowerInvariant(character)).ToArray());
    }

    public void AdjustIcebreakerSpawns()
    {
        if (!InstalledModGuids.Contains(IcebreakerModGuid))
        {
            logger.Info("Icebreaker is not installed. Skipping ISB spawns for icebreaker.");
            return;
        }

        logger.Info("Icebreaker detected. Applying ISB spawns to icebreaker.");
        AdjustISBSpawnsForMap(IcebreakerMapId);
    }

    public void AdjustISBSpawnsForMap(string map)
    {
        try
        {
            AdjustISBSpawnsForMapInternal(map, configController.ModConfig);
        }
        catch (Exception ex)
        {
            logger.Error($"Error adjusting ISB spawns for {map}: {ex.Message}");
            throw;
        }
    }

    private void AdjustISBSpawnsForMapInternal(string map, MainConfig mainConfig, bool labsPreBlackoutEventActive = false, bool allowBlackout = true)
    {
        lock (_spawnAdjustmentSync)
        {
            if (!mainConfig.locations.TryGetValue(map, out var mapConfig))
            {
                logger.Info($"No ISB spawn configuration found for {map}. Skipping ISB spawn adjustment.");
                return;
            }

            var locations = locationTable.GetDictionary();
            if (!locations.TryGetValue(map, out var location) || location?.Base is null)
            {
                logger.Info($"No location data found for {map}. Skipping ISB spawn adjustment.");
                return;
            }

            logger.Info($"Adjusting ISB spawns for {map}.");
            var patrolConfig = mapConfig.patrol;
            var checkpointConfig = mapConfig.checkpoint;
            var huntConfig = mapConfig.hunt;
            var spawns = location.Base.BossLocationSpawn;
            LabsEventSpawnModes.Restore(spawns);
            RestoreSuppressedBlackDivisionSpawns(map, spawns);
            RestoreSuppressedPmcSpawns(map, spawns);
            RestoreSuppressedRaiderSpawns(map, spawns);
            RemoveTrackedPreBlackoutEventSpawns(map, spawns);
            RemoveTrackedLabsBlackoutEventSpawns(map, spawns);
            var removedSpawnCount = spawns.RemoveAll(IsManagedISBSpawn);
            logger.Info($"Removed {removedSpawnCount} previous ISB-managed spawns from {map}.");
            var eventConfig = mainConfig.labsContainmentBreachEvent ?? new LabsPreBlackoutEventConfig();
            var useEventPool = labsPreBlackoutEventActive && string.Equals(NormalizeMapId(map), NormalizeMapId("laboratory"), StringComparison.Ordinal);
            if (!useEventPool || !eventConfig.disableNormalISBSpawnsDuringEvent)
            {
                if (patrolConfig.EnableISBPatrols)
                    AdjustPatrolSpawnsForMap(map, mapConfig, mainConfig, spawns);
                if (checkpointConfig.enableISBHoldZones)
                    AdjustCheckpointSpawnsForMap(map, mapConfig, mainConfig, spawns);
                if (huntConfig?.enableHunt == true)
                    AdjustHuntSpawnsForMap(map, mapConfig, mainConfig, spawns);
                if (mapConfig.enableSecrets)
                    AddISBSecretSpawns(map, spawns);
            }

            if (useEventPool)
            {
                ApplyLabsPreBlackoutEventSpawns(map, eventConfig, mainConfig, spawns);
                AddLabsBlackoutEventPhase(map, eventConfig.labsChaos, "Labs Chaos", LabsChaosTriggerPrefix, mainConfig, spawns);
            }

            if (string.Equals(NormalizeMapId(map), NormalizeMapId("laboratory"), StringComparison.Ordinal))
                if (allowBlackout)
                    ApplyLabsBlackoutEventSpawns(map, mainConfig.labsBlackoutEvent, mainConfig, spawns);
            if (useEventPool)
                LabsEventSpawnModes.Apply(spawns, eventConfig);
        }
    }

    private void ApplyLabsPreBlackoutEventSpawns(string map, LabsPreBlackoutEventConfig eventConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        var populationControl = eventConfig.populationControl ?? new LabsPreBlackoutPopulationControlConfig();
        if (populationControl.enabled)
        {
            if (populationControl.disableNormalPMCs)
                SuppressNormalPmcSpawns(map, spawns);
            if (populationControl.disableNormalRaiders)
                SuppressNormalRaiderSpawns(map, spawns);
        }

        if (eventConfig.isb?.enabled == true)
        {
            AddPreBlackoutEventFormations(map, eventConfig.isb.patrols, SpawnFormationKind.Patrol, mainConfig, spawns, "ISB");
            AddPreBlackoutEventFormations(map, eventConfig.isb.checkpoints, SpawnFormationKind.Checkpoint, mainConfig, spawns, "ISB");
            AddPreBlackoutEventFormations(map, eventConfig.isb.hunts, SpawnFormationKind.Hunt, mainConfig, spawns, "ISB");
        }

        if (eventConfig.pmcs?.enabled == true)
        {
            AddPreBlackoutEventFormations(map, eventConfig.pmcs.patrols, SpawnFormationKind.Patrol, mainConfig, spawns, "PMC");
            AddPreBlackoutEventFormations(map, eventConfig.pmcs.checkpoints, SpawnFormationKind.Checkpoint, mainConfig, spawns, "PMC");
            AddPreBlackoutEventFormations(map, eventConfig.pmcs.hunts, SpawnFormationKind.Hunt, mainConfig, spawns, "PMC");
        }

        if (eventConfig.raiders?.enabled == true)
        {
            AddPreBlackoutEventFormations(map, eventConfig.raiders.patrols, SpawnFormationKind.Patrol, mainConfig, spawns, "Raider");
            AddPreBlackoutEventFormations(map, eventConfig.raiders.checkpoints, SpawnFormationKind.Checkpoint, mainConfig, spawns, "Raider");
            AddPreBlackoutEventFormations(map, eventConfig.raiders.hunts, SpawnFormationKind.Hunt, mainConfig, spawns, "Raider");
        }

        if (eventConfig.blackDivision?.enabled != true)
            return;
        if (!InstalledModGuids.Contains(BlackDivisionModGuid) && !eventConfig.noBlackDivMode)
        {
            return;
        }

        if (eventConfig.disableNormalBlackDivisionSpawnsDuringEvent)
            SuppressNormalBlackDivisionSpawns(map, spawns);
        AddPreBlackoutEventFormations(map, eventConfig.blackDivision.patrols, SpawnFormationKind.Patrol, mainConfig, spawns, "Black Division");
        AddPreBlackoutEventFormations(map, eventConfig.blackDivision.checkpoints, SpawnFormationKind.Checkpoint, mainConfig, spawns, "Black Division");
        AddPreBlackoutEventFormations(map, eventConfig.blackDivision.hunts, SpawnFormationKind.Hunt, mainConfig, spawns, "Black Division");
    }

    private void ApplyLabsBlackoutEventSpawns(string map, LabsBlackoutEventConfig? eventConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        if (eventConfig?.enabled != true)
            return;
        AddLabsBlackoutEventPhase(map, eventConfig.blackoutActivated, "blackout activated", LabsBlackoutTriggerPrefix, mainConfig, spawns);
        AddLabsBlackoutEventPhase(map, eventConfig.powerRestored, "power restored", LabsPowerRestoredTriggerPrefix, mainConfig, spawns);
    }

    private void AddLabsBlackoutEventPhase(string map, LabsBlackoutEventPhaseConfig? phase, string phaseLabel, string triggerPrefix, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        if (phase?.enabled != true)
            return;
        var selectionMode = string.IsNullOrWhiteSpace(phase.selectionMode) ? "preferblackdivision" : phase.selectionMode.Trim().ToLowerInvariant();
        var blackDivisionInstalled = InstalledModGuids.Contains(BlackDivisionModGuid) || _latestLabsPreBlackoutEventActive && mainConfig.labsContainmentBreachEvent?.noBlackDivMode == true;
        var useIsb = false;
        var useBlackDivision = false;
        switch (selectionMode)
        {
            case "isb":
            case "isbonly":
                useIsb = phase.isb?.enabled == true;
                break;
            case "blackdivision":
            case "blackdivisiononly":
                useBlackDivision = blackDivisionInstalled && phase.blackDivision?.enabled == true;
                break;
            case "both":
                useIsb = phase.isb?.enabled == true;
                useBlackDivision = blackDivisionInstalled && phase.blackDivision?.enabled == true;
                break;
            default:
                if (blackDivisionInstalled && phase.blackDivision?.enabled == true)
                    useBlackDivision = true;
                else
                    useIsb = phase.isb?.enabled == true;
                break;
        }

        if (useIsb)
        {
            AddTriggeredLabsEventFormations(map, phase.isb.patrols, SpawnFormationKind.Patrol, triggerPrefix + "patrol", mainConfig, spawns, "ISB");
            AddTriggeredLabsEventFormations(map, phase.isb.checkpoints, SpawnFormationKind.Checkpoint, triggerPrefix + "checkpoint", mainConfig, spawns, "ISB");
            AddTriggeredLabsEventFormations(map, phase.isb.hunts, SpawnFormationKind.Hunt, triggerPrefix + "hunt", mainConfig, spawns, "ISB");
        }

        if (phase.pmcs?.enabled == true)
        {
            AddTriggeredLabsEventFormations(map, phase.pmcs.patrols, SpawnFormationKind.Patrol, triggerPrefix + "patrol", mainConfig, spawns, "PMC");
            AddTriggeredLabsEventFormations(map, phase.pmcs.checkpoints, SpawnFormationKind.Checkpoint, triggerPrefix + "checkpoint", mainConfig, spawns, "PMC");
            AddTriggeredLabsEventFormations(map, phase.pmcs.hunts, SpawnFormationKind.Hunt, triggerPrefix + "hunt", mainConfig, spawns, "PMC");
        }

        if (phase.raiders?.enabled == true)
        {
            AddTriggeredLabsEventFormations(map, phase.raiders.patrols, SpawnFormationKind.Patrol, triggerPrefix + "patrol", mainConfig, spawns, "Raider");
            AddTriggeredLabsEventFormations(map, phase.raiders.checkpoints, SpawnFormationKind.Checkpoint, triggerPrefix + "checkpoint", mainConfig, spawns, "Raider");
            AddTriggeredLabsEventFormations(map, phase.raiders.hunts, SpawnFormationKind.Hunt, triggerPrefix + "hunt", mainConfig, spawns, "Raider");
        }

        if (useBlackDivision)
        {
            AddTriggeredLabsEventFormations(map, phase.blackDivision.patrols, SpawnFormationKind.Patrol, triggerPrefix + "patrol", mainConfig, spawns, "Black Division");
            AddTriggeredLabsEventFormations(map, phase.blackDivision.checkpoints, SpawnFormationKind.Checkpoint, triggerPrefix + "checkpoint", mainConfig, spawns, "Black Division");
            AddTriggeredLabsEventFormations(map, phase.blackDivision.hunts, SpawnFormationKind.Hunt, triggerPrefix + "hunt", mainConfig, spawns, "Black Division");
        }
    }

    private void AddTriggeredLabsEventFormations(string map, List<SpawnFormationConfig>? formations, SpawnFormationKind kind, string triggerId, MainConfig mainConfig, List<BossLocationSpawn> spawns, string factionLabel)
    {
        if (formations is not { Count: > 0 })
            return;
        var activeFormations = formations.Where(formation => formation?.enabled == true).ToList();
        if (activeFormations.Count == 0)
            return;
        foreach (var formation in activeFormations)
        {
            var squadCount = Math.Max(1, formation.squadCount);
            var configuredZones = (formation.zones ?? new List<string>()).Where(zone => !string.IsNullOrWhiteSpace(zone)).Select(zone => zone.Trim()).ToList();
            var availableZones = new List<string>(configuredZones);
            if (kind == SpawnFormationKind.Checkpoint && configuredZones.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < squadCount; i++)
            {
                var chance = Math.Max(0f, Math.Min(100f, formation.chance));
                var spawn = GenerateFormationSpawn(formation, chance, _latestLabsPreBlackoutEventActive ? mainConfig.labsContainmentBreachEvent : null);
                if (spawn == null)
                    continue;
                if (availableZones.Count > 0)
                {
                    spawn.BossZone = randomUtil.GetArrayValue(availableZones);
                    availableZones.Remove(spawn.BossZone);
                    if (availableZones.Count == 0)
                        availableZones = new List<string>(configuredZones);
                }

                var delay = GetSpawnTime(formation.raidTimeMin, formation.raidTimeMax);
                if (delay < 0)
                    delay = 0;
                spawn.Time = -1;
                spawn.Delay = delay;
                spawn.TriggerId = kind == SpawnFormationKind.Checkpoint && factionLabel == "Black Division" ? ISB.Compatibility.BlackDivisionCheckpointPolicy.FormationTrigger(triggerId, formations.IndexOf(formation)) : triggerId;
                spawn.TriggerName = "botEvent";
                spawn.ForceSpawn = true;
                spawn.IgnoreMaxBots = !triggerId.StartsWith(LabsChaosTriggerPrefix, StringComparison.Ordinal);
                spawns.Add(spawn);
                logger.Info($"Armed event formation '{formation.id}' for {map}: boss={spawn.BossName}, followers={DescribeFollowers(spawn)}, chance={spawn.BossChance}%, zone='{spawn.BossZone}', time={spawn.Time}s, trigger={spawn.TriggerId}.");
                if (!_labsBlackoutEventSpawnRefs.TryGetValue(map, out var tracked))
                {
                    tracked = new List<BossLocationSpawn>();
                    _labsBlackoutEventSpawnRefs[map] = tracked;
                }

                spawn.SptId = "isb_event_" + spawn.TriggerId + "_" + Guid.NewGuid().ToString("N");
                tracked.Add(spawn);
            }
        }
    }

    private void RemoveTrackedLabsBlackoutEventSpawns(string map, List<BossLocationSpawn> spawns)
    {
        if (!_labsBlackoutEventSpawnRefs.TryGetValue(map, out var tracked) || tracked.Count == 0)
            return;
        spawns.RemoveAll(spawn => tracked.Contains(spawn));
        tracked.Clear();
    }

    private void AddPreBlackoutEventFormations(string map, List<SpawnFormationConfig>? formations, SpawnFormationKind kind, MainConfig mainConfig, List<BossLocationSpawn> spawns, string factionLabel)
    {
        if (formations is not { Count: > 0 })
            return;
        var before = spawns.Count;
        AddConfiguredFormations(map, formations, kind, mainConfig, spawns, factionLabel, true);
        if (!_preBlackoutEventSpawnRefs.TryGetValue(map, out var tracked))
        {
            tracked = new List<BossLocationSpawn>();
            _preBlackoutEventSpawnRefs[map] = tracked;
        }

        for (var i = before; i < spawns.Count; i++)
        {
            spawns[i].SptId = "isb_lockdown_" + kind.ToString().ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N");
            tracked.Add(spawns[i]);
        }
    }

    private void RemoveTrackedPreBlackoutEventSpawns(string map, List<BossLocationSpawn> spawns)
    {
        if (!_preBlackoutEventSpawnRefs.TryGetValue(map, out var tracked) || tracked.Count == 0)
            return;
        spawns.RemoveAll(spawn => tracked.Contains(spawn));
        tracked.Clear();
    }

    private void SuppressNormalBlackDivisionSpawns(string map, List<BossLocationSpawn> spawns)
    {
        var removed = spawns.Where(IsBlackDivisionSpawn).ToList();
        if (removed.Count == 0)
            return;
        if (!_suppressedBlackDivisionSpawns.TryGetValue(map, out var stored))
        {
            stored = new List<BossLocationSpawn>();
            _suppressedBlackDivisionSpawns[map] = stored;
        }

        stored.Clear();
        stored.AddRange(removed);
        spawns.RemoveAll(IsBlackDivisionSpawn);
    }

    private void RestoreSuppressedBlackDivisionSpawns(string map, List<BossLocationSpawn> spawns)
    {
        if (!_suppressedBlackDivisionSpawns.TryGetValue(map, out var stored) || stored.Count == 0)
            return;
        foreach (var spawn in stored)
        {
            if (!spawns.Contains(spawn))
                spawns.Add(spawn);
        }

        stored.Clear();
    }

    private void SuppressNormalPmcSpawns(string map, List<BossLocationSpawn> spawns)
    {
        SuppressSpawns(map, spawns, _suppressedPmcSpawns, IsPmcSpawn, "PMC");
    }

    private void RestoreSuppressedPmcSpawns(string map, List<BossLocationSpawn> spawns)
    {
        RestoreSuppressedSpawns(map, spawns, _suppressedPmcSpawns);
    }

    private void SuppressNormalRaiderSpawns(string map, List<BossLocationSpawn> spawns)
    {
        SuppressSpawns(map, spawns, _suppressedRaiderSpawns, IsRaiderSpawn, "Raider");
    }

    private void RestoreSuppressedRaiderSpawns(string map, List<BossLocationSpawn> spawns)
    {
        RestoreSuppressedSpawns(map, spawns, _suppressedRaiderSpawns);
    }

    private void SuppressSpawns(string map, List<BossLocationSpawn> spawns, Dictionary<string, List<BossLocationSpawn>> storage, Func<BossLocationSpawn, bool> predicate, string label)
    {
        var removed = spawns.Where(predicate).ToList();
        if (removed.Count == 0)
            return;
        if (!storage.TryGetValue(map, out var stored))
        {
            stored = new List<BossLocationSpawn>();
            storage[map] = stored;
        }

        stored.Clear();
        stored.AddRange(removed);
        spawns.RemoveAll(spawn => predicate(spawn));
    }

    private static void RestoreSuppressedSpawns(string map, List<BossLocationSpawn> spawns, Dictionary<string, List<BossLocationSpawn>> storage)
    {
        if (!storage.TryGetValue(map, out var stored) || stored.Count == 0)
            return;
        foreach (var spawn in stored)
        {
            if (!spawns.Contains(spawn))
                spawns.Add(spawn);
        }

        stored.Clear();
    }

    private static bool IsPmcSpawn(BossLocationSpawn spawn)
    {
        if (spawn is null || string.IsNullOrWhiteSpace(spawn.BossName))
            return false;
        return string.Equals(spawn.BossName, "pmcUSEC", StringComparison.OrdinalIgnoreCase) || string.Equals(spawn.BossName, "pmcBEAR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRaiderSpawn(BossLocationSpawn spawn)
    {
        return spawn is not null && !string.IsNullOrWhiteSpace(spawn.BossName) && string.Equals(spawn.BossName, "pmcBot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlackDivisionSpawn(BossLocationSpawn spawn)
    {
        if (spawn is null)
            return false;
        if (!string.IsNullOrWhiteSpace(spawn.BossName) && spawn.BossName.Contains("blackDiv", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(spawn.BossEscortType) && spawn.BossEscortType.Contains("blackDiv", StringComparison.OrdinalIgnoreCase))
            return true;
        return spawn.Supports?.Any(support => !string.IsNullOrWhiteSpace(support.BossEscortType) && support.BossEscortType.Contains("blackDiv", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool IsManagedISBSpawn(BossLocationSpawn spawn)
    {
        if (!string.IsNullOrEmpty(spawn.BossName) && spawn.BossName.StartsWith("ISB", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(spawn.TriggerId) && (spawn.TriggerId.StartsWith(SecretSpawnTriggerPrefix, StringComparison.Ordinal) || spawn.TriggerId.StartsWith(LabsBlackoutTriggerPrefix, StringComparison.Ordinal) || spawn.TriggerId.StartsWith(LabsChaosTriggerPrefix, StringComparison.Ordinal) || spawn.TriggerId.StartsWith(LabsPowerRestoredTriggerPrefix, StringComparison.Ordinal));
    }

    private static void AddOrReplaceSecretSpawn(List<BossLocationSpawn> spawns, BossLocationSpawn spawn)
    {
        if (string.IsNullOrEmpty(spawn.TriggerId) || !spawn.TriggerId.StartsWith(SecretSpawnTriggerPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Secret ISB spawns must use a TriggerId beginning with '{SecretSpawnTriggerPrefix}'.", nameof(spawn));
        }

        spawns.RemoveAll(existing => string.Equals(existing.TriggerId, spawn.TriggerId, StringComparison.Ordinal));
        spawns.Add(spawn);
    }

    private void AdjustPatrolSpawnsForMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        logger.Info($"Enabling ISB patrols for {map}.");
        var patrolConfig = mapConfig.patrol;
        if (patrolConfig.formations is { Count: > 0 })
        {
            AddConfiguredFormations(map, patrolConfig.formations, SpawnFormationKind.Patrol, mainConfig, spawns, drawLimit: patrolConfig.PatrolDrawLimit, spawnChance: FormationSpawnChance.Resolve(patrolConfig.spawnChance, patrolConfig.formations, patrolConfig.patrolChance));
            return;
        }
        if (patrolConfig.spawnChance.HasValue)
            return;

        var validZones = new List<string>(patrolConfig.mapZones);
        for (int i = 0; i < patrolConfig.squadCount; i++)
        {
            var patrolSize = randomUtil.GetInt(patrolConfig.patrolMinSize, patrolConfig.patrolMaxSize);
            var patrol = GeneratePatrol(patrolSize, mainConfig.debug.spawnAlways ? 100 : patrolConfig.patrolChance);
            patrol.BossZone = randomUtil.GetArrayValue(validZones);
            validZones.Remove(patrol.BossZone);
            if (validZones.Count == 0)
                validZones = new List<string>(patrolConfig.mapZones);
            patrol.Time = GetSpawnTime(patrolConfig.raidTimeMin, patrolConfig.raidTimeMax);
            if (mainConfig.debug.spawnInstantlyAlways)
            {
                logger.Info($"Instantly spawning ISB patrol for {map}.");
                patrol.Time = -1;
            }

            spawns.Add(patrol);
            logger.Info($"Added ({patrolConfig.patrolChance}% chance) ISB patrol of size {patrolSize} to {map} in zone {patrol.BossZone} with a spawn time of {patrol.Time} seconds.");
        }
    }

    private void AdjustCheckpointSpawnsForMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        logger.Info($"Enabling ISB checkpoint for {map}.");
        var checkpointConfig = mapConfig.checkpoint;
        if (checkpointConfig.formations is { Count: > 0 })
        {
            AddConfiguredFormations(map, checkpointConfig.formations, SpawnFormationKind.Checkpoint, mainConfig, spawns, drawLimit: checkpointConfig.CheckpointDrawLimit, spawnChance: FormationSpawnChance.Resolve(checkpointConfig.spawnChance, checkpointConfig.formations));
            return;
        }

        if (checkpointConfig.spawnChance.HasValue)
            return;

        var validZones = new List<ZoneCheckpointConfig>(checkpointConfig.holdMapZones);
        for (int i = 0; i < checkpointConfig.holdZonesCount; i++)
        {
            var checkpointZoneConfig = randomUtil.GetArrayValue(validZones);
            validZones.Remove(checkpointZoneConfig);
            var patrolSize = randomUtil.GetInt(checkpointZoneConfig.checkpointMin, checkpointZoneConfig.checkpointMax);
            var patrol = GeneratePatrol(patrolSize, mainConfig.debug.spawnAlways ? 100 : checkpointZoneConfig.checkpointChance, false);
            patrol.BossZone = checkpointZoneConfig.checkpointZone;
            if (validZones.Count == 0)
                validZones = [..checkpointConfig.holdMapZones];
            patrol.Time = GetSpawnTime(checkpointConfig.raidTimeMin, checkpointConfig.raidTimeMax);
            if (mainConfig.debug.spawnInstantlyAlways)
            {
                logger.Info($"Instantly spawning ISB checkpoint for {map}.");
                patrol.Time = -1;
            }

            spawns.Add(patrol);
            logger.Info($"Added ({checkpointZoneConfig.checkpointChance}% chance) ISB checkpoint of size {patrolSize} to {map} in zone {patrol.BossZone} with a spawn time of {patrol.Time} seconds.");
        }
    }

    private void AddConfiguredFormations(string map, List<SpawnFormationConfig> formations, SpawnFormationKind kind, MainConfig mainConfig, List<BossLocationSpawn> spawns, string factionLabel = "ISB", bool containment = false, int drawLimit = -1, float? spawnChance = null)
    {
        var eligible = formations.Where(f => f?.enabled == true && (kind != SpawnFormationKind.Checkpoint || f.zones?.Any(z => !string.IsNullOrWhiteSpace(z)) == true));
        var activeFormations = WeightedFormationDraw.Select(eligible, containment ? -1 : drawLimit, Random.Shared.NextDouble);
        if (activeFormations.Count == 0)
        {
            return;
        }

        foreach (var formation in activeFormations)
        {
            var squadCount = Math.Max(1, formation.squadCount);
            var validZones = (formation.zones ?? new List<string>()).Where(zone => !string.IsNullOrWhiteSpace(zone)).Select(zone => zone.Trim()).ToList();
            if (kind == SpawnFormationKind.Checkpoint && validZones.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < squadCount; i++)
            {
                var chance = !containment && mainConfig.debug.spawnAlways ? 100f : Math.Max(0f, Math.Min(100f, spawnChance ?? formation.chance));
                var spawn = GenerateFormationSpawn(formation, chance, containment ? mainConfig.labsContainmentBreachEvent : null);
                if (spawn == null)
                    continue;
                if (containment)
                    spawn.IgnoreMaxBots = false;
                if (validZones.Count > 0)
                {
                    spawn.BossZone = randomUtil.GetArrayValue(validZones);
                    validZones.Remove(spawn.BossZone);
                    if (validZones.Count == 0)
                    {
                        validZones = (formation.zones ?? new List<string>()).Where(zone => !string.IsNullOrWhiteSpace(zone)).Select(zone => zone.Trim()).ToList();
                    }
                }

                switch (kind)
                {
                    case SpawnFormationKind.Patrol:
                    case SpawnFormationKind.Checkpoint:
                        spawn.Time = GetSpawnTime(formation.raidTimeMin, formation.raidTimeMax);
                        if (!containment && mainConfig.debug.spawnInstantlyAlways)
                            spawn.Time = -1;
                        break;
                    case SpawnFormationKind.Hunt:
                        spawn.Time = GetSpawnTime(formation.raidTimeMin, formation.raidTimeMax);
                        if (!containment && mainConfig.debug.spawnInstantlyAlways)
                            spawn.Time = -1;
                        spawn.TriggerId = "hunt";
                        spawn.TriggerName = string.Empty;
                        spawn.ForceSpawn = true;
                        break;
                }

                if (!containment && kind == SpawnFormationKind.Checkpoint)
                {
                    spawn.TriggerId = "isb_config_checkpoint_" + formations.IndexOf(formation).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    spawn.TriggerName = string.Empty;
                }

                if (containment && kind == SpawnFormationKind.Checkpoint && factionLabel == "ISB")
                {
                    spawn.TriggerId = "isb_labs_containment_checkpoint_isbcp_" + formations.IndexOf(formation).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    spawn.TriggerName = string.Empty;
                }

                if (kind == SpawnFormationKind.Checkpoint && factionLabel == "Black Division")
                {
                    spawn.TriggerId = ISB.Compatibility.BlackDivisionCheckpointPolicy.FormationTrigger(ISB.Compatibility.BlackDivisionCheckpointPolicy.ContainmentTrigger, formations.IndexOf(formation));
                    spawn.TriggerName = string.Empty;
                }

                spawns.Add(spawn);
                if (logger.Enabled)
                    logger.Info($"Added {factionLabel} {kind} '{formation.id}' ({i + 1}/{squadCount}) to {map}: boss={spawn.BossName}, followers={DescribeFollowers(spawn)}, chance={spawn.BossChance}%, zone='{spawn.BossZone}', time={spawn.Time}s, event={containment}.");
            }
        }
    }

    private static string DescribeFollowers(BossLocationSpawn spawn) => spawn.Supports == null ? spawn.BossEscortAmount ?? "0" : string.Join(",", spawn.Supports.Select(s => s.BossEscortType + ":" + s.BossEscortAmount));
    private int GetSpawnTime(int min, int max)
    {
        if (min < 0 && max < 0)
            return -1;
        if (min < 0)
            min = max;
        if (max < 0)
            max = min;
        if (min > max)
            (min, max) = (max, min);
        return randomUtil.GetInt(min, max);
    }

    private bool _ghostSpawnReported;
    private BossLocationSpawn? GenerateFormationSpawn(SpawnFormationConfig formation, float chance, LabsPreBlackoutEventConfig? modes = null)
    {
        bool ghost = GhostBotVariant.Is(formation.boss?.Trim()) || formation.followers?.Any(f => f != null && f.max > 0 && GhostBotVariant.Is(f.type?.Trim())) == true;
        if (ghost && GhostBotGenerationPatch.PresetsLoaded && (GhostBotVariant.Is(formation.boss?.Trim()) && !GhostBotGenerationPatch.IsReady(formation.boss.Trim(), CurrentRaidIsNight()) || formation.followers?.Any(f => f != null && f.max > 0 && GhostBotVariant.Is(f.type?.Trim()) && !GhostBotGenerationPatch.IsReady(f.type.Trim(), CurrentRaidIsNight())) == true))
        {
            if (!_ghostSpawnReported)
                logger.Error("ISB: GhostBuster formation skipped; add valid presets to its Day/Night folders and ensure Black Division is installed.");
            _ghostSpawnReported = true;
            return null;
        }

        var bossType = string.IsNullOrWhiteSpace(formation.boss) ? "ISBTeamLeader" : GhostBotVariant.Is(formation.boss.Trim()) ? GhostBotVariant.BaseRole(formation.boss.Trim()) : formation.boss.Trim();
        var supports = new List<BossSupport>();
        foreach (var follower in formation.followers ?? new List<SpawnFormationFollowerConfig>())
        {
            if (follower is null || string.IsNullOrWhiteSpace(follower.type))
                continue;
            var min = Math.Max(0, follower.min);
            var max = Math.Max(0, follower.max);
            if (min > max)
                (min, max) = (max, min);
            var amount = min == max ? min : randomUtil.GetInt(min, max);
            if (amount <= 0)
                continue;
            supports.Add(new BossSupport { BossEscortAmount = amount.ToString(), BossEscortDifficulty = new ListOrT<string>([GhostBotVariant.Is(follower.type.Trim()) ? GhostBotVariant.Marker(follower.type.Trim()) : "impossible"], null), BossEscortType = GhostBotVariant.Is(follower.type.Trim()) ? GhostBotVariant.BaseRole(follower.type.Trim()) : follower.type.Trim() });
        }

        var primaryEscortType = supports.Count > 0 ? supports[0].BossEscortType : bossType;
        return new BossLocationSpawn
        {
            BossChance = chance,
            BossDifficulty = GhostBotVariant.Is(formation.boss?.Trim()) ? GhostBotVariant.Marker(formation.boss.Trim()) : "impossible",
            BossEscortAmount = "0",
            BossEscortDifficulty = supports.Count > 0 && GhostBotVariant.Is(formation.followers?.FirstOrDefault(f => f != null && f.max > 0)?.type?.Trim()) ? GhostBotVariant.Marker(formation.followers.First(f => f != null && f.max > 0).type.Trim()) : "impossible",
            BossEscortType = primaryEscortType,
            BossName = bossType,
            IsBossPlayer = false,
            BossZone = string.Empty,
            ForceSpawn = false,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = new[]
            {
                "regular",
                "pve"
            },
            Supports = supports,
            Time = -1,
            TriggerId = string.Empty,
            TriggerName = string.Empty
        };
    }

    private void AdjustHuntSpawnsForMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        if (mapConfig.hunt?.formations is { Count: > 0 })
        {
            AddConfiguredFormations(map, mapConfig.hunt.formations, SpawnFormationKind.Hunt, mainConfig, spawns, drawLimit: mapConfig.hunt.HuntDrawLimit, spawnChance: FormationSpawnChance.Resolve(mapConfig.hunt.spawnChance, mapConfig.hunt.formations));
            return;
        }

        if (mapConfig.hunt?.spawnChance.HasValue == true)
            return;

        if (mapConfig.hunt?.hunts is not { Count: > 0 })
        {
            logger.Warn($"Hunt enabled for {map} but no hunts are configured. Skipping.");
            return;
        }

        spawns.RemoveAll(x => x.TriggerId == "hunt" && x.BossName == "ISBTeamLeader");
        if (mapConfig.hunt.hunts.ContainsKey("ISB"))
        {
            AddISBHuntToMap(map, mapConfig, mainConfig, spawns);
        }

        if (mapConfig.hunt.hunts.ContainsKey("ISB2"))
        {
            AddISBSecondHuntToMap(map, mapConfig, mainConfig, spawns);
        }
    }

    private void AddISBHuntToMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        var huntConfig = mapConfig.hunt.hunts["ISB"];
        logger.Info($"Enabling ISB hunt for {map}.");
        var patrolSize = randomUtil.GetInt(huntConfig.huntMin, huntConfig.huntMax);
        var chance = mainConfig.debug.spawnAlways ? 100 : huntConfig.huntChance;
        var patrol = GeneratePatrol(patrolSize, chance, false);
        patrol.Time = GetSpawnTime(huntConfig.raidTimeMin, huntConfig.raidTimeMax);
        if (mainConfig.debug.spawnInstantlyAlways)
            patrol.Time = -1;
        patrol.BossZone = huntConfig.huntZones ?? string.Empty;
        patrol.TriggerName = string.Empty;
        patrol.TriggerId = "hunt";
        patrol.ForceSpawn = true;
        spawns.Add(patrol);
        logger.Info($"Added ({chance}% chance) ISB Hunt of size {patrolSize} to {map} in zone '{patrol.BossZone}' with a spawn time of {patrol.Time} seconds.");
    }

    private void AddISBSecondHuntToMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        var huntConfig = mapConfig.hunt.hunts["ISB2"];
        logger.Info($"Enabling ISB second hunt for {map}.");
        var patrolSize = randomUtil.GetInt(huntConfig.huntMin, huntConfig.huntMax);
        var chance = mainConfig.debug.spawnAlways ? 100 : huntConfig.huntChance;
        var patrol = new BossLocationSpawn
        {
            BossChance = chance,
            BossDifficulty = "impossible",
            BossEscortAmount = patrolSize.ToString(),
            BossEscortDifficulty = "impossible",
            BossEscortType = "ISBSpecialForces",
            BossName = "ISBTeamLeader",
            IsBossPlayer = false,
            BossZone = huntConfig.huntZones ?? string.Empty,
            ForceSpawn = true,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = new[]
            {
                "regular",
                "pve"
            },
            Supports = new List<BossSupport>(),
            Time = mainConfig.debug.spawnInstantlyAlways ? -1 : GetSpawnTime(huntConfig.raidTimeMin, huntConfig.raidTimeMax),
            TriggerId = "hunt",
            TriggerName = string.Empty
        };
        spawns.Add(patrol);
        logger.Info($"Added ({chance}% chance) ISB Second Hunt of size {patrolSize} to {map} in zone '{patrol.BossZone}' with a spawn time of {patrol.Time} seconds.");
    }

    private void AddISBSecretSpawns(string map, List<BossLocationSpawn> spawns)
    {
        if (map == "Lighthouse")
        {
            var secret = new BossLocationSpawn
            {
                BossName = "ISBBossCommander",
                BossChance = 100,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = "Zone_Blockpost",
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[]
                {
                    "regular",
                    "pve"
                },
                Supports = new List<BossSupport>(),
                Time = -1,
                Delay = 6,
                TriggerId = "isb_secret_expurgo_farol_hunt",
                TriggerName = "botEvent"
            };
            AddOrReplaceSecretSpawn(spawns, secret);
        }

        var bdClash = new BossLocationSpawn
        {
            BossName = "ISBBossCommander",
            BossChance = 100,
            BossDifficulty = "impossible",
            BossEscortType = "ISBSecondLeader",
            BossEscortAmount = "6",
            BossEscortDifficulty = "impossible",
            IsBossPlayer = false,
            BossZone = string.Empty,
            ForceSpawn = true,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = new[]
            {
                "regular",
                "pve"
            },
            Supports = new List<BossSupport>(),
            Time = -1,
            Delay = 8,
            TriggerId = "isb_secret_bd_clash_hunt",
            TriggerName = "botEvent"
        };
        AddOrReplaceSecretSpawn(spawns, bdClash);
        if (InstalledModGuids.Contains("com.blackdiv.tacticaltoaster"))
        {
            var counterattackSize = randomUtil.GetInt(5, 6);
            var bdCounterattack = new BossLocationSpawn
            {
                BossName = "blackDivAssault",
                BossChance = 100,
                BossDifficulty = "impossible",
                BossEscortType = "blackDivAssault",
                BossEscortAmount = (counterattackSize - 1).ToString(),
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = string.Empty,
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[]
                {
                    "regular",
                    "pve"
                },
                Supports = new List<BossSupport>(),
                Time = -1,
                Delay = 5,
                TriggerId = "isb_secret_bd_counterattack",
                TriggerName = "botEvent"
            };
            AddOrReplaceSecretSpawn(spawns, bdCounterattack);
            logger.Info($"Added optional Black Division Counterattack secret with {counterattackSize} blackDivAssault bot(s) to {map}.");
        }

        {
            var massacre = new BossLocationSpawn
            {
                BossName = "ISBBossCommander",
                BossChance = 100,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = string.Empty,
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[]
                {
                    "regular",
                    "pve"
                },
                Supports = new List<BossSupport>(),
                Time = -1,
                Delay = 6,
                TriggerId = "isb_secret_massacre_hunt",
                TriggerName = "botEvent"
            };
            AddOrReplaceSecretSpawn(spawns, massacre);
        }

        if (map == "Shoreline")
        {
            var relic = new BossLocationSpawn
            {
                BossName = "ISBBossCommander",
                BossChance = 100,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = string.Empty,
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[]
                {
                    "regular",
                    "pve"
                },
                Supports = new List<BossSupport>(),
                Time = -1,
                Delay = 6,
                TriggerId = "isb_secret_relic_hunt",
                TriggerName = "botEvent"
            };
            AddOrReplaceSecretSpawn(spawns, relic);
        }

        if (map == "Shoreline")
        {
            var local = new BossLocationSpawn
            {
                BossName = "ISBBossCommander",
                BossChance = 100,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = "ZoneMeteoStation",
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[]
                {
                    "regular",
                    "pve"
                },
                Supports = new List<BossSupport>(),
                Time = -1,
                Delay = 6,
                TriggerId = "isb_secret_local_hunt",
                TriggerName = "botEvent"
            };
            AddOrReplaceSecretSpawn(spawns, local);
        }
    }

    private BossLocationSpawn GeneratePatrol(int patrolSize, float chance, bool isPatrol = true)
    {
        var regularAmount = Math.Max(1, patrolSize);
        logger.Info($"Generating ISB patrol of size {patrolSize} (1 Boss, 2 Specialists, {regularAmount} Regular ISB PMC).");
        var supportsList = new List<BossSupport>
        {
            new BossSupport
            {
                BossEscortAmount = "2",
                BossEscortDifficulty = new ListOrT<string>(["impossible"], null),
                BossEscortType = "ISBSecondLeader"
            },
            new BossSupport
            {
                BossEscortAmount = regularAmount.ToString(),
                BossEscortDifficulty = new ListOrT<string>(["impossible"], null),
                BossEscortType = "ISBSpecialForces"
            }
        };
        return new BossLocationSpawn
        {
            BossChance = chance,
            BossDifficulty = "impossible",
            BossEscortAmount = "0",
            BossEscortDifficulty = "impossible",
            BossEscortType = "ISBSpecialForces",
            BossName = "ISBTeamLeader",
            IsBossPlayer = false,
            BossZone = string.Empty,
            ForceSpawn = false,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = new[]
            {
                "regular",
                "pve"
            },
            Supports = supportsList,
            Time = -1,
            TriggerId = string.Empty,
            TriggerName = string.Empty
        };
    }
}
