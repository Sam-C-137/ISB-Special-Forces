using System.Collections.Generic;

namespace ISBSpecialForces.Behavior.ISB
{
    internal struct ISBGroupPosition
    {
        internal float X, Y, Z;
        internal ISBGroupPosition(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    internal readonly struct ISBAssaultMember
    {
        internal bool HasEnemy { get; }
        internal float EnemyDistance { get; }
        internal bool RequiresReadiness { get; }
        internal bool HasReadiness { get; }
        internal float ReadyAt { get; }

        internal ISBAssaultMember(bool hasEnemy, float enemyDistance, bool requiresReadiness, bool hasReadiness, float readyAt)
        {
            HasEnemy = hasEnemy;
            EnemyDistance = enemyDistance;
            RequiresReadiness = requiresReadiness;
            HasReadiness = hasReadiness;
            ReadyAt = readyAt;
        }
    }

    internal static class ISBLeaderRules
    {
        internal static bool IsReady(bool registered, float readyAt, float now) => registered && readyAt < now;

        internal static bool CanHelp(bool hasDefensivePlace, ISBGroupPosition helper, ISBGroupPosition requester,
            IReadOnlyList<float> personalContactAges)
        {
            if (hasDefensivePlace)
                return false;
            float x = helper.X - requester.X;
            float y = helper.Y - requester.Y;
            float z = helper.Z - requester.Z;
            if (y * y + x * x + z * z > 10000f)
                return false;
            for (int i = 0; i < personalContactAges.Count; i++)
                if (personalContactAges[i] < 20f)
                    return false;
            return true;
        }

        internal static bool CanStartAssault(bool hasDefensivePlace, IReadOnlyList<ISBAssaultMember> members, float now)
        {
            if (hasDefensivePlace)
                return false;
            for (int i = 0; i < members.Count; i++)
            {
                ISBAssaultMember member = members[i];
                if (!member.HasEnemy || member.EnemyDistance >= 30f)
                    return false;
                if (member.RequiresReadiness && !IsReady(member.HasReadiness, member.ReadyAt, now))
                    return false;
            }
            return true;
        }
    }
}
