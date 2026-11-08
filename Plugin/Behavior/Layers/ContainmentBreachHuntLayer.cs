using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISBSpecialForces.Behavior.ISB;
using ISBSpecialForces.Behavior.Tactics;
using ISBSpecialForces.Components;
using MoreBotsAPI.Behavior.Actions;
using MoreBotsAPI.Behavior.Layers;
using MoreBotsAPI.Components;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Behavior.Layers
{
    internal sealed class ContainmentBreachHuntLayer : HuntTargetLayer
    {
        public ContainmentBreachHuntLayer(BotOwner bot, int priority) : base(bot, priority) { }
        public override string GetName() => ISBEventPopulationQueue.IsContainmentHunt ? "ContainmentBreachHunt" : "BlackoutHunt";

        public override bool IsActive()
        {
            if (!ContainmentBreachHunt.CanHunt(BotOwner, out var squad, out bool huntsPlayer) ||
                !squad.Prepare(huntManager, huntsPlayer, out _))
                return false;
            getNextAction();
            if (nextAction == typeof(HuntTargetAction)) nextAction = typeof(ContainmentBreachHuntMove);
            else if (nextAction == typeof(SearchForTargetAction)) nextAction = typeof(ContainmentBreachHuntSearch);
            else if (nextAction == typeof(HuntRegroupAction) && !squad.HasNativeRegroup(BotOwner))
                nextAction = typeof(ContainmentBreachHuntRegroup);
            return true;
        }

        public override void Stop()
        {
            if (huntManager != null && !ISBEventPopulationQueue.TryGetHuntSquad(BotOwner, out _, out _))
            {
                huntManager.active = false;
                huntManager.shouldSearch = huntManager.shouldRegroup = huntManager.isRegrouping = false;
            }
            base.Stop();
        }
    }

    internal sealed class ContainmentBreachHuntMove : HuntTargetAction
    {
        private BotHuntManager _manager;

        public ContainmentBreachHuntMove(BotOwner bot) : base(bot) { }
        public override void Start()
        {
            base.Start();
            _manager = BotOwner.GetComponent<BotHuntManager>();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!ContainmentBreachHunt.CanHunt(BotOwner, out var squad, out bool huntsPlayer) ||
                !squad.Prepare(_manager, huntsPlayer, out _))
                return;
            base.Update(data);
            if (BotOwner.Boss?.IamBoss == true)
                _manager.shouldRegroup = _manager.isRegrouping = false;
        }
    }

    internal sealed class ContainmentBreachHuntRegroup : HuntTargetAction
    {
        private BotHuntManager _manager;
        public ContainmentBreachHuntRegroup(BotOwner bot) : base(bot) { }
        public override void Start()
        {
            base.Start();
            _manager = BotOwner.GetComponent<BotHuntManager>();
        }
        public override void Update(CustomLayer.ActionData data)
        {
            if (!ContainmentBreachHunt.CanHunt(BotOwner, out var squad, out bool huntsPlayer) ||
                !squad.Prepare(_manager, huntsPlayer, out _) || !_manager.shouldRegroup ||
                !squad.RegroupPoint(BotOwner, out var point)) return;
            _manager.knownLocation = _manager.regroupPoint = point;
            base.Update(data);
            BotOwner.Sprint(true);
        }
        public override void Stop()
        {
            BotOwner.Sprint(false);
            base.Stop();
        }
    }

    internal sealed class ContainmentBreachHuntSearch : SearchForTargetAction
    {
        private BotHuntManager _manager;
        public ContainmentBreachHuntSearch(BotOwner bot) : base(bot) { }
        public override void Start()
        {
            base.Start();
            _manager = BotOwner.GetComponent<BotHuntManager>();
        }
        public override void Update(CustomLayer.ActionData data)
        {
            if (ContainmentBreachHunt.CanHunt(BotOwner, out var squad, out bool huntsPlayer) &&
                squad.Prepare(_manager, huntsPlayer, out _))
                base.Update(data);
        }
    }

    internal sealed class ContainmentBreachHunt
    {
        private static readonly ConditionalWeakTable<object, ContainmentBreachHunt> Squads = new ConditionalWeakTable<object, ContainmentBreachHunt>();
        private readonly object _identity;
        private BotOwner _leader;
        private IPlayer _target, _rejectedTarget;
        private BotsGroup _targetGroup, _rejectedGroup;
        private Vector3 _snapshot;
        private bool _huntsPlayer;
        private float _nextTick, _nextSelection, _nextSnapshot, _rejectedUntil;
        private readonly Dictionary<BotOwner, RegroupAnchor> _anchors = new Dictionary<BotOwner, RegroupAnchor>();
        private readonly NavMeshPath _stuckPath = new NavMeshPath();
        private Vector3 _progressPosition;
        private IPlayer _progressTarget;
        private float _progressAt, _lastProgressTick;

        private sealed class RegroupAnchor
        {
            internal Vector3 Point, LeaderAt, NativeLeaderAt, ProgressPosition;
            internal float NextPoint, NextNativePoint, NextProgress, ProgressAt, SuppressedUntil;
            internal bool HasPoint, HasNativePoint;
        }

        private ContainmentBreachHunt(object identity) { _identity = identity; }
        internal static bool Alive(BotOwner bot) => bot != null && bot.isActiveAndEnabled && !bot.IsDead && bot.GetPlayer?.HealthController?.IsAlive == true;

        internal static bool CanHunt(BotOwner bot, out ContainmentBreachHunt squad, out bool huntsPlayer)
        {
            squad = null;
            huntsPlayer = false;
            if (!ISBFikaAuthority.IsAiAuthority || !Alive(bot) || bot.BotsGroup == null || bot.Mover?.ActualPathController == null ||
                !ISBEventPopulationQueue.TryGetHuntSquad(bot, out var identity, out huntsPlayer))
                return false;
            if ((bot.Brain?.BaseBrain as ISBNativeBrain)?.NeedsUrgentAction == true ||
                bot.Medecine?.Using == true || bot.Medecine?.FirstAid?.Have2Do == true || bot.Medecine?.SurgicalKit?.HaveWork == true ||
                bot.BewareGrenade?.ShallRunAway() == true || bot.BewarePlantedMine?.CanDeactivate() == true ||
                bot.FlashGrenade?.IsFlashed == true || bot.WeaponManager?.Grenades?.ThrowindNow == true)
                return false;
            var memory = bot.Memory;
            if (memory == null || memory.IsUnderFire || memory.LastTimeHit > 0f && Time.time - memory.LastTimeHit < 2f ||
                memory.GoalEnemy?.IsVisible == true || memory.GoalEnemy?.CanShoot == true)
                return false;
            if (ISBTacticalAgent.GetOrAdd(bot, false)?.CanRunMission() != true)
                return false;
            squad = Squads.GetValue(identity, key => new ContainmentBreachHunt(key));
            return true;
        }

        internal static bool TryRetarget(BotHuntManager manager)
        {
            var bot = manager?.botOwner;
            if (!ISBFikaAuthority.IsAiAuthority || !Alive(bot) || bot.BotsGroup == null ||
                !ISBEventPopulationQueue.TryGetHuntSquad(bot, out var identity, out bool huntsPlayer))
                return false;
            if (CanHunt(bot, out var squad, out _)) squad.Prepare(manager, huntsPlayer, out _);
            return true;
        }

        internal static bool HasReadyMission(BotOwner bot) => CanHunt(bot, out var squad, out bool huntsPlayer) &&
            squad.Prepare(bot.GetComponent<BotHuntManager>(), huntsPlayer, out _);

        private static void Release(BotHuntManager manager)
        {
            if (manager == null) return;
            var group = manager.botOwner?.BotsGroup;
            if (group != null) manager.huntManager?.huntGroups.Remove(group);
            manager.active = false;
            manager.huntTarget = null;
            manager.shouldSearch = manager.shouldRegroup = manager.isRegrouping = false;
        }

        internal bool Prepare(BotHuntManager manager, bool huntsPlayer, out bool hold)
        {
            hold = false;
            var bot = manager?.botOwner;
            if (bot == null && manager != null) bot = manager.GetComponent<BotOwner>();
            if (!Alive(bot) || bot.BotsGroup == null) return false;
            var controller = MonoBehaviourSingleton<HuntManager>.Instance;
            if (controller == null) return false;
            if (!manager.active || manager.botOwner != bot || manager.huntManager != controller)
                manager.Init(bot, controller);
            if (!ISBEventPopulationQueue.IsHuntSquadAssembled(_identity))
            {
                Release(manager);
                return false;
            }
            Tick(bot, huntsPlayer);
            if (_target?.HealthController?.IsAlive != true || !Alive(_leader))
            {
                Release(manager);
                return false;
            }
            controller.huntGroups[bot.BotsGroup] = _target;
            float gap = (bot.Position - _leader.Position).sqrMagnitude;
            bool regroup = bot != _leader && ShouldRegroup(bot, gap, manager.shouldRegroup);
            if (manager.huntTarget != _target || (manager.knownLocation - _snapshot).sqrMagnitude > 16f || regroup ||
                Mathf.Abs(bot.Position.y - _snapshot.y) > 1.5f)
                manager.shouldSearch = false;
            manager.active = true;
            manager.huntTarget = _target;
            manager.knownLocation = _snapshot;
            manager.ignoreRegroup = false;
            manager.shouldRegroup = manager.isRegrouping = regroup;
            if (regroup && HasNativeRegroup(bot))
            {
                var anchor = Anchor(bot);
                if (Time.time >= anchor.NextNativePoint)
                {
                    anchor.NextNativePoint = Time.time + 2f;
                    if (!anchor.HasNativePoint || (anchor.NativeLeaderAt - _leader.Position).sqrMagnitude >= 9f)
                    {
                        anchor.HasNativePoint = true;
                        anchor.NativeLeaderAt = _leader.Position;
                        manager.regroupPointDirty = true;
                    }
                }
            }
            return true;
        }

        internal bool HasNativeRegroup(BotOwner bot)
        {
            var native = bot?.BotFollower?.BossToFollow?.Player()?.AIData?.BotOwner;
            return bot != _leader && bot?.Boss?.IamBoss == false && native == _leader &&
                bot.BotsGroup?.BossGroup?.Boss == _leader;
        }

        private RegroupAnchor Anchor(BotOwner bot)
        {
            if (!_anchors.TryGetValue(bot, out var anchor))
                _anchors[bot] = anchor = new RegroupAnchor { ProgressPosition = bot.Position, ProgressAt = Time.time };
            return anchor;
        }

        private bool ShouldRegroup(BotOwner bot, float gap, bool wasRegrouping)
        {
            var anchor = Anchor(bot);
            float now = Time.time;
            if (now < anchor.SuppressedUntil) return false;
            bool needed = gap > (wasRegrouping ? 64f : 144f);
            if (!needed) { anchor.ProgressAt = now; anchor.ProgressPosition = bot.Position; return false; }
            if (now < anchor.NextProgress) return true;
            bool interrupted = now - anchor.NextProgress > 1.5f;
            anchor.NextProgress = now + .5f;
            if (interrupted || (bot.Position - anchor.ProgressPosition).sqrMagnitude >= .36f)
            {
                anchor.ProgressPosition = bot.Position;
                anchor.ProgressAt = now;
            }
            else if (now - anchor.ProgressAt >= 9f)
            {
                anchor.SuppressedUntil = now + 8f;
                anchor.ProgressAt = now;
                return false;
            }
            return true;
        }

        internal bool RegroupPoint(BotOwner bot, out Vector3 point)
        {
            point = bot.Position;
            if (!Alive(_leader) || bot == _leader) return false;
            var anchor = Anchor(bot);
            float now = Time.time;
            if (anchor.HasPoint && now < anchor.NextPoint) { point = anchor.Point; return true; }
            anchor.NextPoint = now + 2f;
            if (anchor.HasPoint && (anchor.LeaderAt - _leader.Position).sqrMagnitude < 9f) { point = anchor.Point; return true; }
            int index = 0;
            for (int i = 0; i < bot.BotsGroup.MembersCount; i++)
            {
                var member = bot.BotsGroup.Member(i);
                if (member == bot) break;
                if (member != _leader && IsMember(member)) index++;
            }
            var forward = _leader.LookDirection;
            forward.y = 0f;
            forward = forward.sqrMagnitude > .01f ? forward.normalized : Vector3.forward;
            var desired = _leader.Position - forward * (2f + index / 2) + Vector3.Cross(Vector3.up, forward) * (index % 2 == 0 ? -1.25f : 1.25f);
            if (NavMesh.SamplePosition(desired, out var sample, 2f, NavMesh.AllAreas) && Mathf.Abs(sample.position.y - _leader.Position.y) < 1.5f ||
                NavMesh.SamplePosition(_leader.Position, out sample, 1f, NavMesh.AllAreas) && Mathf.Abs(sample.position.y - _leader.Position.y) < 1.5f)
            {
                anchor.Point = point = sample.position;
                anchor.LeaderAt = _leader.Position;
                anchor.HasPoint = true;
                return true;
            }
            return false;
        }

        internal static bool TryRegroupPoint(BotHuntManager manager, out Vector3 point)
        {
            point = Vector3.zero;
            var bot = manager?.botOwner;
            if (!CanHunt(bot, out var squad, out bool huntsPlayer) || !squad.Prepare(manager, huntsPlayer, out _) || squad.HasNativeRegroup(bot)) return false;
            return squad.RegroupPoint(bot, out point);
        }

        private void Tick(BotOwner candidate, bool huntsPlayer)
        {
            float now = Time.time;
            if (_huntsPlayer != huntsPlayer)
            {
                _huntsPlayer = huntsPlayer;
                _target = null;
                _targetGroup = null;
                _nextTick = _nextSelection = _nextSnapshot = 0f;
            }
            if (now < _nextTick && Alive(_leader) && _leader.BotsGroup == candidate.BotsGroup) return;
            _nextTick = now + 0.5f;
            var leader = SelectLeader(candidate);
            if (_leader != leader)
            {
                _leader = leader;
                _anchors.Clear();
                _nextSnapshot = 0f;
            }
            if (!Alive(_leader)) return;
            SelectTarget(_leader, candidate.BotsGroup, now);
            if (_target != null && now >= _nextSnapshot)
            {
                _snapshot = _target.Position;
                _nextSnapshot = now + 2.5f;
            }
            TrackProgress(now);
        }

        private void TrackProgress(float now)
        {
            if (_target == null) return;
            bool canMove = CanHunt(_leader, out _, out _);
            if (!canMove || _progressTarget != _target || now - _lastProgressTick > 1.5f || (_leader.Position - _progressPosition).sqrMagnitude >= .36f)
            {
                _progressTarget = _target;
                _progressPosition = _leader.Position;
                _progressAt = now;
            }
            _lastProgressTick = now;
            if (!canMove) return;
            if (now - _progressAt < 9f) return;
            _progressAt = now;
            var delta = _snapshot - _leader.Position;
            bool blocked = delta.sqrMagnitude > 25f || Mathf.Abs(delta.y) > 1.5f;
            if (!blocked)
            {
                blocked = !NavMesh.SamplePosition(_snapshot, out var sample, 1f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(_leader.Position, sample.position, NavMesh.AllAreas, _stuckPath) || _stuckPath.status != NavMeshPathStatus.PathComplete;
                if (!blocked)
                {
                    float length = 0f;
                    var corners = _stuckPath.corners;
                    for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
                    blocked = length > 7f;
                }
            }
            if (blocked) RejectTarget(_leader);
        }

        internal void RejectTarget(BotOwner bot)
        {
            if (bot != _leader || _target == null) return;
            _rejectedTarget = _target;
            _rejectedGroup = _targetGroup;
            _rejectedUntil = Time.time + 8f;
            _target = null;
            _targetGroup = null;
            _nextTick = _nextSelection = _nextSnapshot = 0f;
        }

        private bool EligibleTarget(IPlayer target, BotOwner bot, BotsGroup group, float now)
        {
            if (target?.HealthController?.IsAlive != true || target.IsAI == _huntsPlayer || target.ProfileId == bot.ProfileId ||
                ReferenceEquals(target, _rejectedTarget) && now < _rejectedUntil ||
                !target.IsAI && target.IsYourPlayer && !ISBFikaAuthority.CanPresentLocally)
                return false;
            if (target.IsAI)
            {
                var owner = target.AIData?.BotOwner;
                if (!Alive(owner) || owner.Profile?.Info?.Settings == null || owner.BotsGroup == null || owner.BotsGroup == group ||
                    owner.BotsGroup == _rejectedGroup && now < _rejectedUntil) return false;
            }
            return group.IsPlayerEnemy(target);
        }

        private void SelectTarget(BotOwner bot, BotsGroup group, float now)
        {
            if (EligibleTarget(_target, bot, group, now) && now < _nextSelection) return;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            var previous = _target;
            _target = null;
            if (players == null) return;
            bool sameSquad = false, grouped = false;
            float nearest = float.MaxValue;
            foreach (var player in players)
            {
                if (!EligibleTarget(player, bot, group, now)) continue;
                var rival = player.IsAI ? player.AIData.BotOwner.BotsGroup : null;
                bool same = rival != null && rival == _targetGroup;
                bool squad = rival?.MembersCount > 1 || ISBEventPopulationQueue.TryGetHuntSquad(player.AIData?.BotOwner, out _, out _);
                float distance = (player.Position - bot.Position).sqrMagnitude;
                if (sameSquad && !same || sameSquad == same && (grouped && !squad || grouped == squad && distance >= nearest)) continue;
                sameSquad = same;
                grouped = squad;
                nearest = distance;
                _target = player;
            }
            _targetGroup = _target?.IsAI == true ? _target.AIData.BotOwner.BotsGroup : null;
            _nextSelection = now + 12f;
            if (_target != previous) _nextSnapshot = 0f;
        }

        private BotOwner SelectLeader(BotOwner candidate)
        {
            var group = candidate.BotsGroup;
            var native = candidate.BotFollower?.BossToFollow?.Player()?.AIData?.BotOwner;
            if (IsMember(native) && native.BotsGroup == group) return native;
            BotOwner first = null;
            for (int i = 0; i < group.MembersCount; i++)
            {
                var member = group.Member(i);
                if (!IsMember(member)) continue;
                if (member.Boss?.IamBoss == true) return member;
                first ??= member;
            }
            return first ?? candidate;
        }

        private bool IsMember(BotOwner bot) => Alive(bot) &&
            ISBEventPopulationQueue.TryGetHuntSquad(bot, out var identity, out _) && ReferenceEquals(identity, _identity);
    }
}
