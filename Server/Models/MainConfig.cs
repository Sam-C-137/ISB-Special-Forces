namespace ISBSpecialForcesServer;

public class MainConfig
{
    public DebugConfig debug { get; set; }

    public Dictionary<string, MapConfig> locations { get; set; }

    public DayNightConfig dayNight { get; set; } = new();

    public AishiLoyaltyConditionConfig aishiLoyaltyCondition { get; set; } = new();

    public RevengeConfig revenge { get; set; } = new();

    public ForcedHostilityConfig forcedHostility { get; set; } = new();

    public bool enableCollabLoadoutsAndAppearances { get; set; } = true;
}

public class RevengeConfig
{
    public bool enabled { get; set; } = true;

    public bool logs { get; set; } = true;

    public int raidAmount { get; set; } = 5;

    public bool refreshOnAdditionalKill { get; set; } = true;

    public bool stackAdditionalKills { get; set; } = false;

    public bool consumeOnScavRaid { get; set; } = false;
}

public class ForcedHostilityConfig
{
    public bool enabled { get; set; } = true;

    public bool logs { get; set; } = false;

    public List<string> friendlyBotTypes { get; set; } = new() { "exUsec" };
}

public class AishiLoyaltyConditionConfig
{
    public bool enabled { get; set; } = true;

    public bool logs { get; set; } = false;

    public int minLoyaltyLevel { get; set; } = 4;

    public bool breakOnHit { get; set; } = true;

    public float damageThreshold { get; set; } = 300f;

    public bool thresholdPerFaction { get; set; } = false;

    public bool loseReputationOnKill { get; set; } = true;

    public float reputationLostPerKill { get; set; } = 0.05f;

    public bool showReputationLossInRaidResults { get; set; } = true;

    public string reputationMessage { get; set; } = "I appreciate loyalty very much. You hurt my people for no reason. Don't expect me to understand this or let it slide, because they won't understand it either—and they certainly won't go easy on you. You're going to lose points with me, so don't push my patience too far.";


    public bool extendToPlayerAllies { get; set; } = true;

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

public class MapPatrolConfig
{
    public bool EnableISBPatrols { get; set; }
    public float patrolChance { get; set; }
    public int squadCount { get; set; }
    public int patrolMinSize { get; set; }
    public int patrolMaxSize { get; set; }
    public List<string> mapZones { get; set; }
    public int raidTimeMin { get; set; }
    public int raidTimeMax { get; set; }
}

public class MapCheckpointConfig
{
    public bool enableISBHoldZones { get; set; }
    public int holdZonesCount { get; set; }
    public List<ZoneCheckpointConfig> holdMapZones { get; set; }
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
    public bool enableHunt { get; set; }
    public Dictionary<string, FactionHuntConfig> hunts { get; set; } = new();
}

public class FactionHuntConfig
{
    public float huntChance { get; set; } = 100;
    public int huntMin { get; set; }
    public int huntMax { get; set; }
    public string huntZones { get; set; } = "";
}

public class MapConfig
{
    public bool enableSecrets { get; set; } = true;
    public MapPatrolConfig patrol { get; set; }
    public MapCheckpointConfig checkpoint { get; set; }
    public MapHuntConfig hunt { get; set; }
}
