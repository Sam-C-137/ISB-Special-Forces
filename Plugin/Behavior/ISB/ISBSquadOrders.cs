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
}
