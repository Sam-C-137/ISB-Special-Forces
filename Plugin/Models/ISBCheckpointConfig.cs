using System.Collections.Generic;

namespace ISBSpecialForces.Models;
public class MainConfig
{
    public Dictionary<string, MapConfig> locations { get; set; }
    public LabsPreBlackoutEventConfig labsContainmentBreachEvent { get; set; }
    public LabsBlackoutEventConfig labsBlackoutEvent { get; set; }
}

public class LabsPreBlackoutEventConfig
{
    public bool enabled { get; set; }
    public LabsPreBlackoutFactionConfig isb { get; set; }
    public LabsPreBlackoutFactionConfig blackDivision { get; set; }
    public LabsBlackoutEventPhaseConfig labsChaos { get; set; }
}

public class LabsPreBlackoutFactionConfig
{
    public bool enabled { get; set; }
    public List<SpawnFormationConfig> checkpoints { get; set; }
}

public class LabsBlackoutEventConfig
{
    public bool enabled { get; set; }
    public LabsBlackoutEventPhaseConfig blackoutActivated { get; set; }
    public LabsBlackoutEventPhaseConfig powerRestored { get; set; }
}

public class LabsBlackoutEventPhaseConfig
{
    public bool enabled { get; set; }
    public LabsPreBlackoutFactionConfig isb { get; set; }
    public LabsPreBlackoutFactionConfig blackDivision { get; set; }
    public string selectionMode { get; set; }
}

public class LabsPreBlackoutEventStatus
{
    public bool active { get; set; }
    public bool blackDivisionInstalled { get; set; }
}

public class MapConfig
{
    public MapCheckpointConfig checkpoint { get; set; }
}

public class MapCheckpointConfig
{
    public bool enableISBHoldZones { get; set; }
    public int holdZonesCount { get; set; }
    public List<ZoneCheckpointConfig> holdMapZones { get; set; }
    public List<SpawnFormationConfig> formations { get; set; }
}

public class SpawnFormationConfig
{
    public string id { get; set; }
    public bool enabled { get; set; } = true;
    public List<string> zones { get; set; }
    public float checkpointRadius { get; set; } = 20f;
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
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
