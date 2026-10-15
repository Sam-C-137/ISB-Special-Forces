using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ISBSpecialForces.Behavior.BlackDivision;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Behavior
{
    public static class DeadManSwitchEvacuation
    {
        internal static GameWorld World;
        internal static bool Active;
        public static void SetActive(GameWorld world, bool active)
        {
            World = world;
            Active = active && world != null;
        }
    }

    internal sealed class DeadManSwitchPanic : CustomLayer
    {
        private readonly PanicMoveState _state;
        public DeadManSwitchPanic(BotOwner bot, int priority) : base(bot, priority)
        {
            _state = PanicMoveState.Get(bot);
        }

        public override string GetName() => "DeadManSwitchPanic";
        public override bool IsActive() => _state.CanMove();
        public override Action GetNextAction() => new Action(typeof(DeadManSwitchRun), "Evacuate to nearest reachable gate");
        public override bool IsCurrentActionEnding() => !_state.CanMove();
    }

    internal sealed class DeadManSwitchRun : CustomLogic
    {
        private readonly PanicMoveState _state;
        public DeadManSwitchRun(BotOwner bot) : base(bot)
        {
            _state = PanicMoveState.Get(bot);
        }

        public override void Start()
        {
            base.Start();
            _state.Begin();
        }

        public override void Update(CustomLayer.ActionData data) => _state.Move();
        public override void Stop()
        {
            _state.Stop();
            base.Stop();
        }
    }

    internal sealed class PanicMoveState : MonoBehaviour
    {
        private static readonly Vector3[] Gates =
        {
            new Vector3(-231.522f, 4.735f, -473.371f),
            new Vector3(-170.794f, 4.961f, -200.685f)
        };
        private static int _searchFrame = -1;
        private static float _nextPathBudget;
        private BotOwner _bot;
        private readonly NavMeshPath _path = new NavMeshPath();
        private Vector3 _destination, _lastPosition;
        private float _healthAt, _health = -1f, _lostHealth, _motionAt, _threatAt;
        private object _sainMover;
        private MethodInfo _run, _stop;
        private object[] _runArguments;
        private float _sainCheckAt;
        private bool _ready, _leaving, _failed;
        private float _searchAt, _repathAt, _combatUntil, _arrivedAt = -1f;
        internal static PanicMoveState Get(BotOwner bot)
        {
            var state = bot.GetComponent<PanicMoveState>() ?? bot.gameObject.AddComponent<PanicMoveState>();
            state._bot = bot;
            return state;
        }

        internal bool CanMove()
        {
            if (_failed || _leaving || !DeadManSwitchEvacuation.Active || DeadManSwitchEvacuation.World == null || _bot == null || !_bot.isActiveAndEnabled || _bot.IsDead || _bot.GetPlayer?.HealthController?.IsAlive != true)
                return false;
            if (Time.time >= _healthAt)
            {
                var health = _bot.GetPlayer.HealthController.GetBodyPartHealth(EBodyPart.Common, false);
                float value = health.Current;
                if (_health >= 0f)
                    _lostHealth += Math.Max(0f, _health - value);
                _health = value;
                if (_lostHealth >= Math.Max(25f, health.Maximum * .05f))
                    _combatUntil = Time.time + 2f;
                _lostHealth = 0f;
                _healthAt = Time.time + 1f;
            }

            if (Time.time >= _threatAt)
            {
                _threatAt = Time.time + .3f + (_bot.GetInstanceID() & 3) * .025f;
                var enemies = _bot.EnemiesController?.EnemyInfos;
                if (enemies != null)
                    foreach (var enemy in enemies.Values)
                        if (enemy != null && enemy.Person != null && enemy.Person.HealthController?.IsAlive == true && (enemy.Person.Position - _bot.Position).sqrMagnitude <= 625f && (enemy.IsVisible || enemy.CanShoot))
                        {
                            _combatUntil = Time.time + 3f;
                            _arrivedAt = -1f;
                            break;
                        }
            }

            if (Time.time < _combatUntil)
                return false;
            if (_ready)
                return true;
            if (Time.time < _searchAt || !PathBudget())
                return false;
            _searchAt = Time.time + 3f;
            float best = float.MaxValue;
            foreach (var gate in Gates)
            {
                if (!NavMesh.SamplePosition(gate, out var hit, 8f, NavMesh.AllAreas) || !NavMesh.CalculatePath(_bot.Position, hit.position, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
                    continue;
                float distance = 0f;
                var corners = _path.corners;
                for (int i = 1; i < corners.Length; i++)
                    distance += Vector3.Distance(corners[i - 1], corners[i]);
                if (distance >= best)
                    continue;
                best = distance;
                _destination = hit.position;
                _ready = true;
            }

            return _ready;
        }

        internal void Begin()
        {
            _bot.PatrollingData?.Pause();
            _repathAt = 0f;
            _lastPosition = _bot.Position;
            _motionAt = Time.time + 5f;
        }

        internal void Move()
        {
            if (!CanMove())
                return;
            try
            {
                if ((_bot.Position - _destination).sqrMagnitude <= 25f)
                {
                    _stop?.Invoke(_sainMover, null);
                    _bot.Mover?.Stop();
                    _bot.Sprint(false);
                    if (_arrivedAt < 0f)
                        _arrivedAt = Time.time;
                    if (Time.time - _arrivedAt < 1.5f)
                        return;
                    if (_bot.LeaveData == null || _bot.BotsGroup?.BotGame == null)
                        return;
                    _leaving = true;
                    _bot.LeaveData.RemoveFromMap();
                    return;
                }

                _arrivedAt = -1f;
                if (Time.time >= _motionAt)
                {
                    bool stuck = (_bot.Position - _lastPosition).sqrMagnitude < .5f;
                    _lastPosition = _bot.Position;
                    _motionAt = Time.time + 5f;
                    if (stuck)
                    {
                        _ready = false;
                        _searchAt = 0f;
                        _repathAt = 0f;
                    }
                }

                if (Time.time >= _repathAt && PathBudget())
                {
                    bool running = RunWithSain();
                    if (!running)
                        _bot.GoToPoint(_destination, mustHaveWay: true);
                    _repathAt = Time.time + 2f + (_bot.GetInstanceID() & 3) * .1f;
                }

                _bot.SetPose(1f);
                _bot.SetTargetMoveSpeed(1f);
                _bot.Sprint(true);
                _bot.DoorOpener?.UpdateDoorInteractionStatus();
                _bot.Steering?.LookToMovingDirection();
            }
            catch (Exception ex)
            {
                _failed = true;
                try
                {
                    Stop();
                }
                catch
                {
                }

                Plugin.LogSource?.LogError("[ISB] Dead Man's Switch evacuation failed: " + ex.GetBaseException().Message);
            }
        }

        private static bool PathBudget()
        {
            if (_searchFrame == Time.frameCount || Time.time < _nextPathBudget)
                return false;
            _searchFrame = Time.frameCount;
            _nextPathBudget = Time.time + .05f;
            return true;
        }

        private bool RunWithSain()
        {
            if (_run == null && Time.time >= _sainCheckAt)
            {
                _sainCheckAt = Time.time + 5f;
                var sain = SainTacticalSensor.Find(_bot);
                _sainMover = sain?.GetType().GetProperty("Mover")?.GetValue(sain);
                _run = _sainMover?.GetType().GetMethods().FirstOrDefault(m => m.Name == "RunToPoint" && m.GetParameters().Length == 5);
                _stop = _sainMover?.GetType().GetMethod("Stop", Type.EmptyTypes);
                if (_run != null)
                    _runArguments = new object[]
                    {
                        _destination,
                        true,
                        2f,
                        Enum.Parse(_run.GetParameters()[3].ParameterType, "High"),
                        true
                    };
            }

            if (_run == null)
                return false;
            _runArguments[0] = _destination;
            return (bool)_run.Invoke(_sainMover, _runArguments);
        }

        internal void Stop()
        {
            if (_bot == null || _leaving)
                return;
            _stop?.Invoke(_sainMover, null);
            _bot.Sprint(false);
            _bot.Mover?.Stop();
            _bot.PatrollingData?.Unpause();
        }
    }
}
