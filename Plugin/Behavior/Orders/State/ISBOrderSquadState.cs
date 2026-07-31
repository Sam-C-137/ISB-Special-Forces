using EFT;
using ISBSpecialForces.Behavior.Orders.Utilities;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Orders.State
{
    public sealed class ISBOrderSquadState
    {
        private readonly List<BotOwner> _members = new List<BotOwner>();
        private readonly Dictionary<BotOwner, ISBOrderRole> _roles =
            new Dictionary<BotOwner, ISBOrderRole>();

        private float _nextDecisionTime;
        private float _orderExpiresAt;
        private float _orderStartedAt;
        private float _casualtyDefenceUntil;
        private float _pursuitCooldownUntil;
        private int _lastAliveCount;

        public ISBOrderSquadState(BotsGroup group)
        {
            Group = group;
        }

        public BotsGroup Group { get; }
        public IReadOnlyList<BotOwner> Members => _members;
        public BotOwner Commander { get; private set; }
        public ISBOrderType CurrentOrder { get; private set; } = ISBOrderType.None;
        public int OrderRevision { get; private set; }
        public Vector3 EnemyPosition { get; private set; }
        public Vector3 AnchorPosition { get; private set; }
        public bool HasEnemy { get; private set; }
        public bool EnemyVisibleNow { get; private set; }
        public bool EnemyRemembered => HasEnemy && !EnemyVisibleNow;
        public float LastEnemySeenTime { get; private set; } = -1f;
        public float MaximumMemberDistanceFromAnchor { get; private set; }
        public bool LostMemberSinceLastDecision { get; private set; }
        public float CurrentOrderAge => CurrentOrder == ISBOrderType.None ? 0f : Time.time - _orderStartedAt;

        public bool IsEmpty => _members.Count == 0;

        public void AddMember(BotOwner botOwner)
        {
            if (botOwner == null || _members.Contains(botOwner))
                return;

            _members.Add(botOwner);
            _lastAliveCount = _members.Count;
            AssignRoles();
        }

        public void RemoveMember(BotOwner botOwner)
        {
            if (botOwner == null)
                return;

            _members.Remove(botOwner);
            _roles.Remove(botOwner);

            if (Commander == botOwner)
                Commander = null;

            AssignRoles();
        }

        public void Update(float now)
        {
            PruneDeadMembers();

            if (_members.Count == 0)
                return;

            SelectCommander();
            AssignRoles();
            UpdateAnchorAndSpread();
            UpdateEnemy(now);

            var aliveCount = _members.Count;
            LostMemberSinceLastDecision = _lastAliveCount > 0 && aliveCount < _lastAliveCount;

            if (LostMemberSinceLastDecision)
            {
                _casualtyDefenceUntil = Mathf.Max(
                    _casualtyDefenceUntil,
                    now + ISBOrderSettings.Current.casualtyDefenceDuration);
            }

            _lastAliveCount = aliveCount;

            if (!HasEnemy)
            {
                SetOrder(ISBOrderType.None, now, 0f);
                return;
            }

            if (now < _nextDecisionTime)
                return;

            _nextDecisionTime = now + ISBOrderSettings.Current.decisionInterval;
            SelectOrder(now);
        }

        public ISBOrderRole GetRole(BotOwner botOwner)
        {
            if (botOwner != null && _roles.TryGetValue(botOwner, out var role))
                return role;

            return ISBOrderRole.Support;
        }

        public ISBOrderDirective GetDirective(BotOwner botOwner)
        {
            if (!HasEnemy || botOwner == null)
                return ISBOrderDirective.None;

            var role = GetRole(botOwner);

            switch (CurrentOrder)
            {
                case ISBOrderType.Defence:
                    return ISBOrderDirective.None;

                case ISBOrderType.SuppressAssault:
                    return role == ISBOrderRole.Commander || role == ISBOrderRole.Support
                        ? ISBOrderDirective.None
                        : ISBOrderDirective.Assault;

                case ISBOrderType.Flank:
                    return role == ISBOrderRole.Commander || role == ISBOrderRole.Support
                        ? ISBOrderDirective.None
                        : ISBOrderDirective.Flank;

                case ISBOrderType.Assault:
                    if (_members.Count <= 2
                        || role == ISBOrderRole.Commander
                        || role == ISBOrderRole.Support)
                    {
                        return ISBOrderDirective.None;
                    }

                    return ISBOrderDirective.Assault;

                case ISBOrderType.Pursuit:
                    return ISBOrderDirective.Pursuit;

                case ISBOrderType.Regroup:
                    return ISBOrderDirective.None;

                default:
                    return ISBOrderDirective.None;
            }
        }

        private void SelectOrder(float now)
        {
            var settings = ISBOrderSettings.Current;
            var enemyDistance = ISBOrderCombatUtility.HorizontalDistance(
                AnchorPosition,
                EnemyPosition);

            if (now < _casualtyDefenceUntil)
            {
                SetOrder(ISBOrderType.Defence, now, _casualtyDefenceUntil - now);
                return;
            }

            if (CurrentOrder == ISBOrderType.Regroup)
            {
                SetOrder(ISBOrderType.Defence, now, 4f);
                return;
            }

            if (CurrentOrder == ISBOrderType.Pursuit)
            {
                if (enemyDistance <= settings.pursuitExitDistance
                    || now >= _orderExpiresAt)
                {
                    _pursuitCooldownUntil = now + settings.pursuitCooldownDuration;
                    SetOrder(ChoosePostPursuitOrder(enemyDistance), now);
                }

                return;
            }

            if (CurrentOrder != ISBOrderType.None && now < _orderExpiresAt)
                return;

            var pursuitReady = now >= _pursuitCooldownUntil;
            var shouldPursue = pursuitReady
                && enemyDistance >= settings.pursuitEnterDistance
                && (EnemyRemembered
                    || CurrentOrder == ISBOrderType.None
                    || CurrentOrder == ISBOrderType.Defence
                    || CurrentOrder == ISBOrderType.Assault);

            if (shouldPursue)
            {
                SetOrder(ISBOrderType.Pursuit, now, settings.pursuitOrderDuration);
                return;
            }

            if (_members.Count <= 2)
            {
                SetOrder(ISBOrderType.Defence, now);
                return;
            }

            if (enemyDistance <= settings.assaultEnterDistance)
            {
                SetOrder(ISBOrderType.Assault, now);
                return;
            }

            switch (CurrentOrder)
            {
                case ISBOrderType.None:
                    SetOrder(ISBOrderType.Defence, now);
                    break;

                case ISBOrderType.Defence:
                    SetOrder(
                        _members.Count >= 4
                            ? ISBOrderType.SuppressAssault
                            : ISBOrderType.Flank,
                        now);
                    break;

                case ISBOrderType.SuppressAssault:
                    SetOrder(ISBOrderType.Flank, now);
                    break;

                case ISBOrderType.Flank:
                    SetOrder(ISBOrderType.Assault, now);
                    break;

                case ISBOrderType.Assault:
                    SetOrder(ISBOrderType.Defence, now);
                    break;

                default:
                    SetOrder(ISBOrderType.Defence, now);
                    break;
            }
        }

        private ISBOrderType ChoosePostPursuitOrder(float enemyDistance)
        {
            if (_members.Count <= 2)
                return ISBOrderType.Defence;

            if (enemyDistance <= ISBOrderSettings.Current.assaultEnterDistance)
                return ISBOrderType.Assault;

            return _members.Count >= 4
                ? ISBOrderType.SuppressAssault
                : ISBOrderType.Flank;
        }

        private void SetOrder(
            ISBOrderType order,
            float now,
            float explicitDuration = -1f)
        {
            if (CurrentOrder == order && now < _orderExpiresAt)
                return;

            var previous = CurrentOrder;
            CurrentOrder = order;
            _orderStartedAt = now;

            if (previous != order)
            {
                unchecked
                {
                    OrderRevision++;
                }
            }

            if (order == ISBOrderType.None)
            {
                _orderExpiresAt = 0f;
            }
            else
            {
                var settings = ISBOrderSettings.Current;
                var duration = explicitDuration >= 0f
                    ? Mathf.Max(1f, explicitDuration)
                    : Random.Range(settings.minimumOrderDuration, settings.maximumOrderDuration);

                _orderExpiresAt = now + duration;
            }

            if (ISBOrderSettings.Logs && previous != order)
            {
                var commanderName = Commander?.Profile?.Nickname ?? "unknown commander";
                var enemyDistance = HasEnemy
                    ? ISBOrderCombatUtility.HorizontalDistance(AnchorPosition, EnemyPosition)
                    : 0f;
            }
        }

        private void UpdateEnemy(float now)
        {
            EnemyVisibleNow = false;
            var foundRememberedEnemy = false;
            var rememberedPosition = default(Vector3);

            foreach (var member in _members)
            {
                if (!ISBOrderCombatUtility.TryGetEnemySnapshot(
                        member,
                        out var position,
                        out var visible))
                {
                    continue;
                }

                if (visible)
                {
                    EnemyPosition = position;
                    HasEnemy = true;
                    EnemyVisibleNow = true;
                    LastEnemySeenTime = now;
                    return;
                }

                if (!foundRememberedEnemy)
                {
                    foundRememberedEnemy = true;
                    rememberedPosition = position;
                }
            }

            if (foundRememberedEnemy)
            {
                if (LastEnemySeenTime >= 0f
                    && now - LastEnemySeenTime <= ISBOrderSettings.Current.enemyMemoryDuration)
                {
                    EnemyPosition = rememberedPosition;
                    HasEnemy = true;
                    return;
                }
            }
            else if (HasEnemy
                && LastEnemySeenTime >= 0f
                && now - LastEnemySeenTime <= ISBOrderSettings.Current.enemyMemoryDuration)
            {
                return;
            }

            var hadEnemy = HasEnemy;
            var rememberedFor = LastEnemySeenTime < 0f
                ? 0f
                : Mathf.Max(0f, now - LastEnemySeenTime);

            HasEnemy = false;
            EnemyVisibleNow = false;
            EnemyPosition = default;
            LastEnemySeenTime = -1f;

            if (hadEnemy)
                ReleaseForHunt(rememberedFor);
        }

        private void ReleaseForHunt(float rememberedFor)
        {
            foreach (var member in _members)
            {
                try
                {
                    member?.PatrollingData?.Unpause();
                }
                catch
                {
                    
                }
            }

            if (ISBOrderSettings.Logs)
            {
                var commanderName = Commander?.Profile?.Nickname ?? "unknown commander";
            }
        }

        private void SelectCommander()
        {
            Commander = _members
                .OrderBy(ISBOrderCombatUtility.GetCommanderPriority)
                .FirstOrDefault();
        }

        private void AssignRoles()
        {
            _roles.Clear();

            if (_members.Count == 0)
                return;

            SelectCommander();

            if (Commander != null)
                _roles[Commander] = ISBOrderRole.Commander;

            var others = _members
                .Where(member => member != Commander)
                .OrderBy(ISBOrderCombatUtility.GetCommanderPriority)
                .ToList();

            var supportMember = others.FirstOrDefault(member =>
                    member?.Profile?.Info?.Settings != null
                    && (int)member.Profile.Info.Settings.Role == 13708)
                ?? others.FirstOrDefault(member =>
                    member?.Profile?.Info?.Settings != null
                    && (int)member.Profile.Info.Settings.Role == 13701)
                ?? others.FirstOrDefault();

            if (supportMember != null)
                _roles[supportMember] = ISBOrderRole.Support;

            var left = true;

            foreach (var member in others)
            {
                if (member == supportMember)
                    continue;

                _roles[member] = left
                    ? ISBOrderRole.ManeuverLeft
                    : ISBOrderRole.ManeuverRight;

                left = !left;
            }
        }

        private void UpdateAnchorAndSpread()
        {
            if (Commander != null && ISBOrderCombatUtility.IsAlive(Commander))
            {
                AnchorPosition = Commander.Position;
            }
            else
            {
                var sum = Vector3.zero;

                foreach (var member in _members)
                    sum += member.Position;

                AnchorPosition = sum / _members.Count;
            }

            MaximumMemberDistanceFromAnchor = 0f;

            foreach (var member in _members)
            {
                var distance = ISBOrderCombatUtility.HorizontalDistance(
                    member.Position,
                    AnchorPosition);

                if (distance > MaximumMemberDistanceFromAnchor)
                    MaximumMemberDistanceFromAnchor = distance;
            }
        }

        private void PruneDeadMembers()
        {
            for (var index = _members.Count - 1; index >= 0; index--)
            {
                var member = _members[index];

                if (ISBOrderCombatUtility.IsAlive(member))
                    continue;

                _roles.Remove(member);
                _members.RemoveAt(index);
            }
        }
    }
}
