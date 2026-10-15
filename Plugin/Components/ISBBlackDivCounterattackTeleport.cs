using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Components
{
    internal static class ISBBlackDivCounterattackTeleport
    {
        private const float RingMinimumRadius = 20f;
        private const float RingMaximumRadius = 32f;
        private const float NavMeshSampleRadius = 10f;
        private static readonly List<BotOwner> QueuedBots = new List<BotOwner>();
        private static readonly HashSet<string> QueuedBotIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> OriginalBlackDivisionIds = new HashSet<string>(StringComparer.Ordinal);
        private static bool active;
        private static bool teleported;
        private static int expectedBotCount;
        private static float minimumGatherSeconds;
        private static float maximumGatherSeconds;
        private static float hardWindowEnd;
        private static float firstBotQueuedAt = -1f;
        private static float nextMissingAnchorWarningAt;
        private static Func<BotOwner> anchorResolver;
        private static ISBBlackDivCounterattackTeleportRunner runner;
        public static bool Arm(IReadOnlyCollection<string> originalBlackDivisionIds, Func<BotOwner> dynamicAnchorResolver, int expectedBots, float minimumGather = 1.25f, float maximumGather = 8f, float hardWindowSeconds = 45f)
        {
            Reset();
            if (originalBlackDivisionIds == null || originalBlackDivisionIds.Count == 0)
            {
                Plugin.LogSource.LogWarning("[ISB SECRET] Black Division Counterattack could not arm because no original Black Division anchor IDs were captured.");
                return false;
            }

            if (dynamicAnchorResolver == null)
            {
                Plugin.LogSource.LogWarning("[ISB SECRET] Black Division Counterattack could not arm because the dynamic anchor resolver is missing.");
                return false;
            }

            foreach (var botId in originalBlackDivisionIds)
            {
                if (!string.IsNullOrEmpty(botId))
                    OriginalBlackDivisionIds.Add(botId);
            }

            if (OriginalBlackDivisionIds.Count == 0)
                return false;
            anchorResolver = dynamicAnchorResolver;
            expectedBotCount = Mathf.Max(1, expectedBots);
            minimumGatherSeconds = Mathf.Max(0.25f, minimumGather);
            maximumGatherSeconds = Mathf.Max(minimumGatherSeconds, maximumGather);
            hardWindowEnd = Time.time + Mathf.Max(maximumGatherSeconds + 5f, hardWindowSeconds);
            firstBotQueuedAt = -1f;
            nextMissingAnchorWarningAt = 0f;
            teleported = false;
            active = true;
            var host = new GameObject("ISB.BlackDivCounterattackTeleportRunner");
            runner = host.AddComponent<ISBBlackDivCounterattackTeleportRunner>();
            runner.Begin();
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB SECRET] Black Division Counterattack teleport armed. Waiting for up to {expectedBotCount} new blackDivAssault bot(s); original bots are anchor-only.");
            }

            return true;
        }

        public static void Reset()
        {
            active = false;
            teleported = false;
            expectedBotCount = 0;
            firstBotQueuedAt = -1f;
            nextMissingAnchorWarningAt = 0f;
            anchorResolver = null;
            QueuedBots.Clear();
            QueuedBotIds.Clear();
            OriginalBlackDivisionIds.Clear();
            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
                runner = null;
            }
        }

        public static bool TryQueue(BotOwner bot)
        {
            if (!active || bot?.Profile?.Info?.Settings == null)
                return false;
            if (Time.time > hardWindowEnd)
                return false;
            if (!ISBBlackDivisionUtility.IsBlackDivAssaultRole(bot.Profile.Info.Settings.Role))
                return false;
            var botId = ISBBlackDivisionUtility.GetBotId(bot);
            if (string.IsNullOrEmpty(botId) || OriginalBlackDivisionIds.Contains(botId) || !QueuedBotIds.Add(botId))
            {
                return false;
            }

            if (firstBotQueuedAt < 0f)
                firstBotQueuedAt = Time.time;
            QueuedBots.Add(bot);
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB SECRET] Queued Black Division reinforcement '{bot.Profile?.Nickname}' " + $"({QueuedBots.Count}/{expectedBotCount}).");
            }

            if (teleported)
                TeleportLateBot(bot, QueuedBots.Count - 1, Mathf.Max(QueuedBots.Count, expectedBotCount));
            return true;
        }

        internal static IEnumerator RunSession()
        {
            while (active && Time.time <= hardWindowEnd)
            {
                if (!teleported && ShouldTeleport())
                    TryTeleportInitialBatch();
                yield return null;
            }

            if (active && !teleported && QueuedBots.Count > 0)
                TryTeleportInitialBatch();
            FinishSession();
        }

        private static bool ShouldTeleport()
        {
            if (QueuedBots.Count == 0 || firstBotQueuedAt < 0f)
                return false;
            var elapsed = Time.time - firstBotQueuedAt;
            if (QueuedBots.Count >= expectedBotCount && elapsed >= minimumGatherSeconds)
                return true;
            return elapsed >= maximumGatherSeconds;
        }

        private static void TryTeleportInitialBatch()
        {
            if (teleported)
                return;
            var anchor = ResolveAnchor();
            if (anchor == null)
            {
                if (Time.time >= nextMissingAnchorWarningAt)
                {
                    nextMissingAnchorWarningAt = Time.time + 5f;
                    Plugin.LogSource.LogWarning("[ISB SECRET] Black Division Counterattack has reinforcements ready, but no original Black Division bot is still fighting the ISB. Waiting for another valid anchor.");
                }

                return;
            }

            QueuedBots.RemoveAll(bot => !ISBBlackDivisionUtility.IsAlive(bot));
            if (QueuedBots.Count == 0)
                return;
            teleported = true;
            var total = QueuedBots.Count;
            var startAngle = UnityEngine.Random.Range(0f, 360f);
            for (var i = 0; i < total; i++)
                TeleportAroundAnchor(QueuedBots[i], anchor, i, total, startAngle);
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB SECRET] Black Division Counterattack deployed {total} reinforcement(s) in a {RingMinimumRadius:F0}-{RingMaximumRadius:F0}m ring around original fighter '{anchor.Profile?.Nickname}' at {anchor.Position}.");
            }
        }

        private static void TeleportLateBot(BotOwner bot, int index, int total)
        {
            var anchor = ResolveAnchor();
            if (anchor == null)
                return;
            TeleportAroundAnchor(bot, anchor, index, total, UnityEngine.Random.Range(0f, 360f));
        }

        private static BotOwner ResolveAnchor()
        {
            try
            {
                var anchor = anchorResolver?.Invoke();
                if (!ISBBlackDivisionUtility.IsAlive(anchor))
                    return null;
                var anchorId = ISBBlackDivisionUtility.GetBotId(anchor);
                return !string.IsNullOrEmpty(anchorId) && OriginalBlackDivisionIds.Contains(anchorId) ? anchor : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TeleportAroundAnchor(BotOwner bot, BotOwner anchor, int index, int total, float startAngleDegrees)
        {
            if (!ISBBlackDivisionUtility.IsAlive(bot) || anchor == null || bot.Mover == null)
                return;
            var count = Mathf.Max(1, total);
            var angleDegrees = startAngleDegrees + index * (360f / count);
            var angle = angleDegrees * Mathf.Deg2Rad;
            var radiusInterpolation = count <= 1 ? 0.5f : (index % 3) / 2f;
            var radius = Mathf.Lerp(RingMinimumRadius, RingMaximumRadius, radiusInterpolation);
            var requested = anchor.Position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (!TrySampleNavMesh(requested, NavMeshSampleRadius, out var target))
            {
                Plugin.LogSource.LogWarning($"[ISB SECRET] No NavMesh point was found for Black Division reinforcement '{bot.Profile?.Nickname}' near ring point {requested}.");
                return;
            }

            try
            {
                bot.Mover.Stop();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[ISB SECRET] Failed to stop Black Division reinforcement '{bot.Profile?.Nickname}' before teleport: {ex.Message}");
            }

            try
            {
                bot.Mover.Teleport(target);
                if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                {
                    Plugin.LogSource.LogInfo($"[ISB SECRET] Teleported Black Division reinforcement '{bot.Profile?.Nickname}' to {target} around original anchor '{anchor.Profile?.Nickname}'.");
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool TrySampleNavMesh(Vector3 requestedPoint, float radius, out Vector3 sampledPoint)
        {
            sampledPoint = default;
            if (!NavMesh.SamplePosition(requestedPoint, out var hit, radius, NavMesh.AllAreas))
                return false;
            sampledPoint = hit.position;
            return true;
        }

        private static void FinishSession()
        {
            active = false;
            anchorResolver = null;
            firstBotQueuedAt = -1f;
            nextMissingAnchorWarningAt = 0f;
            QueuedBots.Clear();
            QueuedBotIds.Clear();
            OriginalBlackDivisionIds.Clear();
            var activeRunner = runner;
            runner = null;
            if (activeRunner != null)
                UnityEngine.Object.Destroy(activeRunner.gameObject);
        }
    }

    public sealed class ISBBlackDivCounterattackTeleportRunner : MonoBehaviour
    {
        public void Begin()
        {
            StartCoroutine(ISBBlackDivCounterattackTeleport.RunSession());
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }
    }
}
