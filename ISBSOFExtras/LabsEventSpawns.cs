using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Text;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private void ScheduleLabsChaosSpawn(string audio)
        {
            if (!_raidisActive || !IsEventAuthority || !IsLaboratoryPresentationAllowed() || !_labsPreBlackoutEventActive || !_labsChaosEnabled || _labsChaosScheduled || !(string.Equals(audio, "ISB-LabsChaos", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "ISB-LabsChaos.wav", StringComparison.OrdinalIgnoreCase)))
                return;
            _labsChaosScheduled = true;
            _labsChaosRoutine = StartCoroutine(FireLabsChaosSpawn(_activeRaidWorld, _currentNetworkAnnouncementStart + AnnouncementTriggerDelays.LabsChaosSeconds));
        }

        private IEnumerator FireLabsChaosSpawn(GameWorld world, double fireAt)
        {
            while (CanFireLabsChaos(world) && SynchronizedTime < fireAt)
                yield return null;
            var wait = new WaitForSecondsRealtime(0.25f);
            double deadline = SynchronizedTime + 10d;
            while (CanFireLabsChaos(world) && SynchronizedTime <= deadline)
            {
                var dispatcher = Singleton<BotEventHandler>.Instance;
                if (dispatcher != null)
                {
                    try
                    {
                        dispatcher.AnyEvent("isb_labs_chaos_patrol");
                        FireLabsCheckpointEvent(dispatcher, "isb_labs_chaos_checkpoint");
                        dispatcher.AnyEvent("isb_labs_chaos_hunt");
                        ISB.Compatibility.ISBEffectNotifications.Publish("labs_chaos", Log);
                    }
                    catch (Exception ex)
                    {
                        Log.LogError("ISB SOF: Labs Chaos spawn event failed: " + ex.Message);
                    }

                    _labsChaosRoutine = null;
                    yield break;
                }

                yield return wait;
            }

            if (CanFireLabsChaos(world))
                Log.LogError("ISB SOF: Labs Chaos spawn dispatcher was not available.");
            _labsChaosRoutine = null;
        }

        private static void FireLabsCheckpointEvent(BotEventHandler dispatcher, string triggerId)
        {
            dispatcher.AnyEvent(triggerId);
            if (!Chainloader.PluginInfos.ContainsKey(ISB.Compatibility.BlackDivisionCheckpointPolicy.PluginGuid) || !ISB.Compatibility.BlackDivisionCheckpointPolicy.IsPhaseTrigger(triggerId))
                return;
            var waves = (Singleton<AbstractGame>.Instance as BaseLocalGame<EftGamePlayerOwner>)?.Location_0?.BossLocationSpawn;
            if (waves == null)
                return;
            var triggers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wave in waves)
            {
                if (wave != null && ISB.Compatibility.BlackDivisionCheckpointPolicy.IsFormationTrigger(triggerId, wave.TriggerId))
                    triggers.Add(wave.TriggerId);
            }

            foreach (string formationTrigger in triggers)
                dispatcher.AnyEvent(formationTrigger);
        }

        private bool CanFireLabsChaos(GameWorld world) => _raidisActive && IsEventAuthority && _labsPreBlackoutEventActive && IsLaboratoryPresentationAllowed() && world != null && ReferenceEquals(world, _activeRaidWorld) && ReferenceEquals(world, Singleton<GameWorld>.Instance);
        private void ScheduleLabsBlackoutSpawnEvent()
        {
            if (!IsEventAuthority || !_raidisActive || _labsPreBlackoutEventActive || _labsBlackoutSpawnEventTriggered || _labsBlackoutSpawnEventRoutine != null)
            {
                return;
            }

            _labsBlackoutSpawnEventRoutine = StartCoroutine(FireLabsSpawnEventWhenReady(false));
        }

        private void ScheduleLabsPowerRestoredSpawnEvent()
        {
            if (!IsEventAuthority || !_raidisActive || _labsPreBlackoutEventActive || _labsPowerRestoredSpawnEventTriggered || _labsPowerRestoredSpawnEventRoutine != null)
            {
                return;
            }

            _labsPowerRestoredSpawnEventRoutine = StartCoroutine(FireLabsSpawnEventWhenReady(true));
        }

        private IEnumerator FireLabsSpawnEventWhenReady(bool powerRestored)
        {
            string[] triggerIds = powerRestored ? new[]
            {
                LabsPowerRestoredPatrolTriggerId,
                LabsPowerRestoredCheckpointTriggerId,
                LabsPowerRestoredHuntTriggerId
            }

            : new[]
            {
                LabsBlackoutPatrolTriggerId,
                LabsBlackoutCheckpointTriggerId,
                LabsBlackoutHuntTriggerId
            };
            float deadline = Time.time + 10f;
            bool fired = false;
            while (_raidisActive && IsEventAuthority && !_labsPreBlackoutEventActive && Time.time <= deadline)
            {
                BotEventHandler eventHandler = Singleton<BotEventHandler>.Instance;
                if (eventHandler != null)
                {
                    try
                    {
                        for (int i = 0; i < triggerIds.Length; i++)
                            FireLabsCheckpointEvent(eventHandler, triggerIds[i]);
                        fired = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.LogError("ISB SOF Blackout: Failed to fire Labs " + (powerRestored ? "power restored" : "blackout activated") + " spawn event: " + ex.Message);
                    }
                }

                yield return new WaitForSeconds(0.5f);
            }

            if (powerRestored)
            {
                _labsPowerRestoredSpawnEventTriggered = fired;
                _labsPowerRestoredSpawnEventRoutine = null;
            }
            else
            {
                _labsBlackoutSpawnEventTriggered = fired;
                _labsBlackoutSpawnEventRoutine = null;
            }

            if (!(fired))
                if (_raidisActive && IsEventAuthority && !_labsPreBlackoutEventActive)
                {
                    Log.LogError("ISB SOF Blackout: Labs " + (powerRestored ? "power restored" : "blackout activated") + " spawn event could not be fired because BotEventHandler was unavailable.");
                }
        }
    }
}
