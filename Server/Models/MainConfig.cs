namespace ISBSpecialForcesServer;
public class MainConfig
{
    public bool diagnosticLogs { get; set; } = false;
    public int maxBotPresetBatch { get; set; } = 25;
    public DebugConfig debug { get; set; }
    public Dictionary<string, MapConfig> locations { get; set; }
    public DayNightConfig dayNight { get; set; } = new();
    public AishiLoyaltyConditionConfig aishiLoyaltyCondition { get; set; } = new();
    public RevengeConfig revenge { get; set; } = new();
    public ForcedHostilityConfig forcedHostility { get; set; } = new();
    public DogtagConfig dogtags { get; set; } = new();
    public ContainmentPocketLootConfig blackDivisionLoot { get; set; } = new();
    public BotPresetConfig botPresets { get; set; } = new();
    public BotPresetConfig blackDivisionBotPresets { get; set; } = new();
    public LabsPreBlackoutEventConfig labsContainmentBreachEvent { get; set; } = new();
    public LabsBlackoutEventConfig labsBlackoutEvent { get; set; } = new();
    public bool enableCollabLoadoutsAndAppearances { get; set; } = true;
    public Dictionary<string, bool> collabLoadouts { get; set; } = new();
    public bool enableCollabPresets { get; set; } = true;
    public Dictionary<string, bool> collabPresets { get; set; } = new();

    public bool IsCollabLoadoutEnabled(string folder)
    {
        if (!enableCollabLoadoutsAndAppearances)
            return false;
        return IsCollabEntryEnabled(collabLoadouts, folder);
    }

    public bool IsCollabPresetEnabled(string folder)
    {
        if (!enableCollabPresets)
            return false;
        return IsCollabEntryEnabled(collabPresets, folder);
    }

    private static bool IsCollabEntryEnabled(Dictionary<string, bool> entries, string folder)
    {
        if (entries is null || entries.Count == 0)
            return true;
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Key, folder, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return true;
    }
}

public class DogtagConfig
{
    public bool enabled { get; set; } = true;
    public string displaySide { get; set; } = "ISB";
    public string internalSide { get; set; } = "Usec";
    public bool populateIdentityData { get; set; } = true;
    public Dictionary<string, string> dogtagTplByWildType { get; set; } = new()
    {
        {
            "ISBSpecialForces",
            ""
        },
        {
            "ISBBossFirefly",
            ""
        },
        {
            "ISBFollowerVipper",
            ""
        },
        {
            "ISBFollowerLoghan",
            ""
        },
        {
            "ISBFollowerSpearthrower",
            ""
        },
        {
            "ISBBossCommander",
            ""
        },
        {
            "ISBFollowerCommander",
            ""
        },
        {
            "ISBTeamLeader",
            ""
        },
        {
            "ISBSecondLeader",
            ""
        },
        {
            "ISBScout",
            ""
        },
        {
            "ISBHeavy",
            ""
        },
        {
            "ISBFollowerZofia",
            ""
        },
        {
            "ISBAssassin",
            ""
        },
        {
            "ISBMarksman",
            ""
        },
        {
            "ISBFollowerEvernight",
            ""
        },
        {
            "ISBFollowerNemesis",
            ""
        }
    };

    public bool TryGetTemplate(string role, out string templateId)
    {
        templateId = null;
        if (string.IsNullOrWhiteSpace(role) || dogtagTplByWildType is null)
            return false;
        var lookupRole = role;
        if (string.Equals(role, "13701", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBSpecialForces";
        else if (string.Equals(role, "13702", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBBossFirefly";
        else if (string.Equals(role, "13703", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerVipper";
        else if (string.Equals(role, "13704", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerLoghan";
        else if (string.Equals(role, "13705", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerSpearthrower";
        else if (string.Equals(role, "13707", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBBossCommander";
        else if (string.Equals(role, "13708", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerCommander";
        else if (string.Equals(role, "13709", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBTeamLeader";
        else if (string.Equals(role, "13710", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBSecondLeader";
        else if (string.Equals(role, "13711", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBScout";
        else if (string.Equals(role, "13712", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBHeavy";
        else if (string.Equals(role, "13706", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerZofia";
        else if (string.Equals(role, "13713", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBAssassin";
        else if (string.Equals(role, "13714", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBMarksman";
        else if (string.Equals(role, "13715", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerEvernight";
        else if (string.Equals(role, "13716", StringComparison.OrdinalIgnoreCase))
            lookupRole = "ISBFollowerNemesis";
        foreach (var entry in dogtagTplByWildType)
        {
            if (!string.Equals(entry.Key, lookupRole, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(entry.Value))
                return false;
            templateId = entry.Value.Trim();
            return true;
        }

        return false;
    }
}

public class RevengeConfig
{
    public bool enabled { get; set; } = true;
    public bool logs { get; set; } = false;
    public int raidAmount { get; set; } = 5;
    public bool refreshOnAdditionalKill { get; set; } = true;
    public bool stackAdditionalKills { get; set; } = false;
    public bool consumeOnScavRaid { get; set; } = false;
}

public class ForcedHostilityConfig
{
    public bool enabled { get; set; } = true;
    public bool logs { get; set; } = false;
    public List<string> friendlyBotTypes { get; set; } = new()
    {
        "exUsec"
    };
}

public class AishiLoyaltyConditionConfig
{
    public bool enabled { get; set; } = true;
    public bool logs { get; set; } = false;
    public string requiredItemTpl { get; set; } = "6aa5ff857a930fba7793141b";
    public bool breakOnHit { get; set; } = true;
    public float damageThreshold { get; set; } = 300f;
    public bool thresholdPerFaction { get; set; } = false;
    public bool loseReputationOnKill { get; set; } = true;
    public float reputationLostPerKill { get; set; } = 0.05f;
    public bool showReputationLossInRaidResults { get; set; } = true;
    public string reputationMessage { get; set; } = "I appreciate loyalty very much. You hurt my people for no reason. Don't expect me to understand this or let it slide, because they won't understand it either—and they certainly won't go easy on you. You're going to lose points with me, so don't push my patience too far.";
    public bool extendToPlayerAllies { get; set; } = true;
    public FriendlyConditionsConfig friendlyConditions { get; set; } = new();
}

public class FriendlyConditionsConfig
{
    public string mode { get; set; } = "any";
    public QuestTruceConditionsConfig questTruce { get; set; } = new();
}

public class QuestTruceConditionsConfig
{
    public bool enabled { get; set; } = false;
    public bool logs { get; set; } = false;
    public List<QuestTruceRuleConfig> conditions { get; set; } = new();
}

public class QuestTruceRuleConfig
{
    public bool resetHostilityEachRaid { get; set; } = false;
    public string id { get; set; } = string.Empty;
    public bool enabled { get; set; } = true;
    public string questId { get; set; } = string.Empty;
    public List<string> maps { get; set; } = new();
    public int? questStatus { get; set; }
    public List<int> questStatuses { get; set; } = new();
    public QuestTruceDurationConfig truceDuration { get; set; } = new();
    public bool consumeOnScavRaid { get; set; } = false;
    public bool consumeOnlyOnISBEligibleMaps { get; set; } = false;
    public bool activateIfAlreadyMatched { get; set; } = true;

    public bool AppliesToMap(string? locationId)
    {
        if (maps == null || maps.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(locationId))
            return false;
        return maps.Any(map => !string.IsNullOrWhiteSpace(map) && string.Equals(map.Trim(), locationId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public class QuestTruceDurationConfig
{
    public string mode { get; set; } = "whileStatusMatches";
    public int raids { get; set; } = 1;
}

public class DayNightConfig
{
    public bool logs { get; set; } = false;
    public bool ForceDayAppearance { get; set; } = false;
    public bool ForceNightAppearance { get; set; } = false;
    public int nightStartHour { get; set; } = 21;
    public int nightEndHour { get; set; } = 6;
}

public class DebugConfig
{
    public bool logs { get; set; }
    public bool spawnAlways { get; set; }
    public bool spawnInstantlyAlways { get; set; }
}

public class SpawnFormationFollowerConfig
{
    public string type { get; set; } = "ISBSpecialForces";
    public int min { get; set; } = 1;
    public int max { get; set; } = 1;
}

public class SpawnFormationConfig
{
    public string id { get; set; } = "formation";
    public double weight { get; set; } = 1d;
    public bool enabled { get; set; } = true;
    public float chance { get; set; } = 100;
    public int squadCount { get; set; } = 1;
    public string boss { get; set; } = "ISBTeamLeader";
    public List<SpawnFormationFollowerConfig> followers { get; set; } = new();
    public List<string> zones { get; set; } = new();
    public int raidTimeMin { get; set; } = -1;
    public int raidTimeMax { get; set; } = -1;
    public float checkpointRadius { get; set; } = 20f;
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class LabsPreBlackoutEventConfig
{
    public bool whiteTuskOnly { get; set; }
    public bool performanceMode { get; set; }
    public bool ghostBusterOnly { get; set; }
    public bool noISBMode { get; set; }
    public bool noBlackDivMode { get; set; }
    public bool labsVariantsEnabled { get; set; } = true;
    public string containmentBreachLastQuest { get; set; } = "8e4f5535e0bf4135b8a90ec7";
    public double isbHealthMultiplier { get; set; } = 0.6;
    public double blackDivisionHealthMultiplier { get; set; } = 1.25;
    public ISB.Compatibility.LabsOfficeConfig officeAccess { get; set; } = new();
    public bool disableFire { get; set; }
    public bool disableSirens { get; set; }
    public bool disableDestroyedVehicles { get; set; }
    public LabsBlackoutEventPhaseConfig labsChaos { get; set; } = new();
    public bool enabled { get; set; } = true;
    public float chance { get; set; } = 15f;
    public LabsQuestForceConditionsConfig questForceConditions { get; set; } = new();
    public bool disableNormalISBSpawnsDuringEvent { get; set; } = true;
    public bool disableNormalBlackDivisionSpawnsDuringEvent { get; set; } = true;
    public LabsPreBlackoutPopulationControlConfig populationControl { get; set; } = new();
    public ContainmentPocketLootConfig blackDivisionPocketLoot { get; set; } = new();
    public ContainmentMapLootConfig mapLoot { get; set; } = new();
    public ContainmentMapLootConfig containmentCaseSpawns { get; set; } = new()
    {
        enabled = false
    };
    public LabsPreBlackoutFactionConfig isb { get; set; } = new();
    public LabsPreBlackoutFactionConfig blackDivision { get; set; } = new();
    public LabsPreBlackoutFactionConfig pmcs { get; set; } = new();
    public LabsPreBlackoutFactionConfig raiders { get; set; } = new();
    public AnomalousItemSpawnsConfig anomalousItemSpawns { get; set; } = new();
}

public class LabsQuestForceConditionsConfig
{
    public bool enabled { get; set; } = false;
    public string mode { get; set; } = "any";
    public List<LabsQuestForceRuleConfig> conditions { get; set; } = new();

    public bool ShouldForceEvent(Func<string, int?> resolveQuestStatus)
    {
        if (!enabled || conditions == null)
            return false;
        var requireAll = string.Equals(mode?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
        if (!requireAll && !string.Equals(mode?.Trim(), "any", StringComparison.OrdinalIgnoreCase))
            return false;
        var hasForceMatch = false;
        foreach (var rule in conditions)
        {
            if (rule == null || !rule.enabled || string.IsNullOrWhiteSpace(rule.questId) || !float.IsFinite(rule.eventChance) || rule.eventChance < 0f || rule.eventChance > 100f || (!rule.questStatus.HasValue && (rule.questStatuses == null || rule.questStatuses.Count == 0)))
                continue;
            var status = resolveQuestStatus(rule.questId.Trim());
            var matched = status.HasValue && (rule.questStatus == status.Value || rule.questStatuses?.Contains(status.Value) == true);
            if (requireAll && !matched)
                return false;
            if (matched && rule.eventChance == 100f)
            {
                hasForceMatch = true;
                if (!requireAll)
                    return true;
            }
        }

        return hasForceMatch;
    }
}

public class LabsQuestForceRuleConfig
{
    public string id { get; set; } = string.Empty;
    public bool enabled { get; set; } = true;
    public string questId { get; set; } = string.Empty;
    public int? questStatus { get; set; }
    public List<int> questStatuses { get; set; } = new();
    public float eventChance { get; set; } = 100f;
}

public class LabsPreBlackoutPopulationControlConfig
{
    public bool enabled { get; set; } = true;
    public bool disableNormalPMCs { get; set; } = true;
    public bool disableNormalRaiders { get; set; } = true;
}

public class LabsPreBlackoutFactionConfig
{
    public bool enabled { get; set; } = true;
    public List<SpawnFormationConfig> patrols { get; set; } = new();
    public List<SpawnFormationConfig> checkpoints { get; set; } = new();
    public List<SpawnFormationConfig> hunts { get; set; } = new();
}

public class LabsBlackoutEventConfig
{
    public bool enabled { get; set; } = true;
    public LabsBlackoutEventPhaseConfig blackoutActivated { get; set; } = new();
    public LabsBlackoutEventPhaseConfig powerRestored { get; set; } = new();
}

public class LabsBlackoutEventPhaseConfig
{
    public bool enabled { get; set; } = true;
    public string selectionMode { get; set; } = "preferBlackDivision";
    public LabsPreBlackoutFactionConfig isb { get; set; } = new();
    public LabsPreBlackoutFactionConfig blackDivision { get; set; } = new();
    public LabsPreBlackoutFactionConfig pmcs { get; set; } = new();
    public LabsPreBlackoutFactionConfig raiders { get; set; } = new();
}

public class LabsPreBlackoutEventStatus
{
    public bool whiteTuskOnly { get; set; }
    public bool performanceMode { get; set; }
    public string variant { get; set; } = "legacy";
    public ISB.Compatibility.OfficeAccessState? officeAccess { get; set; }
    public bool active { get; set; }
    public bool blackDivisionInstalled { get; set; }
    public bool labsChaosEnabled { get; set; }
    public bool disableFire { get; set; }
    public bool disableSirens { get; set; }
    public bool disableDestroyedVehicles { get; set; }
}

public class MapPatrolConfig
{
    public int PatrolDrawLimit { get; set; } = -1;
    public float? spawnChance { get; set; }
    public bool EnableISBPatrols { get; set; }
    public float patrolChance { get; set; }
    public int squadCount { get; set; }
    public int patrolMinSize { get; set; }
    public int patrolMaxSize { get; set; }
    public List<string> mapZones { get; set; }
    public int raidTimeMin { get; set; }
    public int raidTimeMax { get; set; }
    public List<SpawnFormationConfig> formations { get; set; } = new();
}

public class MapCheckpointConfig
{
    public int CheckpointDrawLimit { get; set; } = -1;
    public float? spawnChance { get; set; }
    public bool enableISBHoldZones { get; set; }
    public int holdZonesCount { get; set; }
    public List<ZoneCheckpointConfig> holdMapZones { get; set; }
    public int raidTimeMin { get; set; } = -1;
    public int raidTimeMax { get; set; } = -1;
    public List<SpawnFormationConfig> formations { get; set; } = new();
}

public class ZoneCheckpointConfig
{
    public string checkpointZone { get; set; }
    public float checkpointChance { get; set; }
    public int checkpointMin { get; set; }
    public int checkpointMax { get; set; }
    public float checkpointRadius { get; set; }
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class MapHuntConfig
{
    public int HuntDrawLimit { get; set; } = -1;
    public float? spawnChance { get; set; }
    public bool enableHunt { get; set; }
    public Dictionary<string, FactionHuntConfig> hunts { get; set; } = new();
    public List<SpawnFormationConfig> formations { get; set; } = new();
}

public class FactionHuntConfig
{
    public float huntChance { get; set; } = 100;
    public int huntMin { get; set; }
    public int huntMax { get; set; }
    public string huntZones { get; set; } = "";
    public int raidTimeMin { get; set; } = -1;
    public int raidTimeMax { get; set; } = -1;
}

public class MapConfig
{
    public bool enableSecrets { get; set; } = true;
    public MapPatrolConfig patrol { get; set; }
    public MapCheckpointConfig checkpoint { get; set; }
    public MapHuntConfig hunt { get; set; }
}
