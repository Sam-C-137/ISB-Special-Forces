using EFT;
using ISBSpecialForces.Behavior.Orders.State;
using ISBSpecialForces.Behavior.Orders.Utilities;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Orders.Components
{
    public sealed class ISBOrderAgent : MonoBehaviour
    {
        private BotOwner _botOwner;
        private ISBOrderSquadState _squad;
        private CustomNavigationPoint _tacticalPoint;
        private ISBOrderDirective _pointDirective;
        private Vector3 _pointEnemyPosition;
        private float _nextPointRefreshTime;
        private float _nextPointFailureLogTime;
        private ISBOrderDirective _activePulseDirective;
        private float _pulseExpiresAt;
        private float _nextPursuitPulseTime;
        private bool _pulseActive;
        private int _activeOrderRevision = -1;
        private int _consumedOrderRevision = -1;
        private int _lastObservedOrderRevision = -1;
        private bool _initialized;

        public BotOwner BotOwner => _botOwner;
        public ISBOrderSquadState Squad => EnsureSquad();

        public ISBOrderDirective CurrentDirective
        {
            get
            {
                var squad = EnsureSquad();
                return squad?.GetDirective(_botOwner) ?? ISBOrderDirective.None;
            }
        }
        public bool HasCombatDirective
        {
            get
            {
                var squad = EnsureSquad();
                return squad != null
                    && squad.HasEnemy
                    && squad.CurrentOrder != ISBOrderType.None;
            }
        }

        public void Init(BotOwner botOwner)
        {
            if (botOwner == null || !WildSpawnTypeExtensions.IsISB(botOwner.Profile.Info.Settings.Role))
                return;

            if (_initialized && _botOwner == botOwner)
            {
                EnsureSquad();
                return;
            }

            _botOwner = botOwner;
            _initialized = true;
            _squad = ISBOrderSquadRegistry.Register(botOwner);
            InvalidatePoint();
            ResetMovementPulse();
        }

        public bool CanRunDirective(ISBOrderDirective directive, bool requiresTacticalPoint)
        {
            var squad = EnsureSquad();

            if (!ISBOrderSettings.Enabled
                || squad == null
                || !ISBOrderCombatUtility.IsAlive(_botOwner)
                || squad.GetDirective(_botOwner) != directive)
            {
                return false;
            }

            if (_pulseActive && _activePulseDirective == directive)
                return true;

            if (_lastObservedOrderRevision != squad.OrderRevision)
            {
                _lastObservedOrderRevision = squad.OrderRevision;
                _nextPursuitPulseTime = 0f;
                InvalidatePoint();
            }

            if (directive == ISBOrderDirective.Pursuit)
            {
                if (ShouldStopPursuit() || Time.time < _nextPursuitPulseTime)
                    return false;

                return !requiresTacticalPoint || TryPrepareTacticalPoint(directive);
            }

            if (_consumedOrderRevision == squad.OrderRevision)
                return false;

            return !requiresTacticalPoint || TryPrepareTacticalPoint(directive);
        }

        public void BeginMovementPulse(ISBOrderDirective directive, float duration)
        {
            var squad = EnsureSquad();

            _activePulseDirective = directive;
            _activeOrderRevision = squad?.OrderRevision ?? -1;

            if (directive != ISBOrderDirective.Pursuit)
                _consumedOrderRevision = _activeOrderRevision;

            _pulseExpiresAt = Time.time + Mathf.Max(0.5f, duration);
            _pulseActive = true;
        }

        public bool ShouldEndMovementPulse(ISBOrderDirective directive)
        {
            return _pulseActive
                && _activePulseDirective == directive
                && Time.time >= _pulseExpiresAt;
        }

        public void ExpireMovementPulse(ISBOrderDirective directive)
        {
            if (_pulseActive && _activePulseDirective == directive)
                _pulseExpiresAt = Time.time;
        }

        public void EndMovementPulse(ISBOrderDirective directive)
        {
            if (_pulseActive && _activePulseDirective == directive)
            {
                _pulseActive = false;
                _activePulseDirective = ISBOrderDirective.None;
                _activeOrderRevision = -1;

                if (directive == ISBOrderDirective.Pursuit)
                {
                    _nextPursuitPulseTime = Time.time
                        + Mathf.Max(0.5f, ISBOrderSettings.Current.pursuitCombatWindow);
                    InvalidatePoint();
                }
            }
        }

        public bool ShouldStopPursuit()
        {
            var squad = EnsureSquad();

            if (squad == null || !squad.HasEnemy || !ISBOrderCombatUtility.IsAlive(_botOwner))
                return true;

            return ISBOrderCombatUtility.HorizontalDistance(
                _botOwner.Position,
                squad.EnemyPosition) <= ISBOrderSettings.Current.pursuitStopDistance;
        }

        public void LookTowardEnemy()
        {
            var squad = EnsureSquad();

            if (squad != null && squad.HasEnemy)
                ISBOrderCombatUtility.LookAtPosition(_botOwner, squad.EnemyPosition);
        }

        public bool TryPrepareTacticalPoint(ISBOrderDirective directive)
        {
            var squad = EnsureSquad();

            if (squad == null
                || !squad.HasEnemy
                || !ISBOrderCombatUtility.IsAlive(_botOwner))
            {
                return false;
            }

            var settings = ISBOrderSettings.Current;
            var enemyRefreshDistance = directive == ISBOrderDirective.Pursuit
                ? settings.pursuitEnemyMoveRefreshDistance
                : 8f;
            var enemyMoved = ISBOrderCombatUtility.HorizontalDistance(
                _pointEnemyPosition,
                squad.EnemyPosition) > enemyRefreshDistance;

            if (_tacticalPoint != null
                && _pointDirective == directive
                && !enemyMoved
                && Time.time < _nextPointRefreshTime)
            {
                return true;
            }

            var desiredPosition = CalculateDesiredPosition(directive, squad);

            if (!ISBOrderCombatUtility.TrySampleNavMesh(
                    desiredPosition,
                    settings.navMeshSampleRadius,
                    out var sampledPosition))
            {
                LogPointFailure(directive, "NavMesh sampling failed", desiredPosition);
                _tacticalPoint = null;
                return false;
            }

            CustomNavigationPoint point;

            if (!TryFindPointWithFallbacks(directive, squad, sampledPosition, settings, out point))
            {
                LogPointFailure(directive, "no usable cover/navigation point", sampledPosition);
                _tacticalPoint = null;
                return false;
            }

            _tacticalPoint = point;
            _pointDirective = directive;
            _pointEnemyPosition = squad.EnemyPosition;
            _nextPointRefreshTime = Time.time + (
                directive == ISBOrderDirective.Pursuit
                    ? settings.pursuitPointRefreshInterval
                    : settings.pointRefreshInterval);
            return true;
        }

        public CustomNavigationPoint GetTacticalPoint(ISBOrderDirective directive)
        {
            return TryPrepareTacticalPoint(directive) ? _tacticalPoint : null;
        }

        public void InvalidatePoint()
        {
            _tacticalPoint = null;
            _pointDirective = ISBOrderDirective.None;
            _pointEnemyPosition = default;
            _nextPointRefreshTime = 0f;
        }

        private void ResetMovementPulse()
        {
            _activePulseDirective = ISBOrderDirective.None;
            _pulseExpiresAt = 0f;
            _nextPursuitPulseTime = 0f;
            _pulseActive = false;
            _activeOrderRevision = -1;
            _consumedOrderRevision = -1;
            _lastObservedOrderRevision = -1;
        }

        private bool TryFindPointWithFallbacks(
            ISBOrderDirective directive,
            ISBOrderSquadState squad,
            Vector3 sampledPosition,
            ISBSpecialForces.Models.ISBOrderConfig settings,
            out CustomNavigationPoint point)
        {
            if (directive == ISBOrderDirective.Pursuit)
            {
                var pursuitRadii = new[]
                {
                    settings.pursuitPointSearchRadius,
                    settings.pursuitPointSearchRadius * 1.5f,
                    settings.pursuitPointSearchRadius * 2f
                };

                foreach (var radius in pursuitRadii)
                {
                    if (ISBOrderCombatUtility.TryFindCoverPoint(
                            _botOwner,
                            sampledPosition,
                            radius,
                            out point))
                    {
                        return true;
                    }
                }

                var halfway = Vector3.Lerp(_botOwner.Position, sampledPosition, 0.65f);

                if (ISBOrderCombatUtility.TrySampleNavMesh(
                        halfway,
                        settings.navMeshSampleRadius,
                        out halfway)
                    && ISBOrderCombatUtility.TryFindCoverPoint(
                        _botOwner,
                        halfway,
                        settings.pursuitPointSearchRadius * 1.5f,
                        out point))
                {
                    return true;
                }

                point = null;
                return false;
            }

            if (ISBOrderCombatUtility.TryFindCoverPoint(
                    _botOwner,
                    sampledPosition,
                    settings.tacticalPointSearchRadius,
                    out point))
            {
                return true;
            }

            var fractions = new[] { 0.75f, 0.55f, 0.35f };

            foreach (var fraction in fractions)
            {
                var fallback = Vector3.Lerp(_botOwner.Position, sampledPosition, fraction);

                if (!ISBOrderCombatUtility.TrySampleNavMesh(
                        fallback,
                        settings.navMeshSampleRadius,
                        out fallback))
                {
                    continue;
                }

                if (ISBOrderCombatUtility.TryFindCoverPoint(
                        _botOwner,
                        fallback,
                        settings.tacticalPointSearchRadius * 1.5f,
                        out point))
                {
                    return true;
                }
            }

            if (directive == ISBOrderDirective.Regroup
                && ISBOrderCombatUtility.TryFindCoverPoint(
                    _botOwner,
                    squad.AnchorPosition,
                    settings.tacticalPointSearchRadius * 1.75f,
                    out point))
            {
                return true;
            }

            return ISBOrderCombatUtility.TryFindCoverPoint(
                _botOwner,
                _botOwner.Position,
                settings.tacticalPointSearchRadius * 2f,
                out point);
        }

        private Vector3 CalculateDesiredPosition(ISBOrderDirective directive, ISBOrderSquadState squad)
        {
            var settings = ISBOrderSettings.Current;
            var botPosition = _botOwner.Position;
            var enemyPosition = squad.EnemyPosition;
            var forward = ISBOrderCombatUtility.HorizontalDirection(botPosition, enemyPosition);
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var side = GetSideMultiplier(squad.GetRole(_botOwner));

            switch (directive)
            {
                case ISBOrderDirective.Assault:
                    return enemyPosition
                        - forward * settings.assaultStopDistance
                        + right * side * 3.5f;

                case ISBOrderDirective.Flank:
                    return enemyPosition
                        - forward * (settings.assaultStopDistance + 4f)
                        + right * side * settings.flankDistance;

                case ISBOrderDirective.Pursuit:
                    var enemyDistance = ISBOrderCombatUtility.HorizontalDistance(
                        botPosition,
                        enemyPosition);
                    var advanceDistance = Mathf.Clamp(
                        enemyDistance - settings.pursuitStopDistance,
                        6f,
                        settings.pursuitAdvanceStep);

                    return botPosition
                        + forward * advanceDistance
                        + right * side * 2.5f;

                case ISBOrderDirective.Regroup:
                    var anchor = ISBOrderCombatUtility.IsValidWorldPosition(squad.AnchorPosition)
                        ? squad.AnchorPosition
                        : botPosition;

                    return anchor
                        + right * side * 4f
                        - forward * 2f;

                default:
                    return botPosition;
            }
        }

        private void LogPointFailure(ISBOrderDirective directive, string reason, Vector3 position)
        {
            if (!ISBOrderSettings.Logs || Time.time < _nextPointFailureLogTime)
                return;

            _nextPointFailureLogTime = Time.time + 5f;
        }

        private static float GetSideMultiplier(ISBOrderRole role)
        {
            switch (role)
            {
                case ISBOrderRole.ManeuverLeft:
                    return -1f;
                case ISBOrderRole.ManeuverRight:
                    return 1f;
                case ISBOrderRole.Support:
                    return 0.35f;
                default:
                    return 0f;
            }
        }

        private ISBOrderSquadState EnsureSquad()
        {
            if (!_initialized || _botOwner == null)
                return null;

            if (_squad == null || _squad.IsEmpty)
                _squad = ISBOrderSquadRegistry.Register(_botOwner);

            return _squad;
        }

        private void OnDestroy()
        {
            if (_squad != null && _botOwner != null)
                _squad.RemoveMember(_botOwner);

            _squad = null;
            _botOwner = null;
            ResetMovementPulse();
        }
    }
}
