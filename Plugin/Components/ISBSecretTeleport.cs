using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Components
{
    public static class ISBSecretTeleport
    {
        private const float DefaultHardWindowSeconds = 45f;
        private const float FormationRadius = 4f;
        private const float NavMeshSampleRadius = 8f;

        private static readonly List<BotOwner> queuedBots = new List<BotOwner>();
        private static readonly HashSet<string> queuedBotIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<WildSpawnType, int> expectedRoleCounts = new Dictionary<WildSpawnType, int>();
        private static readonly Dictionary<WildSpawnType, int> queuedRoleCounts = new Dictionary<WildSpawnType, int>();

        private static bool active;
        private static bool initialBatchTeleported;
        private static Vector3 destination;
        private static int expectedBotCount;
        private static float minimumGatherSeconds;
        private static float maximumGatherSeconds;
        private static float hardWindowEnd;
        private static float firstBotQueuedAt = -1f;
        private static float lastBotQueuedAt = -1f;
        private static bool requireFullAssemblyBeforeTeleport;
        private static ISBSecretTeleportRunner runner;

        public static bool Arm(
            Vector3 requestedPoint,
            int expectedBots,
            float minimumGather = 2f,
            float maximumGather = 7f,
            float hardWindowSeconds = DefaultHardWindowSeconds,
            IReadOnlyDictionary<WildSpawnType, int> expectedRoles = null,
            bool requireFullAssembly = false)
        {
            Reset();

            if (!TrySampleNavMesh(requestedPoint, 18f, out destination))
            {
                Plugin.LogSource.LogError(
                    $"[ISB SECRET] Could not arm teleport because no NavMesh point was found near {requestedPoint}.");
                return false;
            }

            expectedRoleCounts.Clear();
            queuedRoleCounts.Clear();

            if (expectedRoles != null)
            {
                foreach (var pair in expectedRoles)
                {
                    if (pair.Value > 0)
                        expectedRoleCounts[pair.Key] = pair.Value;
                }
            }

            expectedBotCount = expectedRoleCounts.Count > 0
                ? GetExpectedRoleTotal()
                : Mathf.Max(1, expectedBots);

            minimumGatherSeconds = Mathf.Max(0.25f, minimumGather);
            maximumGatherSeconds = Mathf.Max(minimumGatherSeconds, maximumGather);
            hardWindowEnd = Time.time + Mathf.Max(maximumGatherSeconds + 5f, hardWindowSeconds);
            firstBotQueuedAt = -1f;
            lastBotQueuedAt = -1f;
            requireFullAssemblyBeforeTeleport = requireFullAssembly && expectedRoleCounts.Count > 0;
            initialBatchTeleported = false;
            active = true;

            var host = new GameObject("ISB.SecretTeleportRunner");
            runner = host.AddComponent<ISBSecretTeleportRunner>();
            runner.Begin();

            Plugin.LogSource.LogInfo(
                requireFullAssemblyBeforeTeleport
                    ? $"[ISB SECRET] Strict group teleport armed at {destination}. Waiting for all {expectedBotCount} expected bot(s); partial teleport is disabled."
                    : $"[ISB SECRET] Group teleport armed at {destination}. Waiting for {expectedBotCount} bot(s) before moving the squad.");

            return true;
        }

        public static void Reset()
        {
            active = false;
            initialBatchTeleported = false;
            firstBotQueuedAt = -1f;
            lastBotQueuedAt = -1f;
            requireFullAssemblyBeforeTeleport = false;
            expectedBotCount = 0;
            queuedBots.Clear();
            queuedBotIds.Clear();
            expectedRoleCounts.Clear();
            queuedRoleCounts.Clear();

            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        public static bool TryQueue(BotOwner bot)
        {
            if (!active || bot == null)
                return false;

            if (Time.time > hardWindowEnd)
            {
                FinishSession();
                return false;
            }

            if (bot.Profile?.Info?.Settings == null)
                return false;

            var role = bot.Profile.Info.Settings.Role;

            if (expectedRoleCounts.Count > 0)
            {
                if (!expectedRoleCounts.TryGetValue(role, out var maximumForRole))
                    return false;

                queuedRoleCounts.TryGetValue(role, out var currentForRole);

                if (currentForRole >= maximumForRole)
                    return false;
            }

            var botId = GetBotId(bot);

            if (!queuedBotIds.Add(botId))
                return false;

            if (expectedRoleCounts.Count > 0)
            {
                queuedRoleCounts.TryGetValue(role, out var currentForRole);
                queuedRoleCounts[role] = currentForRole + 1;
            }

            if (firstBotQueuedAt < 0f)
                firstBotQueuedAt = Time.time;

            lastBotQueuedAt = Time.time;
            queuedBots.Add(bot);

            Plugin.LogSource.LogInfo(
                $"[ISB SECRET] Queued '{bot.Profile?.Nickname}' for group teleport ({queuedBots.Count}/{expectedBotCount}).");

            if (initialBatchTeleported)
                TeleportBot(bot, queuedBots.Count - 1, Mathf.Max(queuedBots.Count, expectedBotCount));

            return true;
        }

        public static bool TryFindSafeDestinationAround(
            Vector3 center,
            Vector3 avoidPoint,
            float minimumRadius,
            float maximumRadius,
            float minimumDistanceFromAvoidPoint,
            out Vector3 point)
        {
            point = default;

            minimumRadius = Mathf.Max(1f, minimumRadius);
            maximumRadius = Mathf.Max(minimumRadius, maximumRadius);
            minimumDistanceFromAvoidPoint = Mathf.Max(0f, minimumDistanceFromAvoidPoint);

            const int attempts = 32;
            var startingAngle = UnityEngine.Random.Range(0f, 360f);

            for (var i = 0; i < attempts; i++)
            {
                var angle = (startingAngle + i * (360f / attempts)) * Mathf.Deg2Rad;
                var interpolation = (i % 4) / 3f;
                var radius = Mathf.Lerp(minimumRadius, maximumRadius, interpolation);
                var candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                if (!TrySampleNavMesh(candidate, NavMeshSampleRadius, out var sampled))
                    continue;

                if (Vector3.Distance(sampled, avoidPoint) < minimumDistanceFromAvoidPoint)
                    continue;

                point = sampled;
                return true;
            }

            if (TrySampleNavMesh(center, 20f, out var fallback)
                && Vector3.Distance(fallback, avoidPoint) >= minimumDistanceFromAvoidPoint)
            {
                point = fallback;
                return true;
            }

            return false;
        }

        internal static IEnumerator RunSession()
        {
            while (active && Time.time <= hardWindowEnd)
            {
                if (!initialBatchTeleported && ShouldTeleportInitialBatch())
                {
                    TeleportInitialBatch();
                }

                yield return null;
            }

            if (active && !initialBatchTeleported && queuedBots.Count > 0)
            {
                if (requireFullAssemblyBeforeTeleport)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB SECRET] Strict group teleport expired with {queuedBots.Count}/{expectedBotCount} expected bot(s) assembled. " +
                        "No partial teleport was performed.");
                }
                else
                {
                    TeleportInitialBatch();
                }
            }

            FinishSession();
        }

        private static bool ShouldTeleportInitialBatch()
        {
            if (queuedBots.Count == 0 || firstBotQueuedAt < 0f)
                return false;

            if (requireFullAssemblyBeforeTeleport)
            {
                if (!HasCompleteExpectedRoleAssembly() || lastBotQueuedAt < 0f)
                    return false;

                return Time.time - lastBotQueuedAt >= minimumGatherSeconds;
            }

            var elapsedSinceFirstBot = Time.time - firstBotQueuedAt;

            if (queuedBots.Count >= expectedBotCount
                && elapsedSinceFirstBot >= minimumGatherSeconds)
            {
                return true;
            }

            return elapsedSinceFirstBot >= maximumGatherSeconds;
        }

        private static bool HasCompleteExpectedRoleAssembly()
        {
            if (expectedRoleCounts.Count == 0)
                return queuedBots.Count >= expectedBotCount;

            foreach (var pair in expectedRoleCounts)
            {
                queuedRoleCounts.TryGetValue(pair.Key, out var queuedForRole);

                if (queuedForRole < pair.Value)
                    return false;
            }

            return queuedBots.Count >= expectedBotCount;
        }

        private static void TeleportInitialBatch()
        {
            if (initialBatchTeleported)
                return;

            initialBatchTeleported = true;
            queuedBots.Sort(CompareBotPriority);

            var total = queuedBots.Count;

            for (var i = 0; i < total; i++)
                TeleportBot(queuedBots[i], i, total);

            Plugin.LogSource.LogInfo(
                $"[ISB SECRET] Group teleport executed for {total} bot(s) at {destination}.");
        }

        private static void TeleportBot(BotOwner bot, int index, int total)
        {
            if (bot == null || bot.Mover == null)
                return;

            var target = GetFormationPoint(index, total);

            try
            {
                bot.Mover.Stop();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB SECRET] Failed to stop '{bot.Profile?.Nickname}' before teleport: {ex.Message}");
            }

            try
            {
                bot.Mover.Teleport(target);
                Plugin.LogSource.LogInfo(
                    $"[ISB SECRET] Teleported '{bot.Profile?.Nickname}' to {target}.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB SECRET] Failed to teleport '{bot.Profile?.Nickname}': {ex.Message}");
            }
        }

        private static Vector3 GetFormationPoint(int index, int total)
        {
            if (index <= 0 || total <= 1)
                return destination;

            var followerCount = Mathf.Max(1, total - 1);
            var angle = (index - 1) * (Mathf.PI * 2f / followerCount);
            var requested = destination + new Vector3(
                Mathf.Cos(angle) * FormationRadius,
                0f,
                Mathf.Sin(angle) * FormationRadius);

            return TrySampleNavMesh(requested, 5f, out var sampled)
                ? sampled
                : destination;
        }

        private static bool TrySampleNavMesh(Vector3 requestedPoint, float radius, out Vector3 sampledPoint)
        {
            sampledPoint = default;

            if (!NavMesh.SamplePosition(requestedPoint, out var hit, radius, NavMesh.AllAreas))
                return false;

            sampledPoint = hit.position;
            return true;
        }

        private static int CompareBotPriority(BotOwner left, BotOwner right)
        {
            return GetRolePriority(left).CompareTo(GetRolePriority(right));
        }

        private static int GetRolePriority(BotOwner bot)
        {
            if (bot?.Profile?.Info?.Settings == null)
                return 4;

            var role = (int)bot.Profile.Info.Settings.Role;

            switch (role)
            {
                case 13707: // ISBBossCommander
                    return 0;
                case 13708: // ISBFollowerCommander
                    return 1;
                case 13701: // ISBTeamLeader
                    return 2;
                case 13710: // ISBSecondLeader
                    return 3;
                default:
                    return 4;
            }
        }

        private static string GetBotId(BotOwner bot)
        {
            return bot.Profile?.Id
                ?? bot.Profile?.Nickname
                ?? bot.GetHashCode().ToString();
        }

        private static int GetExpectedRoleTotal()
        {
            var total = 0;

            foreach (var count in expectedRoleCounts.Values)
                total += count;

            return Mathf.Max(1, total);
        }

        private static void FinishSession()
        {
            active = false;
            requireFullAssemblyBeforeTeleport = false;
            firstBotQueuedAt = -1f;
            lastBotQueuedAt = -1f;
            queuedBots.Clear();
            queuedBotIds.Clear();
            expectedRoleCounts.Clear();
            queuedRoleCounts.Clear();

            var activeRunner = runner;
            runner = null;

            if (activeRunner != null)
                UnityEngine.Object.Destroy(activeRunner.gameObject);
        }
    }

    public sealed class ISBSecretTeleportRunner : MonoBehaviour
    {
        public void Begin()
        {
            StartCoroutine(ISBSecretTeleport.RunSession());
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }
    }
}
