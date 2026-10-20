using System;
using System.Runtime.CompilerServices;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBRetreatCover
    {
        private sealed class CoverPath
        {
            internal readonly NavMeshPath Path = new NavMeshPath();
            internal CustomNavigationPoint SafeCover;
            internal Vector3 Threat;
            internal float CheckedAt;
        }
        private static readonly ConditionalWeakTable<BotOwner, CoverPath> Paths = new ConditionalWeakTable<BotOwner, CoverPath>();
        private static readonly ConditionalWeakTable<BotOwner, CoverPath>.CreateValueCallback CreatePath = bot => new CoverPath();
        private static Vector3? Threat(BotOwner bot, CoverSearchData data)
        {
            Vector3? threat = bot.Memory.GoalEnemy?.EnemyLastPosition;
            if (!threat.HasValue && bot.Memory.LastDamageDataActive)
                threat = bot.Memory.LastDamageData.Postion;
            if (!threat.HasValue && Time.time - bot.Memory.LastTimeHit < 20f)
                threat = bot.Memory.LastHitPos;
            if (!threat.HasValue && data?.CarePositions != null)
            {
                float nearest = float.MaxValue;
                foreach (var point in data.CarePositions)
                {
                    float distance = (point - bot.Position).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    threat = point;
                }
            }
            return threat;
        }

        internal static void DiscardUnsafe(BotOwner bot)
        {
            if (bot.Memory.IsInCover || bot.Memory.CurCustomCoverPoint == null) return;
            var threat = Threat(bot, null);
            if (!threat.HasValue) return;
            if (!ISBNavigationBudget.TryBeginSearch(Time.frameCount))
                return;
            var cached = Paths.GetValue(bot, CreatePath);
            var path = cached.Path;
            if (NavMesh.CalculatePath(bot.Position, bot.Memory.CurCustomCoverPoint.Position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete && SafePath(bot.Position, threat.Value, path.corners))
            {
                cached.SafeCover = bot.Memory.CurCustomCoverPoint;
                cached.Threat = threat.Value;
                cached.CheckedAt = Time.time;
                return;
            }
            cached.SafeCover = null;
            bot.Memory.SetCoverPoints(null);
            bot.StopMove();
        }

        internal static CustomNavigationPoint Find(BotOwner bot, CoverSearchData data,
            Func<CoverSearchData, bool, CustomNavigationPoint> find, bool checkCurrent)
        {
            var threat = Threat(bot, data);
            if (!threat.HasValue) return find(data, checkCurrent);
            var away = bot.Position - threat.Value;
            away.y = 0f;
            if (away.sqrMagnitude < 0.25f) return find(data, checkCurrent);
            away.Normalize();
            var side = Vector3.Cross(Vector3.up, away);
            var center = data.CenterPos;
            float maxDistance = data.MaxDistSqr;
            var cached = Paths.GetValue(bot, CreatePath);
            var path = cached.Path;
            try
            {
                data.MaxDistSqr = Mathf.Min(maxDistance, 100f);
                for (int i = 0; i < 3; i++)
                {
                    if (!ISBNavigationBudget.TryBeginSearch(Time.frameCount))
                        return cached.SafeCover != null && Time.time - cached.CheckedAt < 1f && (threat.Value - cached.Threat).sqrMagnitude < 1f ? cached.SafeCover : null;
                    data.CenterPos = bot.Position + away * 4f + side * (i == 0 ? 0f : i == 1 ? 5f : -5f);
                    var cover = find(data, checkCurrent && i == 0);
                    if (cover != null && NavMesh.CalculatePath(bot.Position, cover.Position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete && SafePath(bot.Position, threat.Value, path.corners))
                    {
                        cached.SafeCover = cover;
                        cached.Threat = threat.Value;
                        cached.CheckedAt = Time.time;
                        return cover;
                    }
                }
                return null;
            }
            finally
            {
                data.CenterPos = center;
                data.MaxDistSqr = maxDistance;
            }
        }

        internal static bool SafePath(Vector3 origin, Vector3 threat, Vector3[] corners)
        {
            if (corners == null || corners.Length == 0) return false;
            var toward = threat - origin;
            toward.y = 0f;
            float distance = toward.magnitude;
            if (distance < 0.5f) return true;
            toward /= distance;
            float forwardAllowance = Mathf.Max(1.5f, Mathf.Min(3f, distance * 0.15f));
            float clearance = Mathf.Min(8f, distance * 0.65f);
            var previous = origin;
            foreach (var corner in corners)
            {
                if (Vector3.Dot(corner - origin, toward) > forwardAllowance) return false;
                var segment = corner - previous;
                float t = segment.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector3.Dot(threat - previous, segment) / segment.sqrMagnitude) : 0f;
                if ((previous + segment * t - threat).sqrMagnitude < clearance * clearance) return false;
                previous = corner;
            }
            return true;
        }
    }
}
