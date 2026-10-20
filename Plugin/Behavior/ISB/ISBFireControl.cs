using System.Runtime.CompilerServices;
using EFT;
using UnityEngine;
#if SPT40
using NativeAiming = GClass178;
using LayersMaskController = LayerMaskClass;
#else
using NativeAiming = Aiming;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBFireControl
    {
        private sealed class AimState
        {
            internal string Enemy;
            internal Vector3 Point;
            internal Vector3 TargetPosition;
            internal float Until;
            internal bool Memory;
        }

        private static readonly ConditionalWeakTable<BotOwner, AimState> States = new ConditionalWeakTable<BotOwner, AimState>();
        private static bool Controls(BotOwner bot) => ISBRuntimeBindings.IsAuthority() && ISBNativeBrain.Owns(bot);

        internal static bool Clear(BotOwner bot, Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) &&
            !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z) &&
            !Physics.Linecast(bot.LookSensor.ShootStartPos, point, LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore);

        internal static void AimPrefix(NativeAiming __instance, ref Vector3? __0)
        {
#if SPT40
            var bot = __instance.BotOwner_0;
#else
            var bot = __instance._owner;
#endif
            if (!Controls(bot)) return;
            var enemy = bot.Memory?.GoalEnemy;
            bool memory = __instance is ISBSuppressionAiming && enemy?.IsVisible != true;
            if (enemy?.IsVisible == true) __0 = enemy.GetPartToShoot();
            if (!__0.HasValue || enemy?.Person?.HealthController?.IsAlive != true ||
                !memory && (!enemy.IsVisible || (__0.Value - enemy.CurrPosition).sqrMagnitude > 16f))
            {
                __0 = null;
                Stop(bot);
                return;
            }
            var state = States.GetValue(bot, _ => new AimState());
            state.Enemy = enemy.ProfileId;
            state.Point = __0.Value;
            state.TargetPosition = enemy.Person.Position;
            state.Until = Time.time + 0.3f;
            state.Memory = memory;
        }

        internal static bool MayFire(BotOwner bot)
        {
            var enemy = bot.Memory?.GoalEnemy;
            if (enemy?.Person?.HealthController?.IsAlive != true || !States.TryGetValue(bot, out var state) ||
                Time.time > state.Until || state.Enemy != enemy.ProfileId)
                return false;
            if (state.Memory)
            {
                var offset = state.Point - bot.LookSensor.ShootStartPos;
                if (!ISBSuppressionSafety.MayFireAtMemory(Time.time - enemy.PersonalLastSeenTime,
                    new Vector2(offset.x, offset.z).magnitude, offset.y,
                    Vector3.Dot(state.Point - bot.Position, bot.LookDirection), true,
                    bot.WeaponManager.UnderbarrelLauncherController.IsActive)) return false;
            }
            else if (!enemy.IsVisible || !enemy.CanShoot || (state.Point - enemy.CurrPosition).sqrMagnitude > 16f)
                return false;
            var aiming = bot.AimingManager?.CurrentAiming;
            return aiming != null && aiming.IsReady &&
                (state.Memory || (state.TargetPosition - enemy.Person.Position).sqrMagnitude <= 0.25f) &&
                Vector3.Dot(bot.LookDirection.normalized, (aiming.EndTargetPoint - bot.LookSensor.ShootStartPos).normalized) >= 0.9f &&
                Clear(bot, state.Point) && (state.Point.Equals(aiming.EndTargetPoint) || Clear(bot, aiming.EndTargetPoint));
        }

        internal static bool ShootPrefix(ShootData __instance, ref bool __result)
        {
#if SPT40
            var bot = __instance.Owner;
#else
            var bot = __instance._owner;
#endif
            if (!Controls(bot) || MayFire(bot)) return true;
            __instance.EndShoot();
            __result = false;
            return false;
        }

        internal static void UpdatePostfix(ShootData __instance)
        {
#if SPT40
            var bot = __instance.Owner;
#else
            var bot = __instance._owner;
#endif
            if (Controls(bot) && __instance.Shooting && !MayFire(bot)) __instance.EndShoot();
        }

        internal static void Stop(BotOwner bot)
        {
            if (States.TryGetValue(bot, out var state)) state.Until = -1f;
            bot.ShootData?.EndShoot();
            bot.AimingManager?.CurrentAiming?.LoseTarget();
        }

        internal static bool PronePrefix(BotLay __instance, ref bool __result)
        {
#if SPT40
            var bot = __instance.BotOwner_0;
#else
            var bot = __instance._owner;
#endif
            if (!Controls(bot) || bot.ArtilleryDangerPlace.IsActive) return true;
            var enemy = bot.Memory?.GoalEnemy;
            if (enemy?.IsVisible == true && enemy.CanShoot && enemy.Distance >= 35f &&
                Mathf.Abs(enemy.EnemyLastPositionReal.y - bot.Position.y) < 2f && !bot.Mover.HasPathAndNoComplete)
                return true;
            __result = false;
            return false;
        }
    }
}
