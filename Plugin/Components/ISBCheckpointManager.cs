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
        private readonly Dictionary<string, ISBCheckpoint> ContainmentCheckpoints = new Dictionary<string, ISBCheckpoint>();
        private bool _containmentCheckpointsActive;
        private readonly Dictionary<string, ISBCheckpoint> ConfiguredCheckpoints = new Dictionary<string, ISBCheckpoint>();
        public Dictionary<BotZone, ISBCheckpoint> ZoneCheckpoints = new Dictionary<BotZone, ISBCheckpoint>();
        private readonly Dictionary<BotZone, ISBCheckpoint> LabsBlackoutCheckpoints = new Dictionary<BotZone, ISBCheckpoint>();
        private readonly Dictionary<BotZone, ISBCheckpoint> LabsPowerRestoredCheckpoints = new Dictionary<BotZone, ISBCheckpoint>();
        private readonly Dictionary<BotZone, ISBCheckpoint> LabsChaosCheckpoints = new Dictionary<BotZone, ISBCheckpoint>();
        private readonly Dictionary<BotZone, float> RuntimeCheckpointAssignmentUntil = new Dictionary<BotZone, float>();
        private readonly BlackDivisionEventCheckpoints _blackDivision = new BlackDivisionEventCheckpoints();
        internal bool BlackDivisionContainmentActive => _blackDivision.ContainmentActive;

        private const string LabsBlackoutCheckpointTriggerId = "isb_labs_blackout_checkpoint";
        private const string LabsChaosCheckpointTriggerId = "isb_labs_chaos_checkpoint";
        private const string LabsPowerRestoredCheckpointTriggerId = "isb_labs_power_restored_checkpoint";
        public void InitRaid()
        {
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo("[ISB] Initializing ISBCheckpointManager for raid...");
            }

            BotISBManagerFactory.ClearManagers();
            RuntimeCheckpointAssignmentUntil.Clear();
            LabsBlackoutCheckpoints.Clear();
            LabsPowerRestoredCheckpoints.Clear();
            LabsChaosCheckpoints.Clear();
            LoadCheckpoints();
        }

        public void FindAndAssignCheckpoint(BotOwner botOwner)
        {
            if (!ISBFikaAuthority.IsAiAuthority || botOwner?.Profile?.Info?.Settings == null)
                return;
            if (_blackDivision.TryGet(botOwner, out var blackDivisionCheckpoint))
            {
                AssignBotToCheckpoint(botOwner, blackDivisionCheckpoint);
                return;
            }

            if (botOwner == null || !WildSpawnTypeExtensions.IsISB(botOwner.Profile.Info.Settings.Role))
                return;
            if (ConfiguredCheckpoints.TryGetValue(GetSpawnId(botOwner), out var configuredCheckpoint))
            {
                AssignBotToCheckpoint(botOwner, configuredCheckpoint);
                return;
            }

            if (ContainmentCheckpoints.TryGetValue(GetSpawnId(botOwner), out var ownCheckpoint))
            {
                AssignBotToCheckpoint(botOwner, ownCheckpoint);
                return;
            }

            if (_containmentCheckpointsActive && !IsLabsEventCheckpointBot(botOwner))
                return;
            if (!ISBSpecialForces.Behavior.BlackDivision.BlackDivisionTactics.CanAssignCheckpoint(GetSpawnId(botOwner)))
                return;
            var botSpawner = Singleton<IBotGame>.Instance.BotsController.BotSpawner;
            var botZone = botOwner.SpawnBotZone != null ? botOwner.SpawnBotZone : botSpawner.GetClosestZone(botOwner.Position, out float dist);
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB] [{botOwner.Profile.Nickname}] Spawn bot zone: {(botZone != null ? botZone.NameZone : "None")}");
            }

            if (botZone != null)
            {
                var checkpoint = GetLabsEventCheckpointForBot(botOwner, botZone) ?? GetCheckpointForZone(botZone);
                if (checkpoint != null)
                {
                    AssignBotToCheckpoint(botOwner, checkpoint);
                }
            }
        }

        public void AssignBotToCheckpoint(BotOwner botOwner, ISBCheckpoint checkpoint)
        {
            if (!ISBFikaAuthority.IsAiAuthority || botOwner == null || checkpoint == null)
                return;
            var botISBManager = botOwner.GetOrAddISBManager();
            if (botISBManager == null)
                return;
            botISBManager.SetAssignedCheckpoint(checkpoint);
            checkpoint.AssignedBots.RemoveAll(bot => bot == null || bot.IsDead);
            if (!checkpoint.AssignedBots.Contains(botOwner))
                checkpoint.AssignedBots.Add(botOwner);
        }

        public ISBCheckpoint GetCheckpointForZone(BotZone zone)
        {
            if (ZoneCheckpoints.TryGetValue(zone, out var checkpoint))
            {
                if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                {
                    Plugin.LogSource.LogInfo($"[ISB] Found checkpoint {checkpoint.Position} for zone {zone.NameZone}");
                }

                return checkpoint;
            }

            return null;
        }

        public void RegisterRuntimeCheckpoint(string[] configuredZones, Vector3 position, float radius, string formationId, float assignmentWindowSeconds)
        {
            if (configuredZones == null || configuredZones.Length == 0)
                return;
            RegisterCheckpoint(configuredZones, position, radius, formationId);
            var botGame = Singleton<IBotGame>.Instance;
            var botSpawner = botGame?.BotsController?.BotSpawner;
            if (botSpawner == null)
                return;
            var until = Time.time + Mathf.Max(1f, assignmentWindowSeconds);
            foreach (var configuredZone in configuredZones)
            {
                if (string.IsNullOrWhiteSpace(configuredZone))
                    continue;
                foreach (var rawZoneName in configuredZone.Split(','))
                {
                    var zoneName = rawZoneName.Trim();
                    if (string.IsNullOrEmpty(zoneName))
                        continue;
                    var zone = botSpawner.GetZoneByName(zoneName);
                    if (zone != null)
                        RuntimeCheckpointAssignmentUntil[zone] = until;
                }
            }
        }

        public bool CanAssignRuntimeCheckpoint(BotOwner botOwner)
        {
            if (botOwner == null)
                return false;
            if (ConfiguredCheckpoints.ContainsKey(GetSpawnId(botOwner)) || ContainmentCheckpoints.ContainsKey(GetSpawnId(botOwner)) || IsBlackDivisionCheckpointBot(botOwner))
                return true;
            if (IsLabsEventCheckpointBot(botOwner))
                return true;
            if (RuntimeCheckpointAssignmentUntil.Count == 0)
                return false;
            var botGame = Singleton<IBotGame>.Instance;
            var botSpawner = botGame?.BotsController?.BotSpawner;
            if (botSpawner == null)
                return false;
            var zone = botOwner.SpawnBotZone != null ? botOwner.SpawnBotZone : botSpawner.GetClosestZone(botOwner.Position, out float dist);
            if (zone == null || !RuntimeCheckpointAssignmentUntil.TryGetValue(zone, out var until))
                return false;
            if (Time.time <= until)
                return true;
            RuntimeCheckpointAssignmentUntil.Remove(zone);
            return false;
        }

        public void LoadCheckpoints()
        {
            _blackDivision.Clear();
            ContainmentCheckpoints.Clear();
            ConfiguredCheckpoints.Clear();
            _containmentCheckpointsActive = false;
            ZoneCheckpoints.Clear();
            LabsBlackoutCheckpoints.Clear();
            LabsPowerRestoredCheckpoints.Clear();
            LabsChaosCheckpoints.Clear();
            if (!ISBFikaAuthority.IsAiAuthority)
                return;
            var result = RequestHandler.GetJson("/ISB/checkpoints");
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB] Loading checkpoints for {RaidChangesUtil.LocationId}...");
            }

            var mainConfig = JsonConvert.DeserializeObject<MainConfig>(result);
            ZoneCheckpoints.Clear();
            LabsBlackoutCheckpoints.Clear();
            LabsPowerRestoredCheckpoints.Clear();
            LabsChaosCheckpoints.Clear();
            if (mainConfig?.locations == null)
            {
                Plugin.LogSource.LogError("[ISB] Checkpoint config is empty or invalid.");
                return;
            }

            var mapConfig = FindMapConfig(mainConfig, RaidChangesUtil.LocationId);
            if (mapConfig == null)
            {
                Plugin.LogSource.LogWarning($"[ISB] No configuration found for location {RaidChangesUtil.LocationId}.");
                return;
            }

            var checkpointConfig = mapConfig.checkpoint;
            _blackDivision.Load(mainConfig);
            var eventCheckpoints = GetActiveLabsPreBlackoutCheckpoints(mainConfig);
            if (eventCheckpoints != null)
            {
                _containmentCheckpointsActive = true;
                for (int index = 0; index < eventCheckpoints.Count; index++)
                {
                    var formation = eventCheckpoints[index];
                    if (formation?.enabled != true)
                        continue;
                    var points = new Dictionary<BotZone, ISBCheckpoint>();
                    RegisterFormationCheckpoints(new[] { formation }, points);
                    foreach (var point in points.Values)
                    {
                        ContainmentCheckpoints["isb_labs_containment_checkpoint_isbcp_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = point;
                        break;
                    }
                }
            }
            else if (checkpointConfig != null && checkpointConfig.enableISBHoldZones)
            {
                if (checkpointConfig.formations != null && checkpointConfig.formations.Count > 0)
                {
                    for (int index = 0; index < checkpointConfig.formations.Count; index++)
                    {
                        var points = new Dictionary<BotZone, ISBCheckpoint>();
                        RegisterFormationCheckpoints(new[] { checkpointConfig.formations[index] }, points);
                        foreach (var point in points.Values)
                        {
                            ConfiguredCheckpoints["isb_config_checkpoint_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = point;
                            break;
                        }
                    }
                }
                else if (checkpointConfig.holdMapZones != null)
                {
                    foreach (var zoneConfig in checkpointConfig.holdMapZones)
                    {
                        if (zoneConfig == null || string.IsNullOrWhiteSpace(zoneConfig.checkpointZone))
                            continue;
                        RegisterCheckpoint(new List<string> { zoneConfig.checkpointZone }, new Vector3(zoneConfig.x, zoneConfig.y, zoneConfig.z), zoneConfig.checkpointRadius, "legacy");
                    }
                }
            }

            RegisterLabsBlackoutEventCheckpoints(mainConfig);
        }

        public bool IsBlackDivisionCheckpointBot(BotOwner botOwner) => _blackDivision.TryGet(botOwner, out _);
        private List<SpawnFormationConfig> GetActiveLabsPreBlackoutCheckpoints(MainConfig mainConfig)
        {
            if (Normalize(RaidChangesUtil.LocationId) != "laboratory")
                return null;
            if (mainConfig?.labsContainmentBreachEvent?.enabled != true || mainConfig.labsContainmentBreachEvent.isb?.enabled != true)
                return null;
            try
            {
                var statusJson = RequestHandler.GetJson("/ISB/labsPreBlackoutEvent/status");
                var status = JsonConvert.DeserializeObject<LabsPreBlackoutEventStatus>(statusJson);
                if (status?.active != true)
                    return null;
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource.LogError("[ISB] Failed to read Labs ContainmentBreach checkpoint state: " + ex.Message);
                return null;
            }

            return mainConfig.labsContainmentBreachEvent.isb.checkpoints;
        }

        private void RegisterLabsBlackoutEventCheckpoints(MainConfig mainConfig)
        {
            if (Normalize(RaidChangesUtil.LocationId) == "laboratory" && mainConfig?.labsContainmentBreachEvent?.enabled == true)
            {
                var chaos = mainConfig.labsContainmentBreachEvent.labsChaos;
                if (chaos?.enabled == true && chaos.isb?.enabled == true && chaos.isb.checkpoints != null)
                    RegisterFormationCheckpoints(chaos.isb.checkpoints, LabsChaosCheckpoints);
            }

            if (Normalize(RaidChangesUtil.LocationId) != "laboratory" || mainConfig?.labsBlackoutEvent?.enabled != true)
                return;
            var blackout = mainConfig.labsBlackoutEvent.blackoutActivated;
            if (blackout?.enabled == true && blackout.isb?.enabled == true && blackout.isb.checkpoints != null)
            {
                RegisterFormationCheckpoints(blackout.isb.checkpoints, LabsBlackoutCheckpoints);
            }

            var restored = mainConfig.labsBlackoutEvent.powerRestored;
            if (restored?.enabled == true && restored.isb?.enabled == true && restored.isb.checkpoints != null)
            {
                RegisterFormationCheckpoints(restored.isb.checkpoints, LabsPowerRestoredCheckpoints);
            }
        }

        private ISBCheckpoint GetLabsEventCheckpointForBot(BotOwner botOwner, BotZone zone)
        {
            var spawnId = GetSpawnId(botOwner);
            if (spawnId.IndexOf(LabsChaosCheckpointTriggerId, System.StringComparison.OrdinalIgnoreCase) >= 0 && LabsChaosCheckpoints.TryGetValue(zone, out var chaosCheckpoint))
                return chaosCheckpoint;
            if (spawnId.IndexOf(LabsBlackoutCheckpointTriggerId, System.StringComparison.OrdinalIgnoreCase) >= 0 && LabsBlackoutCheckpoints.TryGetValue(zone, out var blackoutCheckpoint))
            {
                return blackoutCheckpoint;
            }

            if (spawnId.IndexOf(LabsPowerRestoredCheckpointTriggerId, System.StringComparison.OrdinalIgnoreCase) >= 0 && LabsPowerRestoredCheckpoints.TryGetValue(zone, out var restoredCheckpoint))
            {
                return restoredCheckpoint;
            }

            return null;
        }

        private static bool IsLabsEventCheckpointBot(BotOwner botOwner)
        {
            var spawnId = GetSpawnId(botOwner);
            return spawnId.IndexOf(LabsBlackoutCheckpointTriggerId, System.StringComparison.OrdinalIgnoreCase) >= 0 || spawnId.IndexOf(LabsChaosCheckpointTriggerId, System.StringComparison.OrdinalIgnoreCase) >= 0 || spawnId.IndexOf(LabsPowerRestoredCheckpointTriggerId, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetSpawnId(BotOwner botOwner)
        {
            return botOwner?.SpawnProfileData?.SpawnParams?.Id_spawn ?? string.Empty;
        }

        private void RegisterFormationCheckpoints(IEnumerable<SpawnFormationConfig> formations)
        {
            RegisterFormationCheckpoints(formations, ZoneCheckpoints);
        }

        private void RegisterFormationCheckpoints(IEnumerable<SpawnFormationConfig> formations, Dictionary<BotZone, ISBCheckpoint> target)
        {
            foreach (var formation in formations)
            {
                if (formation == null || !formation.enabled || formation.zones == null || formation.zones.Count == 0)
                    continue;
                RegisterCheckpoint(formation.zones, new Vector3(formation.x, formation.y, formation.z), formation.checkpointRadius, formation.id, target);
            }
        }

        private void RegisterCheckpoint(IEnumerable<string> configuredZones, Vector3 position, float radius, string formationId)
        {
            RegisterCheckpoint(configuredZones, position, radius, formationId, ZoneCheckpoints);
        }

        private void RegisterCheckpoint(IEnumerable<string> configuredZones, Vector3 position, float radius, string formationId, Dictionary<BotZone, ISBCheckpoint> target)
        {
            var botSpawner = Singleton<IBotGame>.Instance.BotsController.BotSpawner;
            ISBCheckpoint checkpoint = null;
            foreach (var configuredZone in configuredZones)
            {
                if (string.IsNullOrWhiteSpace(configuredZone))
                    continue;
                foreach (var rawZoneName in configuredZone.Split(','))
                {
                    var zoneName = rawZoneName.Trim();
                    if (string.IsNullOrEmpty(zoneName))
                        continue;
                    var zone = botSpawner.GetZoneByName(zoneName);
                    if (zone == null)
                    {
                        continue;
                    }

                    if (checkpoint == null)
                    {
                        checkpoint = new ISBCheckpoint(zone, position, radius);
                    }

                    target[zone] = checkpoint;
                }
            }
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
