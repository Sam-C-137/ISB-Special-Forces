using System;

namespace ISBSpecialForces.Behavior.ISB
{
    internal enum ISBTacticalState
    {
        Defence,
        Assault,
        Flank,
        SuppressAssault
    }

    internal sealed class ISBLeaderState
    {
        internal ISBTacticalState Status { get; private set; }
        internal float LastStatusChange { get; private set; } = -9999f;
        internal bool UnderAttack { get; private set; }
        internal float UnderAttackUntil { get; private set; }
        internal bool HasDefensivePlace { get; set; }
        internal bool NoAttackAi { get; set; } = true;
        internal int AreaId { get; private set; } = -1;
        internal float LastAssault { get; private set; }
        internal ISBGroupPosition? FlankPoint { get; private set; }

        internal bool IssueOrder(ISBTacticalState requested, int areaId, bool hasTarget, bool targetIsAI,
            ISBGroupPosition? flankPoint, float now, Action<ISBTacticalState> onOrder)
        {
            if (requested != ISBTacticalState.Defence && hasTarget && targetIsAI && NoAttackAi)
                return false;
            if (!hasTarget && (requested == ISBTacticalState.Flank || requested == ISBTacticalState.SuppressAssault))
                throw new ArgumentException("The order requires a target.", nameof(hasTarget));
            ISBTacticalState next = requested == ISBTacticalState.Flank && UnderAttack ?
                ISBTacticalState.SuppressAssault : requested;
            if (areaId > 0)
                AreaId = areaId;
            if (next == ISBTacticalState.Flank)
                FlankPoint = flankPoint;
            SetStatus(next, now);
            if (next == ISBTacticalState.Assault)
                LastAssault = now;
            onOrder?.Invoke(next);
            return true;
        }

        internal void SetStatus(ISBTacticalState status, float now)
        {
            if (Status == status)
                return;
            Status = status;
            LastStatusChange = now;
        }

        internal void BecomeUnderAttack(float now, Action requestDefence)
        {
            if (!UnderAttack)
            {
                UnderAttack = true;
                requestDefence?.Invoke();
            }
            UnderAttackUntil = now + 90f;
        }

        internal void UpdateUnderAttack(float now)
        {
            if (UnderAttack && UnderAttackUntil < now)
                UnderAttack = false;
        }

        internal bool CanPursue(float now, bool hasEnemy, float gameTime, float enemyLastSeen)
        {
            return !HasDefensivePlace && now - LastStatusChange >= 80f &&
                (!hasEnemy || gameTime - enemyLastSeen > 80f);
        }
    }
}
