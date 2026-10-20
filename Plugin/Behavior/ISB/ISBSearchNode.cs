using EFT;
using UnityEngine;
using UnityEngine.AI;
#if SPT40
using NativeSearchNode = GClass235;
using NativeParameters = GClass26;
#else
using NativeSearchNode = SearchInvisibleEnemy;
using NativeParameters = CoreActionResultParams;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal sealed class ISBSearchNode : NativeSearchNode
    {
        private readonly BotOwner _bot;
        private readonly NavMeshPath _path = new NavMeshPath();
        private Vector3 _destination, _lastProgress;
        private bool _hasDestination;
        private float _nextPath, _progressTime;

        internal ISBSearchNode(BotOwner bot) : base(bot) { _bot = bot; }

        public override void UpdateNodeByBrain(NativeParameters parameters)
        {
            if (_bot.IsDead || !ISBRuntimeBindings.IsAuthority()) return;
            var enemy = _bot.Memory?.GoalEnemy;
            if (enemy?.IsVisible == true && enemy.CanShoot) return;
            ISBFireControl.Stop(_bot);
#if SPT40
            method_0();
#else
            DoorOpen();
#endif
            var search = _bot.SearchData;
            search.RefreshSearchPoint();
            var point = search.SearchPoint;
            if (point == null)
            {
                _hasDestination = false;
                _bot.StopMove();
                return;
            }
            var offset = point.Position - _bot.Position;
            if (Mathf.Abs(offset.y) < 0.8f && new Vector2(offset.x, offset.z).sqrMagnitude < 1.44f)
            {
#if SPT40
                search.method_4();
#else
                search.InPoint();
#endif
                _hasDestination = false;
                _bot.SetPose(0.5f);
                return;
            }
            if ((_lastProgress - _bot.Position).sqrMagnitude > 0.25f)
            {
                _lastProgress = _bot.Position;
                _progressTime = Time.time;
            }
            bool changed = !_hasDestination || (_destination - point.Position).sqrMagnitude > 4f;
            bool stalled = Time.time - _progressTime > 4f;
            if (Time.time >= _nextPath && (changed || stalled || !_bot.Mover.HasPathAndNoComplete) && ISBNavigationBudget.TryBeginSearch(Time.frameCount))
            {
                _nextPath = Time.time + 2f;
                if (NavMesh.SamplePosition(point.Position, out var hit, 1.25f, NavMesh.AllAreas) &&
                    Mathf.Abs(hit.position.y - point.Position.y) < 0.8f &&
                    NavMesh.CalculatePath(_bot.Position, hit.position, NavMesh.AllAreas, _path) &&
                    _path.status == NavMeshPathStatus.PathComplete)
                {
                    _destination = hit.position;
                    _hasDestination = true;
                    _lastProgress = _bot.Position;
                    _progressTime = Time.time;
                    _bot.GoToPoint(_destination, mustHaveWay: true);
                }
                else
                {
                    _hasDestination = false;
                    _bot.StopMove();
                }
            }
            _bot.Sprint(false);
            if (_bot.Mover.HasPathAndNoComplete)
            {
                _bot.BotLay.GetUp(true);
                _bot.SetPose(1f);
                _bot.SetTargetMoveSpeed(1f);
                _bot.Steering.LookToMovingDirection();
            }
        }
    }
}
