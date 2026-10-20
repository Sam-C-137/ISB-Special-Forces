using EFT;
using UnityEngine;
using UnityEngine.AI;
#if SPT40
using NativeAdvance = GClass223;
using NativeParameters = GClass26;
using NativeAiming = GClass178;
#else
using NativeAdvance = GoToEnemy;
using NativeParameters = CoreActionResultParams;
using NativeAiming = Aiming;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal class ISBContactAdvance : NativeAdvance
    {
        private readonly BotOwner _bot;
        private readonly NativeAiming _contactAim;
        private float _nextPath;
        private string _enemy;
        private Vector3 _destination;
        private bool _hasDestination;
        private Vector3 _lastProgress;
        private float _progressTime;
        private Vector3 _lastKnown;
        private int _searchStep;
        private readonly NavMeshPath _path = new NavMeshPath();
        internal ISBContactAdvance(BotOwner bot) : base(bot) { _bot = bot; _contactAim = new ISBSuppressionAiming(bot); }

        private bool _searching;
        private float _searchAngle;

        public override void NotMovingCheck()
        {
            var enemy = _bot.Memory?.GoalEnemy;
            if (enemy == null || Time.time < _nextPath) return;
            _nextPath = Time.time + 1f;
            var known = enemy.IsVisible ? enemy.EnemyLastPositionReal : enemy.EnemyLastPosition;
            bool changed = _enemy != enemy.Person?.ProfileId || (_lastKnown - known).sqrMagnitude > 16f;
            if (changed || enemy.IsVisible)
            {
                _searching = false;
                _searchStep = 0;
                _lastKnown = known;
            }
            _enemy = enemy.Person?.ProfileId;
            if ((_lastProgress - _bot.Position).sqrMagnitude > 0.25f)
            {
                _lastProgress = _bot.Position;
                _progressTime = Time.time;
            }
            bool stalled = Time.time - _progressTime >= 4f;
            bool reached = _hasDestination && (_destination - _bot.Position).sqrMagnitude <= 2.25f;
            if (!_searching && !enemy.IsVisible && (known - _bot.Position).sqrMagnitude <= 4f)
            {
                _searching = true;
                _lastKnown = known;
                var approach = _bot.Position - known;
                _searchAngle = Mathf.Atan2(approach.x, approach.z);
                reached = true;
            }
            if (_searching)
            {
                if (!reached && !stalled && _hasDestination && _bot.Mover.HasPathAndNoComplete) return;
                while (_searchStep < 8)
                {
                    if (!ISBNavigationBudget.TryBeginSearch(Time.frameCount))
                    {
                        _nextPath = Time.time + .05f;
                        return;
                    }
                    float angle = _searchAngle + (++_searchStep * Mathf.PI / 4f);
                    var candidate = _lastKnown + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (_searchStep > 4 ? 6f : 3f);
                    if ((candidate - _bot.Position).sqrMagnitude > 4f && MoveTo(candidate)) return;
                }
                enemy.GroupInfo.IsLastPositionChecked = true;
                _hasDestination = false;
                return;
            }
            if (!changed && !stalled && _hasDestination && _bot.Mover.HasPathAndNoComplete &&
                (_destination - known).sqrMagnitude < 9f) return;
            if (ISBNavigationBudget.TryBeginSearch(Time.frameCount)) MoveTo(known);
            else _nextPath = Time.time + .05f;
        }

        private bool MoveTo(Vector3 position)
        {
            if (!NavMesh.SamplePosition(position, out var hit, 1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - position.y) > 1.2f ||
                !NavMesh.CalculatePath(_bot.Position, hit.position, NavMesh.AllAreas, _path) ||
                _path.status != NavMeshPathStatus.PathComplete) return false;
            if ((_bot.Position - hit.position).sqrMagnitude <= 1f) return false;
            _destination = hit.position;
            _hasDestination = true;
            _progressTime = Time.time;
            _lastProgress = _bot.Position;
            _bot.GoToPoint(_destination, mustHaveWay: true);
            return true;
        }

        public override void UpdateNodeByBrain(NativeParameters data)
        {
            var enemy = _bot.Memory?.GoalEnemy;
            if (enemy == null || _bot.IsDead || !ISBRuntimeBindings.IsAuthority()) return;
            if (enemy.IsVisible && enemy.CanShoot)
            {
                _bot.StopMove();
                _bot.Sprint(false);
                _bot.BotLay.GetUp(true);
                _contactAim.UpdateNodeByBrain(null);
                return;
            }
            base.UpdateNodeByBrain(data);
            if (enemy.IsVisible)
            {
                _contactAim.UpdateNodeByBrain(null);
                _bot.SetTargetMoveSpeed(0.35f);
                return;
            }
            _bot.ShootData.EndShoot();
            _bot.AimingManager.CurrentAiming.LoseTarget();
            if (_bot.Mover.HasPathAndNoComplete)
            {
                _bot.BotLay.GetUp(true);
                _bot.SetPose(1f);
                _bot.SetTargetMoveSpeed(1f);
                _bot.Steering.LookToMovingDirection();
            }
            else if (Mathf.Abs(enemy.EnemyLastPosition.y - _bot.Position.y) < 1.5f)
                _bot.Steering.LookToPoint(enemy.EnemyLastPosition + Vector3.up * 1.2f);
        }
    }
}
