using EFT;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Behavior.BlackDivision
{
    internal sealed class ISBTacticalAgent : MonoBehaviour
    {
        internal BotOwner Bot { get; private set; }

        internal TacticalSquad Squad;
        internal int Team;
        internal string EnemyId;
        internal Vector3 EnemyPosition, Destination;
        internal float ContactAge = float.MaxValue;
        internal bool HasContact, Visible, CanShoot, Urgent = true;
        internal bool HasDestination { get; private set; }

        private NavMeshPath _path;
        internal NavMeshPath Path => _path ?? (_path = new NavMeshPath());

        private object _sain;
        private BotISBManager _isbManager;
        private int _sensorFrame = -1;
        private float _findSainAt;
        private bool _faulted, _running;
        private bool _registerTactics;
        private int _revision = -1, _finishedRevision = -1;
        private float _moveUntil, _retryAt, _preparedUntil, _lastProgressAt;
        private Vector3 _progressPosition;
        internal bool Alive => Bot != null && Bot.isActiveAndEnabled && !Bot.IsDead && Bot.GetPlayer?.HealthController?.IsAlive == true && isActiveAndEnabled;
        internal bool UnderFire => Bot?.Memory != null && (Bot.Memory.IsUnderFire || (Bot.Memory.LastTimeHit > 0f && Time.time - Bot.Memory.LastTimeHit < 2f));
        internal bool ReadyToMove => !_faulted && !HasCheckpoint && !ISBCameraHuntController.AppliesTo(Bot) && !ISBRevengeHuntController.AppliesTo(Bot) && HasContact && BlackDivisionTactics.CanManeuver(Alive, Visible, CanShoot, Urgent || Bot.Medecine?.Using == true, UnderFire, Vector3.Distance(Bot.Position, EnemyPosition), ContactAge);

        internal static ISBTacticalAgent GetOrAdd(BotOwner bot, bool registerTactics = true)
        {
            if (!ISBFikaAuthority.IsAiAuthority || bot == null)
                return null;
            var agent = bot.GetComponent<ISBTacticalAgent>() ?? bot.gameObject.AddComponent<ISBTacticalAgent>();
            agent.Bot = bot;
            agent._registerTactics |= registerTactics;
            if (registerTactics)
                MonoBehaviourSingleton<ISBTacticalController>.Instance?.Register(agent);
            return agent;
        }

        internal void RefreshContact()
        {
            if (_sensorFrame == Time.frameCount)
                return;
            _sensorFrame = Time.frameCount;
            HasContact = Visible = CanShoot = false;
            Urgent = true;
            EnemyId = null;
            ContactAge = float.MaxValue;
            if (!Alive || _missionSensorFault)
                return;
            try
            {
                if ((_sain == null || (_sain is UnityEngine.Object obj && obj == null)) && Time.time >= _findSainAt)
                {
                    _sain = SainTacticalSensor.Find(Bot);
                    _findSainAt = Time.time + 1f;
                }

                HasContact = SainTacticalSensor.ReadContact(_sain, out EnemyId, out EnemyPosition, out ContactAge, out Visible, out CanShoot, out Urgent);
                if (_sain == null)
                    Urgent = Bot.Memory?.HaveEnemy == true;
            }
            catch (Exception ex)
            {
                _missionSensorFault = true;
                Plugin.LogSource?.LogError($"[ISB MISSIONS] Sensor unavailable; leaving control to SAIN. {ex.GetBaseException()}");
                CancelMove();
            }
        }

        private float _combatUntil;
        private bool _missionSensorFault;
        internal bool HasCheckpoint
        {
            get
            {
                if (_isbManager == null && Bot != null)
                    _isbManager = Bot.GetManager();
                return _isbManager != null && _isbManager.HasAssignedCheckpoint();
            }
        }

        internal bool CanRunMission()
        {
            if (!ISBFikaAuthority.IsAiAuthority || !Alive || _missionSensorFault)
                return false;
            RefreshContact();
            if (_missionSensorFault)
                return false;
            var enemy = Bot.Memory?.GoalEnemy;
            if (BlackDivisionTactics.NeedsCombat(Visible || enemy?.IsVisible == true, CanShoot || enemy?.CanShoot == true, Urgent || Bot.Medecine?.Using == true, UnderFire, HasContact, ContactAge, HasContact ? Vector3.Distance(Bot.Position, EnemyPosition) : float.MaxValue))
            {
                _combatUntil = Time.time + 2f;
                return false;
            }

            return Time.time >= _combatUntil;
        }

        internal bool CanRun()
        {
            if (!ISBFikaAuthority.IsAiAuthority || !Alive || Bot.Mover?._player == null || Bot.Mover.ActualPathController == null || _faulted || _missionSensorFault || HasCheckpoint || ISBRevengeHuntController.AppliesTo(Bot) || ISBCameraHuntController.AppliesTo(Bot) || Squad == null || !Squad.CanMove(this))
                return false;
            var enemy = Bot.Memory?.GoalEnemy;
            if (enemy?.IsVisible == true || enemy?.CanShoot == true || UnderFire || Bot.Medecine?.Using == true)
                return false;
            RefreshContact();
            if (!ReadyToMove || !Squad.CanMove(this))
                return false;
            if (_running)
                return _revision == Squad.Policy.Revision && Time.time < _moveUntil;
            if (Time.time < _retryAt || _finishedRevision == Squad.Policy.Revision)
                return false;
            if (HasDestination && (_revision != Squad.Policy.Revision || Time.time > _preparedUntil))
                HasDestination = false;
            if (HasDestination)
                return true;
            if (!ISBNavigationBudget.TryBeginSearch(Time.frameCount))
                return false;
            try
            {
                if (!TacticalCover.TryFind(this, out Destination))
                {
                    _retryAt = Time.time + 2f;
                    return false;
                }

                _revision = Squad.Policy.Revision;
                _preparedUntil = Time.time + 1f;
                HasDestination = true;
                return true;
            }
            catch (Exception ex)
            {
                Fail(ex);
                return false;
            }
        }

        internal void BeginMove()
        {
            if (!HasDestination || !CanRun())
                return;
            _running = true;
            _moveUntil = Time.time + 4.5f;
            _lastProgressAt = Time.time;
            _progressPosition = Bot.Position;
            _finishedRevision = _revision;
            try
            {
                Bot.PatrollingData?.Pause();
                Bot.GoToPoint(Destination, mustHaveWay: true);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        internal void TickMove()
        {
            try
            {
                if (!_running || !CanRun())
                {
                    CancelMove();
                    return;
                }

                if ((Bot.Position - Destination).sqrMagnitude <= 2.25f)
                {
                    CancelMove();
                    return;
                }

                if ((Bot.Position - _progressPosition).sqrMagnitude >= 0.36f)
                {
                    _progressPosition = Bot.Position;
                    _lastProgressAt = Time.time;
                }
                else if (Time.time - _lastProgressAt > 2f)
                {
                    CancelMove();
                    return;
                }

                Bot.SetPose(1f);
                Bot.SetTargetMoveSpeed(0.9f);
                Bot.Sprint(false);
                Bot.BewarePlantedMine?.Update();
                Bot.DoorOpener?.UpdateDoorInteractionStatus();
                Bot.Steering?.LookToMovingDirection();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        internal void CancelMove()
        {
            HasDestination = false;
            if (!_running)
                return;
            _running = false;
            _retryAt = Time.time + 2f;
            try
            {
                var bot = Bot;
                if (bot != null && bot.isActiveAndEnabled && !bot.IsDead && bot.GetPlayer?.HealthController?.IsAlive == true)
                {
                    var mover = bot.Mover;
                    if (mover?._player != null)
                    {
                        if (mover.ActualPathController != null)
                            mover.Stop();
                        mover.Sprint(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                try
                {
                    if (Bot != null && Bot.isActiveAndEnabled && !Bot.IsDead && Bot.GetPlayer?.HealthController?.IsAlive == true)
                        Bot.PatrollingData?.Unpause();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }
            }
        }

        private void Fail(Exception ex)
        {
            if (!_faulted)
                Plugin.LogSource?.LogError($"[ISB TACTICS] Disabled tactical movement for {Bot?.Profile?.Nickname}; SAIN retains control. {ex.GetBaseException()}");
            _faulted = true;
            CancelMove();
        }

        private void OnEnable()
        {
            if (Bot != null && _registerTactics)
                MonoBehaviourSingleton<ISBTacticalController>.Instance?.Register(this);
        }

        private void OnDisable()
        {
            CancelMove();
            MonoBehaviourSingleton<ISBTacticalController>.Instance?.Remove(this);
        }

        private void OnDestroy()
        {
            CancelMove();
            MonoBehaviourSingleton<ISBTacticalController>.Instance?.Remove(this);
        }
    }
}
