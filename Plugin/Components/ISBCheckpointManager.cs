using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.SinglePlayer.Utils.InRaid;
using System.Collections.Generic;
using ISBSpecialForces.Controllers;
using ISBSpecialForces.Models;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public class ISBCheckpointManager : MonoBehaviourSingleton<ISBCheckpointManager>
    {
        public Dictionary<BotZone, ISBCheckpoint> ZoneCheckpoints = new Dictionary<BotZone, ISBCheckpoint>();

        public void InitRaid()
        {
            Plugin.LogSource.LogInfo("[ISB] Initializing ISBCheckpointManager for raid...");
            BotISBManagerFactory.ClearManagers();
            LoadCheckpoints();
        }

        public void FindAndAssignCheckpoint(BotOwner botOwner)
        {
            var botSpawner = Singleton<IBotGame>.Instance.BotsController.BotSpawner;
            var botZone = botOwner.SpawnBotZone != null ? botOwner.SpawnBotZone : botSpawner.GetClosestZone(botOwner.Position, out float dist);

            Plugin.LogSource.LogInfo($"[ISB] [{botOwner.Profile.Nickname}] Spawn bot zone: {(botZone != null ? botZone.NameZone : "None")}");

            if (botZone != null)
            {
                var checkpoint = GetCheckpointForZone(botZone);
                if (checkpoint != null)
                {
                    AssignBotToCheckpoint(botOwner, checkpoint);
                }
            }
        }

        public void AssignBotToCheckpoint(BotOwner botOwner, ISBCheckpoint checkpoint)
        {
            var botISBManager = botOwner.GetOrAddISBManager();
            botISBManager.SetAssignedCheckpoint(checkpoint);
            checkpoint.AssignedBots.Add(botOwner);
        }

        public ISBCheckpoint GetCheckpointForZone(BotZone zone)
        {
            if (ZoneCheckpoints.TryGetValue(zone, out var checkpoint))
            {
                Plugin.LogSource.LogInfo($"[ISB] Found checkpoint {checkpoint.Position} for zone {zone.NameZone}");
                return checkpoint;
            }
            return null;
        }

        public void LoadCheckpoints()
        {
            var result = RequestHandler.GetJson("/ISB/checkpoints");
            Plugin.LogSource.LogInfo($"[ISB] Loading checkpoints for {RaidChangesUtil.LocationId}...");
            var mainConfig = JsonConvert.DeserializeObject<MainConfig>(result);

            ZoneCheckpoints.Clear();

            if (mainConfig?.locations == null)
            {
                Plugin.LogSource.LogWarning("[ISB] Checkpoint config is empty or invalid.");
                return;
            }

            var mapConfig = FindMapConfig(mainConfig, RaidChangesUtil.LocationId);

            if (mapConfig == null)
            {
                Plugin.LogSource.LogWarning($"[ISB] No configuration found for location {RaidChangesUtil.LocationId}.");
                return;
            }

            var checkpointConfig = mapConfig.checkpoint;

            if (checkpointConfig == null || !checkpointConfig.enableISBHoldZones || checkpointConfig.holdMapZones == null)
                return;

            var botSpawner = Singleton<IBotGame>.Instance.BotsController.BotSpawner;

            foreach (var zoneConfig in checkpointConfig.holdMapZones)
            {
                var checkpoint = default(ISBCheckpoint);

                foreach (var rawZoneName in zoneConfig.checkpointZone.Split(','))
                {
                    var zoneName = rawZoneName.Trim();
                    var zone = botSpawner.GetZoneByName(zoneName);

                    if (zone == null)
                    {
                        Plugin.LogSource.LogWarning($"[ISB] BotZone '{zoneName}' not found on this map.");
                        continue;
                    }

                    if (checkpoint == null)
                    {
                        checkpoint = new ISBCheckpoint
                        (
                            zone,
                            new Vector3(zoneConfig.x, zoneConfig.y, zoneConfig.z),
                            zoneConfig.checkpointRadius
                        );
                    }

                    ZoneCheckpoints[zone] = checkpoint;
                }
            }

            Plugin.LogSource.LogInfo($"[ISB] {ZoneCheckpoints.Count} checkpoint zone(s) registered for {RaidChangesUtil.LocationId}.");
        }

        private MapConfig FindMapConfig(MainConfig mainConfig, string locationId)
        {
            var normalizedId = Normalize(locationId);

            foreach (var entry in mainConfig.locations)
            {
                if (Normalize(entry.Key) == normalizedId)
                    return entry.Value;
            }

            return null;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant().Replace("_", "");
        }
    }
}
