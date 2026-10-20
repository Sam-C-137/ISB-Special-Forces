using EFT;
#if SPT40
using LayersMaskController = LayerMaskClass;
#endif
using UnityEngine;
#if SPT40
using NativeAdvance = GClass223;
using NativeAiming = GClass178;
using NativeParameters = GClass26;
#else
using NativeAdvance = GoToEnemy;
using NativeAiming = Aiming;
using NativeParameters = CoreActionResultParams;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal sealed class ISBSuppressionAiming : NativeAiming
    {
        private readonly BotOwner _bot;
        private string _enemy;
        private Vector3 _entryDirection;
        internal ISBSuppressionAiming(BotOwner bot) : base(bot) { _bot = bot; }
        public override Vector3? GetTarget()
        {
            var enemy = _bot.Memory?.GoalEnemy;
            if (enemy == null)
                return null;
            if (enemy.IsVisible)
            {
                _enemy = enemy.Person?.ProfileId;
                _entryDirection = enemy.EnemyLastPositionReal - _bot.Position;
                _entryDirection.y = 0f;
                _entryDirection.Normalize();
                var visiblePoint = enemy.GetPartToShoot();
                return (visiblePoint - enemy.CurrPosition).sqrMagnitude <= 16f ? (Vector3?)visiblePoint : null;
            }
            if (_enemy != enemy.Person?.ProfileId)
            {
                _enemy = enemy.Person?.ProfileId;
                _entryDirection = enemy.EnemyLastPositionReal - _bot.Position;
                _entryDirection.y = 0f;
                _entryDirection.Normalize();
            }
            if (_entryDirection.sqrMagnitude < 0.5f) return null;
            var point = enemy.EnemyLastPositionReal + Vector3.up * 1.2f;
            var delta = point - _bot.LookSensor.ShootStartPos;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float progress = Vector3.Dot(point - _bot.Position, _entryDirection);
            bool clear = !Physics.Linecast(_bot.LookSensor.ShootStartPos, point, LayersMaskController.HighPolyWithTerrainMask);
            return ISBSuppressionSafety.MayFireAtMemory(Time.time - enemy.PersonalLastSeenTime, horizontal, delta.y, progress,
                clear, _bot.WeaponManager.UnderbarrelLauncherController.IsActive) ? (Vector3?)point : null;
        }
    }

    internal sealed class ISBSuppressionAdvance : ISBContactAdvance
    {
        private readonly BotOwner _bot;
        private readonly ISBSuppressionPulse _pulse = new ISBSuppressionPulse();
        private readonly ISBSuppressionAiming _aiming;
        private readonly bool _advance;
        internal ISBSuppressionAdvance(BotOwner bot, bool advance = true) : base(bot)
        {
            _bot = bot;
            _advance = advance;
            _aiming = new ISBSuppressionAiming(bot);
        }
        public override void UpdateNodeByBrain(NativeParameters data)
        {
            var enemy = _bot.Memory?.GoalEnemy;
            if (enemy == null || _bot.IsDead || !ISBRuntimeBindings.IsAuthority())
            {
                _bot.ShootData?.EndShoot();
                return;
            }
#if SPT40
            method_0();
            method_6();
#else
            DoorOpen();
            PeriodChangeWeapon();
#endif
            _bot.Sprint(false);
            bool contact = enemy.IsVisible && enemy.CanShoot;
            if (_advance && !contact) NotMovingCheck();
            else _bot.StopMove();
            bool aim = _pulse.ShouldAim(Time.time, enemy.IsVisible, () => Random.Range(0.35f, 0.65f));
            var point = _aiming.GetTarget();
            if (aim && point.HasValue)
                _aiming.UpdateNodeByBrain(null);
            else
            {
                ISBFireControl.Stop(_bot);
                if (aim && point.HasValue) _bot.Steering.LookToPoint(point.Value);
                else _bot.Steering.LookToMovingDirection();
            }
            if (_bot.Mover.HasPathAndNoComplete)
            {
                _bot.BotLay.GetUp(true);
                _bot.SetTargetMoveSpeed(enemy.IsVisible ? 0.35f : 1f);
                _bot.SetPose(1f);
                if (_bot.WeaponManager.UnderbarrelLauncherController.IsActive)
                {
                    if (_bot.WeaponManager.UnderbarrelLauncherController.NeedToReload())
                        _bot.WeaponManager.UnderbarrelLauncherController.TryReload();
                }
                else if (!_bot.WeaponManager.HaveBullets)
                    _bot.WeaponManager.Reload.TryReload();
                if (_bot.Mover.IsComeTo(_bot.Settings.FileSettings.Move.REACH_DIST, false))
                    _bot.StopMove();
            }
            else
            {
                _bot.StopMove();
                _bot.SetPose(0.5f);
            }
        }
    }
}
