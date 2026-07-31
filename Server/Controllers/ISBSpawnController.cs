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
public class ISBSpawnController(
    JsonUtil jsonUtil,
    RandomUtil randomUtil,
    ConfigController configController,
    DatabaseService databaseService,
    ISBLogger logger,
    ModHelper modHelper,
    WeatherHelper weatherHelper,
    TimeUtil timeUtil,
    IReadOnlyList<SptMod> modList,
    HttpResponseUtil httpResponse,
    ISBWeaponPresetController weaponPresetController
)
{

    private const string SecretSpawnTriggerPrefix = "isb_secret_";

    private HashSet<string>? _installedModGuids;

    private HashSet<string> InstalledModGuids
    {
        get
        {
            _installedModGuids ??= modList
                .Select(m => m.ModMetadata.ModGuid)
                .Where(g => !string.IsNullOrEmpty(g))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _installedModGuids;
        }
    }
    private static readonly string[] ISBBotTypes =
    [
        "ISBSpecialForces",
        "ISBBossCommander",
        "ISBFollowerCommander",
        "ISBTeamLeader",
        "ISBSecondLeader"
    ];

    private static readonly Dictionary<string, string[]> ColabFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ArmoryLoadouts",             new[] { "com.wtt.armory" } },
        { "AllinOneLoadouts",           new[] { "com.epicrangetime.aio" } },
        { "ECOLoadouts",                new[] { "com.wtt.ecoattachmentemporium" } },
        { "TrueNorth-Loadouts",         new[] { "com.c11.truenorth4" } },
        { "MassivesoftWeaponsLoadouts", new[] { "com.massivesoft.massivesoftweapons" } },
        { "KS-1Loadouts",               new[] { "com.spy.jsoc-swr" } },
        { "ArtemLoadouts",              new[] { "com.crackbone.artem-wtt" } },
        { "SalcoIndustriesLoadouts",    new[] { "com.salco.salcosarmory", "com.combatarsenal.core" } },
        { "TGCLoadouts",                new[] { "com.emilanderss0n.tgc" } }
    };

    private static float _lastRaidHour = -1f;

    private sealed class BotTypeBaseline
    {
        public string Appearance;
        public string Ammo;
        public string Equipment;
        public string Mods;
    }

    private static readonly Dictionary<string, BotTypeBaseline> _botTypeBaselines = new();

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
        if (configController.ModConfig?.dayNight?.logs == true)
            logger.Warn(message);
    }

    public void SetRaidHourFromConfig(GetRaidConfigurationRequestData? info)
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

            LogDebug($"[ISB] /raid/configuration -> Map={mapId}, TimeVariant={timeVariant}, " +
                        $"baseHour={baseHour}h, finalHour={hour}h => {(IsNightTime() ? "EVERNIGHT" : "EVERYDAY")}");

            ApplyDayNightAppearance();
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] Process /raid/configuration failed: {ex.Message}");
        }
    }

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
        var tables = databaseService.GetTables();

        if (tables?.Bots?.Types is null)
        {
            logger.Warn("[ISB] tables.Bots.Types null, appearance aborted.");
            return;
        }

        var dn = configController.ModConfig?.dayNight;
        var suffix = IsNightTime() ? "Night" : "Day";

        if (dn?.ForceNightAppearance == true)
            LogDebug($"[ISB] ForceNightAppearance active: Forced ISB NIGHT Loadouts.");
        else if (dn?.ForceDayAppearance == true)
            LogDebug($"[ISB] ForceDayAppearance active: Forced ISB DAYTIME Loadouts.");

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

            var key = botType.ToLower();
            if (!tables.Bots.Types.ContainsKey(key))
            {
                logger.Warn($"[ISB] Bot type not found on DB: {key}");
                continue;
            }

            var botType_db = tables.Bots.Types[key];
            if (botType_db is null)
            {
                logger.Warn($"[ISB] Null entry on {key}, skiping.");
                continue;
            }

            RestoreBaseline(key, botType_db, suffix);

            var current = botType_db.BotAppearance;

            var newAppearance = new Appearance
            {
                Body = PickPool(data.body, current?.Body),
                Feet = PickPool(data.feet, current?.Feet),
                Hands = PickPool(data.hands, current?.Hands),
                Head = PickPool(data.head, current?.Head),
                Voice = PickPool(data.voice, current?.Voice),
            };

            botType_db.BotAppearance = newAppearance;

            if (data.equipment is { Count: > 0 })
            {
                ApplyEquipmentOverride(botType_db, data.equipment, key, suffix);
            }

            if (data.ammo is { Count: > 0 })
            {
                ApplyAmmoOverride(botType_db, data.ammo, key, suffix);
            }

            if (data.mods is { Count: > 0 })
            {
                ApplyModsOverride(botType_db, data.mods, key, suffix);
            }

            if (data.mixedAmmo is { Count: > 0 })
            {
                foreach (var caliberEntry in data.mixedAmmo)
                {
                    if (caliberEntry.Value.entries == null || caliberEntry.Value.entries.Count == 0)
                    {
                        logger.Warn($"[ISB] {key} ({suffix}): mixedAmmo '{caliberEntry.Key}' has no entries. Check for PascalCase keys, the parser only accepts lowercase (chance, mode, entries, tpl, ratio).");
                    }
                }

                ISBMixedAmmoState.Merge(key, data.mixedAmmo);
                LogDebug($"[ISB] {key} ({suffix}): mixed ammo registered for {data.mixedAmmo.Count} caliber(s).");
            }

            ApplyColabGear(botType_db, appearanceDir, botType, suffix, key);

            LogDebug($"[ISB] Appearance {suffix} applied to {key} " +
                        $"(body={newAppearance.Body.Count}, feet={newAppearance.Feet.Count}, " +
                        $"hourOfDay={_lastRaidHour:F1}).");
        }

        weaponPresetController.ApplyDefinWeapPreset(IsNightTime());
    }

    private void ApplyColabGear(
        BotType botType_db,
        string appearanceDir,
        string botType,
        string suffix,
        string key)
    {
        var appliedColabs = new List<string>();

        foreach (var (folder, guids) in ColabFolders)
        {
            if (!guids.Any(InstalledModGuids.Contains))
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
            LogDebug($"[ISB] {key} ({suffix}): Mod Loadout active. {appliedColabs.Count} folder(s): " +
                     string.Join(", ", appliedColabs));
    }

    private void ApplyEquipmentOverride(
        BotType botType_db,
        Dictionary<string, Dictionary<string, double>> slotData,
        string key,
        string suffix,
        bool additive = false)
    {
        try
        {
            var equipment = botType_db.BotInventory?.Equipment;
            if (equipment is null)
            {
                LogDebug($"[ISB] {key}: BotInventory.Equipment null, skiping.");
                return;
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
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] {key}: failed to set equipment: {ex.Message}");
        }
    }

    private void ApplyAmmoOverride(
        BotType botType_db,
        Dictionary<string, Dictionary<string, double>> ammoData,
        string key,
        string suffix,
        bool additive = false)
    {
        try
        {
            var ammo = botType_db.BotInventory?.Ammo;
            if (ammo is null)
            {
                LogDebug($"[ISB] {key}: BotInventory.Ammo null, skiping ammo.");
                return;
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
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] {key}: failed to apply ammo: {ex.Message}");
        }
    }

    private void ApplyModsOverride(
        BotType botType_db,
        Dictionary<string, Dictionary<string, List<string>>> modsData,
        string key,
        string suffix,
        bool additive = false)
    {
        try
        {
            var mods = botType_db.BotInventory?.Mods;
            if (mods is null)
            {
                LogDebug($"[ISB] {key}: BotInventory.Mods null, skiping mods.");
                return;
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
                    slotMap = new Dictionary<string, HashSet<MongoId>>();

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
        }
        catch (Exception ex)
        {
            logger.Warn($"[ISB] {key}: failed to apply mods: {ex.Message}");
        }
    }

    private static Dictionary<MongoId, double> PickPool(
        Dictionary<string, double>? overridePool,
        Dictionary<MongoId, double>? currentPool)
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

        if (currentPool is { Count: > 0 })
        {
            foreach (var kvp in currentPool)
                result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    public void AdjustAllISBSpawns()
    {
        try
        {
            ApplyDayNightAppearance();

            var tables = databaseService.GetTables();
            var mainConfig = configController.ModConfig;

            foreach (var map in mainConfig.locations.Keys)
            {
                logger.Info($"Adjusting ISB spawns for {map}.");

                if (!tables.Locations.GetDictionary().ContainsKey(map))
                {
                    logger.Info($"No location data found for {map}. Skipping ISB spawn adjustment.");
                    continue;
                }

                var mapConfig = mainConfig.locations[map];
                var patrolConfig = mapConfig.patrol;
                var checkpointConfig = mapConfig.checkpoint;
                var huntConfig = mapConfig.hunt;
                var location = tables.Locations.GetDictionary()[map];
                var spawns = location.Base.BossLocationSpawn;

                var removedSpawnCount = spawns.RemoveAll(IsManagedISBSpawn);

                if (removedSpawnCount > 0)
                    logger.Info($"Removed {removedSpawnCount} previous ISB-managed spawns from {map}.");

                if (patrolConfig.EnableISBPatrols)
                    AdjustPatrolSpawnsForMap(map, mapConfig, mainConfig, spawns);

                if (checkpointConfig.enableISBHoldZones)
                    AdjustCheckpointSpawnsForMap(map, mapConfig, mainConfig, spawns);

                if (huntConfig?.enableHunt == true)
                    AdjustHuntSpawnsForMap(map, mapConfig, mainConfig, spawns);

                AddISBSecretSpawns(map, spawns);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error adjusting ISB spawns: {ex.Message}");
            throw;
        }
    }

    private static bool IsManagedISBSpawn(BossLocationSpawn spawn)
    {
        if (string.Equals(spawn.BossName, "ISBTeamLeader", StringComparison.Ordinal))
            return true;

        return !string.IsNullOrEmpty(spawn.TriggerId)
            && spawn.TriggerId.StartsWith(SecretSpawnTriggerPrefix, StringComparison.Ordinal);
    }

    private static void AddOrReplaceSecretSpawn(List<BossLocationSpawn> spawns, BossLocationSpawn spawn)
    {
        if (string.IsNullOrEmpty(spawn.TriggerId)
            || !spawn.TriggerId.StartsWith(SecretSpawnTriggerPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Secret ISB spawns must use a TriggerId beginning with '{SecretSpawnTriggerPrefix}'.",
                nameof(spawn));
        }

        spawns.RemoveAll(existing =>
            string.Equals(existing.TriggerId, spawn.TriggerId, StringComparison.Ordinal));

        spawns.Add(spawn);
    }

    private void AdjustPatrolSpawnsForMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
        var patrolConfig = mapConfig.patrol;

        logger.Info($"Enabling ISB patrols for {map}.");
        var validZones = new List<string>(patrolConfig.mapZones);

        for (int i = 0; i < patrolConfig.squadCount; i++)
        {
            var patrolSize = randomUtil.GetInt(patrolConfig.patrolMinSize, patrolConfig.patrolMaxSize);
            var patrol = GeneratePatrol(patrolSize, mainConfig.debug.spawnAlways ? 100 : patrolConfig.patrolChance);

            patrol.BossZone = randomUtil.GetArrayValue(validZones);
            validZones.Remove(patrol.BossZone);

            if (validZones.Count == 0)
                validZones = new List<string>(patrolConfig.mapZones);

            patrol.Time = randomUtil.GetInt(patrolConfig.raidTimeMin, patrolConfig.raidTimeMax);

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
        var checkpointConfig = mapConfig.checkpoint;

        logger.Info($"Enabling ISB checkpoint for {map}.");
        var validZones = new List<ZoneCheckpointConfig>(checkpointConfig.holdMapZones);

        for (int i = 0; i < checkpointConfig.holdZonesCount; i++)
        {
            var checkpointZoneConfig = randomUtil.GetArrayValue(validZones);
            validZones.Remove(checkpointZoneConfig);

            var patrolSize = randomUtil.GetInt(checkpointZoneConfig.checkpointMin, checkpointZoneConfig.checkpointMax);
            var patrol = GeneratePatrol(patrolSize, mainConfig.debug.spawnAlways ? 100 : checkpointZoneConfig.checkpointChance, false);

            patrol.BossZone = checkpointZoneConfig.checkpointZone;

            if (validZones.Count == 0)
                validZones = [.. checkpointConfig.holdMapZones];

            patrol.Time = -1;

            if (mainConfig.debug.spawnInstantlyAlways)
            {
                logger.Info($"Instantly spawning ISB checkpoint for {map}.");
                patrol.Time = -1;
            }

            spawns.Add(patrol);

            logger.Info($"Added ({checkpointZoneConfig.checkpointChance}% chance) ISB checkpoint of size {patrolSize} to {map} in zone {patrol.BossZone} with a spawn time of {patrol.Time} seconds.");
        }
    }

    private void AdjustHuntSpawnsForMap(string map, MapConfig mapConfig, MainConfig mainConfig, List<BossLocationSpawn> spawns)
    {
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
            SpawnMode = new[] { "regular", "pve" },
            Supports = new List<BossSupport>(),
            Time = -1,
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
                BossChance = 20,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = "Zone_Blockpost",
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[] { "regular", "pve" },
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
            BossChance = 15,
            BossDifficulty = "impossible",
            BossEscortType = "ISBSecondLeader",
            BossEscortAmount = "6",
            BossEscortDifficulty = "impossible",
            IsBossPlayer = false,
            BossZone = string.Empty,
            ForceSpawn = true,
            IgnoreMaxBots = true,
            IsRandomTimeSpawn = false,
            SpawnMode = new[] { "regular", "pve" },
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
                SpawnMode = new[] { "regular", "pve" },
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
                BossChance = 25,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = string.Empty,
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[] { "regular", "pve" },
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
                BossChance = 10,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = string.Empty,
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[] { "regular", "pve" },
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
                BossChance = 10,
                BossDifficulty = "impossible",
                BossEscortType = "ISBFollowerCommander",
                BossEscortAmount = "1",
                BossEscortDifficulty = "impossible",
                IsBossPlayer = false,
                BossZone = "ZoneMeteoStation",
                ForceSpawn = true,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,
                SpawnMode = new[] { "regular", "pve" },
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
            SpawnMode = new[] { "regular", "pve" },
            Supports = supportsList,
            Time = -1,
            TriggerId = string.Empty,
            TriggerName = string.Empty
        };
    }
}