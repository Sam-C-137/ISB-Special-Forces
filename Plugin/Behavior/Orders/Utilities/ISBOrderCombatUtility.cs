using EFT;
using ISBSpecialForces.Behavior.Orders.State;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Behavior.Orders.Utilities
{
    internal static class ISBOrderCombatUtility
    {
        private static readonly BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] EnemyPositionMemberNames =
        {
            "CurrPosition",
            "EnemyLastPosition",
            "EnemyLastPositionReal",
            "EnemyLastPositionCheck",
            "PersonalLastPos",
            "Position"
        };

        public static bool IsAlive(BotOwner botOwner)
        {
            if (botOwner == null || botOwner.IsDead)
                return false;

            var player = botOwner.GetPlayer;
            return player != null
                && player.HealthController != null
                && player.HealthController.IsAlive;
        }

        public static bool TryGetEnemyPosition(BotOwner botOwner, out Vector3 position)
        {
            return TryGetEnemySnapshot(botOwner, out position, out _);
        }

        public static bool TryGetEnemySnapshot(
            BotOwner botOwner,
            out Vector3 position,
            out bool visible)
        {
            position = default;
            visible = false;

            if (!IsAlive(botOwner) || botOwner.Memory == null)
                return false;

            try
            {
                var enemy = GetMemberValue(botOwner.Memory, "GoalEnemy");

                if (enemy == null)
                    return false;

                visible = TryReadVisibility(enemy);

                foreach (var memberName in EnemyPositionMemberNames)
                {
                    if (TryReadVector3(enemy, memberName, out position) && IsValidWorldPosition(position))
                        return true;
                }

                var person = GetMemberValue(enemy, "Person");

                if (person != null
                    && TryReadVector3(person, "Position", out position)
                    && IsValidWorldPosition(position))
                {
                    return true;
                }
            }
            catch
            {
                
            }

            position = default;
            visible = false;
            return false;
        }

        public static bool TryFindCoverPoint(
            BotOwner botOwner,
            Vector3 desiredCenter,
            float searchRadius,
            out CustomNavigationPoint point)
        {
            point = null;

            if (!IsAlive(botOwner)
                || botOwner.BotsGroup == null
                || botOwner.BotsGroup.CoverPointMaster == null
                || botOwner.CoverSearchInfo == null
                || botOwner.Settings?.FileSettings?.Cover == null
                || !IsValidWorldPosition(desiredCenter))
            {
                return false;
            }

            try
            {
                var radius = Mathf.Max(3f, searchRadius);
                var searchData = new CoverSearchData(
                    desiredCenter,
                    botOwner.CoverSearchInfo,
                    CoverShootType.hide,
                    radius * radius,
                    0f,
                    CoverSearchType.distToToCenter,
                    null,
                    null,
                    null,
                    ECheckSHootHide.shootAndHide,
                    new CoverSearchDefenceDataClass(
                        botOwner.Settings.FileSettings.Cover.MIN_DEFENCE_LEVEL),
                    PointsArrayType.byShootType,
                    true);

                point = botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
                return point != null && IsValidWorldPosition(point.Position);
            }
            catch (Exception ex)
            {
                if (ISBOrderSettings.Logs)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB ORDER SYSTEM] Cover search failed for '{botOwner.Profile?.Nickname}': {ex.Message}");
                }

                point = null;
                return false;
            }
        }

        public static bool TrySampleNavMesh(Vector3 desiredPosition, float radius, out Vector3 sampledPosition)
        {
            sampledPosition = default;

            if (!IsValidWorldPosition(desiredPosition))
                return false;

            if (NavMesh.SamplePosition(
                    desiredPosition,
                    out var hit,
                    Mathf.Max(1f, radius),
                    NavMesh.AllAreas))
            {
                sampledPosition = hit.position;
                return IsValidWorldPosition(sampledPosition);
            }

            return false;
        }

        public static void LookAtPosition(BotOwner botOwner, Vector3 position)
        {
            if (!IsAlive(botOwner) || botOwner.Steering == null || !IsValidWorldPosition(position))
                return;

            try
            {
                var steering = botOwner.Steering;
                var steeringType = steering.GetType();
                var methodNames = new[] { "LookToPoint", "LookToPosition", "LookToEnemy" };

                foreach (var methodName in methodNames)
                {
                    var methods = steeringType.GetMethods(MemberFlags);

                    foreach (var method in methods)
                    {
                        if (method.Name != methodName)
                            continue;

                        var parameters = method.GetParameters();

                        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Vector3))
                            continue;

                        method.Invoke(steering, new object[] { position });
                        return;
                    }
                }
            }
            catch
            {
                
            }

            botOwner.Steering.LookToMovingDirection();
        }

        public static Vector3 HorizontalDirection(Vector3 from, Vector3 to)
        {
            var direction = to - from;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
                return Vector3.forward;

            return direction.normalized;
        }

        public static bool IsValidWorldPosition(Vector3 position)
        {
            return IsFinite(position.x)
                && IsFinite(position.y)
                && IsFinite(position.z)
                && position.sqrMagnitude > 0.01f;
        }

        public static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        public static int GetCommanderPriority(BotOwner botOwner)
        {
            if (botOwner?.Profile?.Info?.Settings == null)
                return int.MaxValue;

            switch ((int)botOwner.Profile.Info.Settings.Role)
            {
                case 13707: // ISBBossCommander
                    return 0;
                case 13709: // ISBTeamLeader
                    return 1;
                case 13708: // ISBFollowerCommander
                    return 2;
                case 13710: // ISBSecondLeader
                    return 3;
                default:
                    return 4;
            }
        }


        private static bool TryReadVisibility(object enemy)
        {
            var directNames = new[]
            {
                "IsVisible",
                "Visible",
                "VisibleNow",
                "IsVisibleNow",
                "CanShoot",
                "IsSeen",
                "Seen",
                "HaveSeen",
                "IsVisibleForBot",
                "IsVisibleForAI"
            };

            var foundBooleanSignal = false;

            foreach (var name in directNames)
            {
                var value = GetMemberValue(enemy, name);
                if (!(value is bool flag))
                    continue;

                foundBooleanSignal = true;
                if (flag)
                    return true;
            }

            var nestedNames = new[] { "EnemyInfo", "Person", "Vision", "EnemyData" };

            foreach (var nestedName in nestedNames)
            {
                var nested = GetMemberValue(enemy, nestedName);
                if (nested == null)
                    continue;

                foreach (var name in directNames)
                {
                    var value = GetMemberValue(nested, name);
                    if (!(value is bool flag))
                        continue;

                    foundBooleanSignal = true;
                    if (flag)
                        return true;
                }
            }

            if (foundBooleanSignal)
                return false;

            var timeNames = new[]
            {
                "TimeLastSeen",
                "LastSeenTime",
                "PersonalSeenTime",
                "LastVisibleTime",
                "TimeSeen"
            };

            foreach (var name in timeNames)
            {
                if (TryReadRecentTimestamp(enemy, name))
                    return true;
            }

            foreach (var nestedName in nestedNames)
            {
                var nested = GetMemberValue(enemy, nestedName);
                if (nested == null)
                    continue;

                foreach (var name in timeNames)
                {
                    if (TryReadRecentTimestamp(nested, name))
                        return true;
                }
            }

            return false;
        }

        private static bool TryReadRecentTimestamp(object owner, string memberName)
        {
            var raw = GetMemberValue(owner, memberName);
            if (raw == null)
                return false;

            float timestamp;

            try
            {
                timestamp = Convert.ToSingle(raw);
            }
            catch
            {
                return false;
            }

            if (!IsFinite(timestamp) || timestamp < 0f || timestamp > Time.time + 5f)
                return false;

            return Time.time - timestamp <= 1.25f;
        }

        private static bool TryReadVector3(object instance, string memberName, out Vector3 value)
        {
            value = default;
            var result = GetMemberValue(instance, memberName);

            if (result is Vector3 vector)
            {
                value = vector;
                return true;
            }

            return false;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
                return null;

            var type = instance.GetType();
            var property = type.GetProperty(memberName, MemberFlags);

            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            var field = type.GetField(memberName, MemberFlags)
                ?? type.GetField("_" + char.ToLowerInvariant(memberName[0]) + memberName.Substring(1), MemberFlags);

            return field?.GetValue(instance);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
