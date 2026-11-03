using System;
using System.Collections.Generic;

namespace ISBSpecialForces.Behavior.ISB
{
    internal readonly struct ISBContact
    {
        internal string Id { get; }
        internal bool IsAI { get; }
        internal bool Visible { get; }
        internal float Age { get; }
        internal float Distance { get; }
        internal ISBContact(string id, bool isAI, bool visible, float age, float distance)
        {
            Id = id;
            IsAI = isAI;
            Visible = visible;
            Age = age;
            Distance = distance;
        }
    }

    internal sealed class ISBSquadOrders
    {
        internal ISBLeaderState Leader { get; } = new ISBLeaderState();
        internal string EnemyId { get; private set; }
        internal int Revision { get; private set; }
        private float _nextDecision;
        private float _nextAssault;
        private float _lastDistance, _lastDistanceAt;

        internal void Update(float now, ISBContact? contact, bool hit, bool defensivePlace,
            IReadOnlyList<ISBAssaultMember> members, Func<bool> chooseAssault)
        {
            Leader.HasDefensivePlace = defensivePlace;
            Leader.UpdateUnderAttack(now);
            if (hit)
                Leader.BecomeUnderAttack(now, () => Issue(ISBTacticalState.Defence, now, null));
            if (!contact.HasValue)
            {
                if (EnemyId != null || Leader.Status != ISBTacticalState.Defence)
                    Issue(ISBTacticalState.Defence, now, null);
                EnemyId = null;
                return;
            }
            if (EnemyId != contact.Value.Id)
            {
                EnemyId = contact.Value.Id;
                _nextAssault = now + 5f;
                Issue(ISBTacticalState.Defence, now, contact);
                _lastDistance = contact.Value.Distance;
                _lastDistanceAt = now;
            }
            bool retreating = now - _lastDistanceAt <= 15f && contact.Value.Distance > _lastDistance;
            _lastDistance = contact.Value.Distance;
            _lastDistanceAt = now;
            if (defensivePlace)
            {
                if (Leader.Status != ISBTacticalState.Defence)
                    Issue(ISBTacticalState.Defence, now, contact);
                return;
            }
            if (Leader.Status == ISBTacticalState.Flank && Leader.UnderAttack)
                Issue(ISBTacticalState.SuppressAssault, now, contact);
            if (now < _nextDecision)
                return;
            _nextDecision = now + 1f;
            if (contact.Value.IsAI && Leader.NoAttackAi)
                return;
            if (ISBLeaderRules.CanStartAssault(false, members, now))
            {
                if (Leader.Status != ISBTacticalState.Assault)
                    Issue(ISBTacticalState.Assault, now, contact);
                return;
            }
            if (Leader.Status == ISBTacticalState.Defence && now >= _nextAssault &&
                retreating && contact.Value.Age >= 9f && contact.Value.Age <= 40f)
                Issue(chooseAssault() ? ISBTacticalState.Assault : ISBTacticalState.SuppressAssault, now, contact);
        }

        internal bool Flank(float now, ISBContact contact, ISBGroupPosition point)
        {
            bool changed = Leader.IssueOrder(ISBTacticalState.Flank, -1, true, contact.IsAI, point, now, _ => Revision++);
            return changed;
        }

        private void Issue(ISBTacticalState state, float now, ISBContact? contact)
        {
            Leader.IssueOrder(state, -1, contact.HasValue, contact?.IsAI ?? false, null, now, _ => Revision++);
        }
    }

    internal static class ISBSquadCoordination
    {
        internal static float FocusScore(bool visible, bool canShoot, bool hit, bool leader, float age, float distance) =>
            (visible ? 200f : 0f) + (visible && canShoot ? 40f : 0f) + (hit ? 80f : 0f) + (leader ? 12f : 0f) -
            Math.Min(120f, Math.Max(0f, distance)) * 0.25f - Math.Min(20f, Math.Max(0f, age)) * 2f;

        internal static bool KeepFocus(float currentScore, float alternativeScore, float heldSeconds) =>
            alternativeScore < currentScore + (heldSeconds < 2.5f ? 45f : 15f);

        internal static bool Regroup(bool active, bool follower, bool fixedPost, bool treating,
            bool canEngage, bool immediateThreat, bool hasContact, bool flanking, float distance, float floorGap)
        {
            if (!follower || fixedPost || treating || canEngage || immediateThreat ||
                float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
                return false;
            float start = flanking ? 28f : hasContact ? 18f : 12f;
            float stop = flanking ? 14f : hasContact ? 9f : 6f;
            return active ? distance > stop || floorGap > 2.5f : distance > start || floorGap > 2.5f && distance > 6f;
        }
    }

}
