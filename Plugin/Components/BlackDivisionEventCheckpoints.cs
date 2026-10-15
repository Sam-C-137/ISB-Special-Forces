using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using ISB.Compatibility;
using ISBSpecialForces.Models;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.SinglePlayer.Utils.InRaid;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Components
{
    internal sealed class BlackDivisionEventCheckpoints
    {
        private readonly Dictionary<string, ISBCheckpoint> _checkpoints = new Dictionary<string, ISBCheckpoint>(StringComparer.Ordinal);
        internal static bool Installed => Chainloader.PluginInfos.ContainsKey(BlackDivisionCheckpointPolicy.PluginGuid);
        internal bool ContainmentActive { get; private set; }

        internal void Clear()
        {
            _checkpoints.Clear();
            ContainmentActive = false;
        }

        internal void Load(MainConfig config)
        {
            Clear();
            if (!Installed || !string.Equals(RaidChangesUtil.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase) || (config?.labsContainmentBreachEvent?.enabled != true && config?.labsBlackoutEvent?.enabled != true))
                return;
            LabsPreBlackoutEventStatus status;
            try
            {
                status = JsonConvert.DeserializeObject<LabsPreBlackoutEventStatus>(RequestHandler.GetJson("/ISB/labsPreBlackoutEvent/status"));
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("[ISB] Failed to read Black Division checkpoint event state: " + ex.Message);
                return;
            }

            if (status?.blackDivisionInstalled != true)
                return;
            var containment = config.labsContainmentBreachEvent;
            ContainmentActive = status.active && containment?.enabled == true;
            if (status.active && containment?.enabled == true)
            {
                if (containment.blackDivision?.enabled == true)
                    Register(containment.blackDivision.checkpoints, BlackDivisionCheckpointPolicy.ContainmentTrigger);
                RegisterPhase(containment.labsChaos, BlackDivisionCheckpointPolicy.ChaosTrigger);
            }

            if (config.labsBlackoutEvent?.enabled == true)
            {
                RegisterPhase(config.labsBlackoutEvent.blackoutActivated, BlackDivisionCheckpointPolicy.BlackoutTrigger);
                RegisterPhase(config.labsBlackoutEvent.powerRestored, BlackDivisionCheckpointPolicy.PowerRestoredTrigger);
            }
        }

        private void RegisterPhase(LabsBlackoutEventPhaseConfig phase, string trigger)
        {
            if (BlackDivisionCheckpointPolicy.UseBlackDivision(true, phase?.enabled == true, phase?.blackDivision?.enabled == true, phase?.selectionMode))
                Register(phase.blackDivision.checkpoints, trigger);
        }

        private void Register(List<SpawnFormationConfig> formations, string phaseTrigger)
        {
            var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
            if (formations == null || spawner == null)
                return;
            for (int index = 0; index < formations.Count; index++)
            {
                var formation = formations[index];
                if (formation == null || !formation.enabled || formation.zones == null || !BlackDivisionCheckpointPolicy.ValidPoint(formation.x, formation.y, formation.z, formation.checkpointRadius))
                    continue;
                BotZone zone = null;
                foreach (string configuredZone in formation.zones)
                {
                    if (string.IsNullOrWhiteSpace(configuredZone))
                        continue;
                    foreach (string name in configuredZone.Split(','))
                    {
                        zone = spawner.GetZoneByName(name.Trim());
                        if (zone != null)
                            break;
                    }

                    if (zone != null)
                        break;
                }

                if (zone == null)
                    continue;
                var position = new Vector3(formation.x, formation.y, formation.z);
                if (!NavMesh.SamplePosition(position, out var hit, 2f, NavMesh.AllAreas))
                {
                    Plugin.LogSource.LogError("[ISB] Black Division checkpoint is outside the navigation mesh: " + formation.id);
                    continue;
                }

                _checkpoints[BlackDivisionCheckpointPolicy.FormationTrigger(phaseTrigger, index)] = new ISBCheckpoint(zone, hit.position, formation.checkpointRadius);
            }
        }

        internal bool TryGet(BotOwner bot, out ISBCheckpoint checkpoint)
        {
            checkpoint = null;
            if (!Installed || bot?.Profile?.Info?.Settings == null || !BlackDivisionCheckpointPolicy.IsRole((int)bot.Profile.Info.Settings.Role))
                return false;
            string spawnId = bot.SpawnProfileData?.SpawnParams?.Id_spawn;
            return spawnId != null && _checkpoints.TryGetValue(spawnId, out checkpoint);
        }
    }
}
