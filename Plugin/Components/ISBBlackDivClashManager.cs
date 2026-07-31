using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public class ISBBlackDivClashManager : MonoBehaviourSingleton<ISBBlackDivClashManager>
    {
        private const string BlackDivGuid = "com.blackdiv.tacticaltoaster";
        private const string ClashTriggerId = "isb_secret_bd_clash_hunt";
        private const string CounterattackTriggerId = "isb_secret_bd_counterattack";

        private const float CounterattackDelaySeconds = 40f;
        private const float CounterattackGiveUpSeconds = 180f;
        private const float CombatProximityFallback = 95f;

        private readonly Dictionary<string, BotOwner> blackDivisionBots =
            new Dictionary<string, BotOwner>(StringComparer.Ordinal);

        private readonly Dictionary<string, BotOwner> isbBots =
            new Dictionary<string, BotOwner>(StringComparer.Ordinal);

        private readonly HashSet<string> originalBlackDivisionIds =
            new HashSet<string>(StringComparer.Ordinal);

        private bool modPresent;
        private bool clashFired;
        private bool counterattackFired;
        private bool counterattackCancelled;
        private float nextCheck;
        private float counterattackAvailableAt;
        private float counterattackGiveUpAt;

        public void ResetRaid()
        {
            clashFired = false;
            counterattackFired = false;
            counterattackCancelled = false;
            nextCheck = 0f;
            counterattackAvailableAt = 0f;
            counterattackGiveUpAt = 0f;
            blackDivisionBots.Clear();
            isbBots.Clear();
            originalBlackDivisionIds.Clear();
            ISBBlackDivCounterattackTeleport.Reset();
        }

        public void RegisterBot(BotOwner bot)
        {
            if (bot?.Profile?.Info?.Settings == null)
                return;

            var botId = ISBBlackDivisionUtility.GetBotId(bot);

            if (string.IsNullOrEmpty(botId))
                return;

            var role = bot.Profile.Info.Settings.Role;

            if (ISBBlackDivisionUtility.IsBlackDivisionRole(role))
            {
                blackDivisionBots[botId] = bot;

                ISBBlackDivCounterattackTeleport.TryQueue(bot);
                return;
            }

            if (WildSpawnTypeExtensions.IsISB(role))
                isbBots[botId] = bot;
        }

        private void Start()
        {
            modPresent = Chainloader.PluginInfos.ContainsKey(BlackDivGuid);

            if (!modPresent)
                Plugin.LogSource.LogInfo("[ISB SECRET] Black Division not installed. Disabling Module.");
        }

        private void Update()
        {
            if (!modPresent || Time.time < nextCheck)
                return;

            nextCheck = Time.time + 1f;
            RefreshBotRegistriesFromWorld();
            PruneDeadBots();

            if (!clashFired)
            {
                TryStartClash();
                return;
            }

            if (!counterattackFired && !counterattackCancelled)
                TryStartCounterattack();
        }

        private void TryStartClash()
        {
            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld == null || gameWorld.AllAlivePlayersList == null)
                return;

            foreach (var player in gameWorld.AllAlivePlayersList)
            {
                if (player == null || !player.IsAI)
                    continue;

                if (player.HealthController == null || !player.HealthController.IsAlive)
                    continue;

                if (player.Profile?.Info?.Settings == null)
                    continue;

                if (!ISBBlackDivisionUtility.IsBlackDivisionRole(player.Profile.Info.Settings.Role))
                    continue;

                SnapshotOriginalBlackDivisionBots();

                if (player.Profile.Id != null)
                    originalBlackDivisionIds.Add(player.Profile.Id);

                var clashPoint = player.Position;

                Plugin.LogSource.LogInfo(
                    $"[ISB SECRET] Black Division detected at {clashPoint}. Dispatching 1 ISB Commander and 6 Second Leaders to engage.");

                if (!ISBSecretTeleport.Arm(
                        clashPoint,
                        expectedBots: 7,
                        minimumGather: 1.25f,
                        maximumGather: 10f,
                        hardWindowSeconds: 75f,
                        expectedRoles: new Dictionary<WildSpawnType, int>
                        {
                            [(WildSpawnType)13707] = 1,
                            [(WildSpawnType)13710] = 6,
                        },
                        requireFullAssembly: true))
                {
                    return;
                }

                if (!ISBSecretsManager.Fire(ClashTriggerId))
                {
                    ISBSecretTeleport.Reset();
                    return;
                }

                clashFired = true;
                counterattackAvailableAt = Time.time + CounterattackDelaySeconds;
                counterattackGiveUpAt = Time.time + CounterattackGiveUpSeconds;

                Plugin.LogSource.LogInfo(
                    $"[ISB SECRET] Black Division Counterattack will become eligible in {CounterattackDelaySeconds:F0}s if an original Black Division fighter remains engaged with the ISB.");
                return;
            }
        }

        private void TryStartCounterattack()
        {
            if (Time.time < counterattackAvailableAt)
                return;

            if (Time.time > counterattackGiveUpAt)
            {
                counterattackCancelled = true;
                Plugin.LogSource.LogInfo(
                    "[ISB SECRET] Black Division Counterattack expired because no original Black Division fighter remained engaged with the ISB.");
                return;
            }

            var anchor = SelectCounterattackAnchor();

            if (anchor == null)
            {
                if (!HasAnyOriginalBlackDivisionAlive())
                {
                    counterattackCancelled = true;
                    Plugin.LogSource.LogInfo(
                        "[ISB SECRET] Black Division Counterattack cancelled because every original Black Division bot is dead.");
                }

                return;
            }

            if (!ISBBlackDivCounterattackTeleport.Arm(
                    originalBlackDivisionIds,
                    SelectCounterattackAnchor,
                    expectedBots: 6,
                    minimumGather: 1.25f,
                    maximumGather: 8f,
                    hardWindowSeconds: 45f))
            {
                return;
            }

            if (!ISBSecretsManager.Fire(CounterattackTriggerId))
            {
                ISBBlackDivCounterattackTeleport.Reset();
                return;
            }

            counterattackFired = true;

            Plugin.LogSource.LogInfo(
                $"[ISB SECRET] Black Division Counterattack requested around original fighter '{anchor.Profile?.Nickname}'. " +
                "Only newly spawned blackDivAssault bots may enter the reinforcement teleport.");
        }

        private void SnapshotOriginalBlackDivisionBots()
        {
            originalBlackDivisionIds.Clear();

            foreach (var pair in blackDivisionBots)
            {
                if (ISBBlackDivisionUtility.IsAlive(pair.Value))
                    originalBlackDivisionIds.Add(pair.Key);
            }

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld?.AllAlivePlayersList == null)
                return;

            foreach (var player in gameWorld.AllAlivePlayersList)
            {
                if (player?.Profile?.Info?.Settings == null
                    || player.HealthController == null
                    || !player.HealthController.IsAlive
                    || !player.IsAI
                    || !ISBBlackDivisionUtility.IsBlackDivisionRole(player.Profile.Info.Settings.Role)
                    || string.IsNullOrEmpty(player.Profile.Id))
                {
                    continue;
                }

                originalBlackDivisionIds.Add(player.Profile.Id);
            }

            Plugin.LogSource.LogInfo(
                $"[ISB SECRET] Captured {originalBlackDivisionIds.Count} pre-counterattack Black Division bot ID(s) as valid anchor candidates.");
        }

        private BotOwner SelectCounterattackAnchor()
        {
            BotOwner bestDirectCombatAnchor = null;
            BotOwner bestNearbyAnchor = null;
            var bestDirectDistance = float.MaxValue;
            var bestNearbyDistance = float.MaxValue;

            foreach (var originalId in originalBlackDivisionIds)
            {
                if (!blackDivisionBots.TryGetValue(originalId, out var candidate)
                    || !ISBBlackDivisionUtility.IsAlive(candidate))
                {
                    continue;
                }

                var nearestISBDistance = GetNearestAliveISBDistance(candidate);
                var directCombat = ISBBlackDivisionUtility.HasISBGoalEnemy(candidate)
                    || IsTargetedByAliveISB(originalId);

                if (directCombat && nearestISBDistance < bestDirectDistance)
                {
                    bestDirectDistance = nearestISBDistance;
                    bestDirectCombatAnchor = candidate;
                }

                if (nearestISBDistance <= CombatProximityFallback
                    && nearestISBDistance < bestNearbyDistance)
                {
                    bestNearbyDistance = nearestISBDistance;
                    bestNearbyAnchor = candidate;
                }
            }

            return bestDirectCombatAnchor ?? bestNearbyAnchor;
        }

        private bool IsTargetedByAliveISB(string blackDivisionProfileId)
        {
            if (string.IsNullOrEmpty(blackDivisionProfileId))
                return false;

            foreach (var isb in isbBots.Values)
            {
                if (!ISBBlackDivisionUtility.IsAlive(isb))
                    continue;

                var targetId = ISBBlackDivisionUtility.GetGoalEnemyProfileId(isb);

                if (string.Equals(targetId, blackDivisionProfileId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private float GetNearestAliveISBDistance(BotOwner blackDivisionBot)
        {
            var nearest = float.MaxValue;

            foreach (var isb in isbBots.Values)
            {
                if (!ISBBlackDivisionUtility.IsAlive(isb))
                    continue;

                var distance = Vector3.Distance(blackDivisionBot.Position, isb.Position);

                if (distance < nearest)
                    nearest = distance;
            }

            return nearest;
        }

        private bool HasAnyOriginalBlackDivisionAlive()
        {
            foreach (var originalId in originalBlackDivisionIds)
            {
                if (blackDivisionBots.TryGetValue(originalId, out var bot)
                    && ISBBlackDivisionUtility.IsAlive(bot))
                {
                    return true;
                }
            }

            return false;
        }


        private void RefreshBotRegistriesFromWorld()
        {
            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld?.AllAlivePlayersList == null)
                return;

            foreach (var player in gameWorld.AllAlivePlayersList)
            {
                if (player == null
                    || !player.IsAI
                    || player.HealthController == null
                    || !player.HealthController.IsAlive)
                {
                    continue;
                }

                var botOwner = ISBBlackDivisionUtility.TryResolveBotOwner(player);

                if (botOwner != null)
                    RegisterBot(botOwner);
            }
        }

        private void PruneDeadBots()
        {
            PruneDictionary(blackDivisionBots);
            PruneDictionary(isbBots);
        }

        private static void PruneDictionary(Dictionary<string, BotOwner> bots)
        {
            if (bots.Count == 0)
                return;

            var staleIds = new List<string>();

            foreach (var pair in bots)
            {
                if (!ISBBlackDivisionUtility.IsAlive(pair.Value))
                    staleIds.Add(pair.Key);
            }

            for (var i = 0; i < staleIds.Count; i++)
                bots.Remove(staleIds[i]);
        }
    }
}
