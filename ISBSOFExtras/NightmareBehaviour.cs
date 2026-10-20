using System;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSSOFExtra
{
    internal sealed class NightmareFacilityState
    {
        public double awakeAt = -1d, warningDue = -1d, removalDue = -1d, containmentAt = -1d;
        public double announcementStart = -1d, announcementEnd = -1d, decontaminationDelay;
        public double delayUpdatedAt = -1d;
        public string announcement = "", removal = "";
        public bool warningCompleted, flashTeleported, removalAnnounced, warningSequenceActive;
        public double warningPreludeUntil = -1d, warningHoldUntil = -1d;
    }

    internal sealed partial class Scp173Event
    {
        internal NightmareFacilityState Facility = new NightmareFacilityState();
        internal Scp173Config Settings => _config;
        internal double Clock => _authority ? Time.realtimeSinceStartup : _receivedServerTime + Time.realtimeSinceStartup - _receivedLocalTime;
        internal bool ClockReady => _authority || _receivedLocalTime > 0f;
        internal string Entity => _entity;
        internal GameWorld World => _world;
        internal bool Authority => _authority;
        internal bool ContainmentRaid => _containmentRaid && _selected;
        internal bool RaidStarted => _raidStarted;
        internal bool Moving => _moving;
        private bool _moving;
        private float _seenSince = -1f, _chaseBegan = -1f, _nextChase;
        private int _chases;
        private string _chaseTarget, _avoidTarget, _triggerObserver;
        private float _avoidUntil;
        private Vector3 _lastKnownTarget;
        internal bool ContainmentReady;

        internal bool InsideContainment => _active && NightmareContainmentRoom.Contains(_position);
        internal bool TryContain(Player player)
        {
            if (!_authority || !ContainmentReady || !InsideContainment || Facility.containmentAt >= 0d || !Alive(player) || player.IsAI || _headless && player.IsYourPlayer || (player.Position - NightmareContainmentRoom.ButtonPosition(_config)).sqrMagnitude > 9f)
                return false;
            Facility.containmentAt = Clock;
            SetMoving(false);
            SendState();
            return true;
        }

        internal bool RequestContainment(Player player)
        {
            if (_authority) return TryContain(player);
            if (!InsideContainment || !Alive(player) || !player.IsYourPlayer || _headless || !_network.Ready)
                return false;
            return _network.SendToServer("SC173|C|" + Newtonsoft.Json.JsonConvert.SerializeObject(new DebugRequest { entity = _entity, player = player.ProfileId }));
        }

        private bool AwaitAwakening(float now)
        {
            if (_awakened) return false;
            string observer = HumanObserver(now);
            if (observer == null)
            {
                _seenSince = -1f;
                _triggerObserver = null;
                return true;
            }
            if (_triggerObserver != observer) { _triggerObserver = observer; _seenSince = now; }
            if (_seenSince < 0f) _seenSince = now;
            if (now - _seenSince < _config.activationLookSeconds) return true;
            StartAwakening(now);
            return true;
        }

        private void StartAwakening(float now)
        {
            _awakened = true;
            _activatedAt = now;
            Facility.awakeAt = now;
            Facility.warningDue = now + _config.activationBlackoutSeconds + _config.warningDelaySeconds;
            Facility.removalDue = _random.NextDouble() * 100d < _config.automaticContainmentChancePercent ? now + Mathf.Lerp(_config.automaticContainmentMinSeconds, _config.automaticContainmentMaxSeconds, (float)_random.NextDouble()) : -1d;
            _nextTeleport = now + _config.teleportCooldownSeconds;
            _lastObserved = now;
            _safeUntil = now + _config.activationBlackoutSeconds + _config.afterTeleportGraceSeconds;
            SendState(sound: _config.activationAudio);
        }

        private void TickFacilityBehaviour(float now)
        {
            if (!_active) return;
            if (Facility.containmentAt >= 0d)
            {
                SetMoving(false);
                if (now >= Facility.containmentAt + _config.containmentSeconds) RemoveEntity("player");
                return;
            }
            if (!_awakened) return;
            if (Facility.removalDue >= 0d && now >= Facility.removalDue)
            {
                RemoveEntity("bots");
                return;
            }
            if (!Facility.flashTeleported && now >= Facility.awakeAt + .1d && now < Facility.awakeAt + _config.activationBlackoutSeconds && now >= _nextTeleportTry)
            {
                _nextTeleportTry = now + .2f;
                if (TryTeleport(now, TeleportTarget())) Facility.flashTeleported = true;
            }
        }

        private void RemoveEntity(string reason)
        {
            if (Facility.announcement != "SCPWarn") { Facility.warningCompleted = true; Facility.warningDue = -1d; }
            _active = false;
            _attempted = true;
            Facility.removal = reason;
            SetMoving(false);
            StopAudio();
            _body?.SetActive(false);
            _identity?.Dispose();
            SendState();
        }

        private bool TryTeleport(float now, Player target)
        {
            if (target == null || !FindSafePoint(target.Position, _config.teleportMinDistance, _config.teleportMaxDistance, out var point)) return false;
            SetMoving(false);
            ChooseTeleportPose();
            SetPosition(point);
            _nextTeleport = now + _config.teleportCooldownSeconds;
            _teleports++;
            _safeUntil = now + _config.afterTeleportGraceSeconds;
            _corners = Array.Empty<Vector3>();
            _nextPath = 0f;
            _chaseBegan = -1f;
            _chases = 0;
            _chaseTarget = null;
            SendState(sound: ChooseSound(_config.teleportAudio, _config.teleportAudioVariants, ref _lastTeleportSound));
            return true;
        }

        private void SetMoving(bool moving)
        {
            _moving = moving;
            if (!moving)
            {
                _movementAudio?.Stop();
                if (IsMoveSound(_pendingSound)) _pendingSound = null;
            }
        }

        private bool IsMoveSound(string name) => !string.IsNullOrEmpty(name) && _config != null && (name == _config.moveAudio || Array.IndexOf(_config.moveAudioVariants, name) >= 0);
        private void ResetBehaviour()
        {
            Facility = new NightmareFacilityState();
            _seenSince = _chaseBegan = -1f;
            _nextChase = _avoidUntil = 0f;
            _chases = 0;
            _chaseTarget = _avoidTarget = null;
            _triggerObserver = null;
            _moving = false;
            ContainmentReady = false;
        }

        private Player ChaseTarget(float now)
        {
            Player closest = null;
            float distance = float.MaxValue;
            foreach (var player in _world.AllAlivePlayersList)
            {
                if (!Alive(player) || player.IsAI && !_config.killBots || _headless && player.IsYourPlayer || now < _avoidUntil && player.ProfileId == _avoidTarget) continue;
                if (player.ProfileId == _chaseTarget) return player;
                float d = (player.Position - _position).sqrMagnitude;
                if (d < distance) { closest = player; distance = d; }
            }
            return closest;
        }

        private bool ChaseDestination(Player target, float now, out Vector3 destination)
        {
            if (_chases >= _config.chasesBeforeRetarget)
            {
                _avoidTarget = target.ProfileId;
                _avoidUntil = now + _config.chaseRetrySeconds;
                _chaseTarget = null;
                var other = ChaseTarget(now);
                if (other != null) TryTeleport(now, other);
                _chases = 0;
                destination = _lastKnownTarget;
                return false;
            }
            if (_chaseTarget != target.ProfileId)
            {
                _chaseTarget = target.ProfileId;
                _chaseBegan = -1f;
                _lastKnownTarget = target.Position;
                _corners = Array.Empty<Vector3>();
                _nextPath = 0f;
            }
            if (ClearLine(_position + Vector3.up, target.Position + Vector3.up)) _lastKnownTarget = target.Position;
            if (now < _nextChase) { destination = _lastKnownTarget; return false; }
            if (_chaseBegan < 0f) _chaseBegan = now;
            destination = ClearLine(_position + Vector3.up, target.Position + Vector3.up) ? target.Position : _lastKnownTarget;
            if ((_position - destination).sqrMagnitude < .16f || now - _chaseBegan >= _config.chaseDurationSeconds)
            {
                _chaseBegan = -1f;
                _nextChase = now + .2f;
                if (++_chases >= _config.chasesBeforeRetarget)
                {
                    _avoidTarget = target.ProfileId;
                    _avoidUntil = now + _config.chaseRetrySeconds;
                    _chaseTarget = null;
                    var other = ChaseTarget(now);
                    if (other != null) TryTeleport(now, other);
                    _chases = 0;
                }
                return false;
            }
            return true;
        }

        private bool CanStep(Vector3 proposed, out Vector3 grounded)
        {
            grounded = proposed;
            if (!GroundPoint(proposed, false, out grounded)) return false;
            if (Math.Abs(grounded.y - _position.y) > _config.chaseStepHeight) return false;
            var delta = grounded - _position;
            if (delta.sqrMagnitude < .0001f) return false;
            float low = Math.Max(_config.bodyRadius + .05f, _config.chaseStepHeight + _config.bodyRadius);
            return !Physics.CapsuleCast(_position + Vector3.up * low, _position + Vector3.up * (_config.height - _config.bodyRadius), _config.bodyRadius, delta.normalized, delta.magnitude, _geometryMask, QueryTriggerInteraction.Ignore);
        }
    }
}
