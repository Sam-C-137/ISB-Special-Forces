using System.Collections.Generic;

namespace ISBSpecialForces.Models;

public class MainConfig
{
    public Dictionary<string, MapConfig> locations { get; set; }
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
