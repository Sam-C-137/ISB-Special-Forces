using System;
using System.Collections.Generic;
using EFT;
using ISBSpecialForces.Behavior.Tactics;
using UnityEngine;
using UnityEngine.AI;
#if SPT40
using EngineLayer = BaseLogicLayerSimpleAbstractClass;
using EngineAction = AICoreActionResultStruct<BotLogicDecision, GClass26>;
using EngineEnd = AICoreActionEndStruct;
using EnginePriority = GClass671;
using NativeDanger = GClass48;
using NativeRequest = GClass84;
using NativePatrolDefence = GClass135;
using NativeFlankData = GClass29;
using NativeFinder = GClass483;
using NativeShootPoint = ShootPointClass;
using NativeSettings = LocalBotSettingsProviderClass;
using NativeMalfunction = GClass118;
using NativeSearch = Class99;
using NativeCombat = GClass39;
#else
using EngineLayer = BaseLogicLayer;
using EngineAction = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
using EngineEnd = AICoreActionEnd;
using EnginePriority = BotEventsPriority;
using NativeDanger = AvoidDangerLayer;
using NativeRequest = FightRequestLayer;
using NativePatrolDefence = PatrolStayAtPositionLayer;
using NativeFlankData = CoreActionResultParamsFlankMove;
using NativeFinder = BotFindPlaceToShoot;
using NativeShootPoint = ShootToPoint;
using NativeSettings = BotInternalSettingsController;
using NativeMalfunction = MalfunctionLayer;
using NativeSearch = AdvAssaultTargetLayer;
using NativeCombat = AssaultHaveEnemyLayer;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBRuntimeBindings
    {
        internal static Func<bool> IsAuthority = () => false;
        internal static Func<BotOwner, bool> IsEligible = _ => false;
        internal static Func<BotOwner, Vector3?> DefensivePoint = _ => null;
        internal static Action<Exception> CriticalFailure = _ => { };
    }

    internal class ISBNativeBrain : BaseBrain
    {
        internal const int Identifier = 137;
        internal const BotLogicDecision SuppressionAdvance = (BotLogicDecision)1371;
        internal const BotLogicDecision ContactAdvance = (BotLogicDecision)1372;
        internal readonly BotOwner Bot;
        internal readonly string BrainName;
        internal string LayerName(string name) => BrainName == "blackDiv" ? name.Replace("ISB", "blackDiv") : name;
        internal int BrainIdentifier => BrainName == "blackDiv" ? 130 : 137;
        internal ISBNativeSquad Squad { get; private set; }
        internal bool Disposed { get; private set; }
        private readonly Player _player;
        private readonly ISBDangerLayer _danger;
        private bool _retired;
        private ISBTacticalState? _appliedState;
        private float _nextBodyCheck, _nextReload;

        internal ISBNativeBrain(BotOwner owner) : this(owner, "ISB") { }
        internal ISBNativeBrain(BotOwner owner, string name) : base(owner)
        {
            var core = NativeSettings.Core;
            if (core != null && core.DELTA_GRENADE_RUN_DIST > 0f && core.DELTA_GRENADE_BEWARE_DIST_SQRT <= 0f && core.DELTA_GRENADE_SAFE_DIST_SQRT <= 0f)
                core.Update();
            Bot = owner;
            BrainName = name;
            _player = owner.GetPlayer;
#if SPT40
            method_0(180, new ISBMalfunctionLayer(this), true);
            method_0(181, new ISBSearchLayer(this), true);
            method_0(182, new ISBCombatFallbackLayer(this), true);
#else
            TryAddLayer(180, new ISBMalfunctionLayer(this), true);
            TryAddLayer(181, new ISBSearchLayer(this), true);
            TryAddLayer(182, new ISBCombatFallbackLayer(this), true);
#endif
            foreach (var spec in ISBLayerCatalog.Layers)
            {
                EngineLayer layer;
                switch (spec.Id)
                {
                    case ISBLayerId.AvoidDanger: layer = _danger = new ISBDangerLayer(this, spec.Priority); break;
                    case ISBLayerId.FightRequest: layer = new ISBRequestLayer(this, spec.Priority); break;
                    case ISBLayerId.PatrolDefence: layer = new ISBPatrolDefenceLayer(this, spec.Priority); break;
                    default: layer = new ISBDecisionLayer(this, spec); break;
                }
#if SPT40
                if (!method_0((int)spec.Id, layer, spec.ActiveOnStart))
#else
                if (!TryAddLayer((int)spec.Id, layer, spec.ActiveOnStart))
#endif
                    throw new InvalidOperationException("ISB layer registration failed: " + spec.Name);
            }
            if (_player != null)
                _player.OnPlayerDeadOrUnspawn += Retire;
        }

        public override string ShortName() => BrainName;
        public override EnginePriority EventsPriority() => new EnginePriority(-1, -1, -1, -1, -1, -1);
        internal static bool Owns(BotOwner bot) => bot?.Brain?.BaseBrain is ISBNativeBrain brain && !brain.Disposed;
        internal bool Alive => !Disposed && !_retired && Bot != null && !Bot.IsDead && Bot.GetPlayer?.HealthController?.IsAlive == true;
        internal bool NeedsTreatment => Bot.Medecine?.Using == true || Bot.Medecine?.FirstAid?.Have2Do == true || Bot.Medecine?.SurgicalKit?.HaveWork == true;
        internal bool NeedsUrgentAction => NeedsTreatment || _danger.ShallUseNow();

        public override void ManualUpdate()
        {
            if (!Alive || !ISBRuntimeBindings.IsAuthority())
                return;
            var squad = ISBNativeSquad.Join(this);
            if (Squad != squad)
            {
                Squad?.Remove(this);
                Squad = squad;
            }
            Squad?.Tick(Time.time);
            var status = Squad?.Orders.Leader.Status ?? ISBTacticalState.Defence;
            if (_appliedState != status)
            {
                ISBTacticLayers.Apply(status, SetLayerActive, null);
                _appliedState = status;
            }
            if (Bot.Memory?.GoalEnemy == null)
            {
                if (Time.time >= _nextBodyCheck)
                {
                    _nextBodyCheck = Time.time + 5f;
                    Bot.DeadBodyWork?.UpdateCheck();
                }
                if (Time.time >= _nextReload)
                {
                    _nextReload = Time.time + 15f;
                    var reload = Bot.WeaponManager?.Reload;
                    if (reload != null && reload.MaxBulletCount > 0 && reload.BulletCount < reload.MaxBulletCount * 0.6f)
                        reload.TryReload();
                }
            }
        }

        private void SetLayerActive(ISBLayerId id, bool active)
        {
#if SPT40
            if (active && !method_2(Dictionary_0[(int)id])) method_1((int)id);
            if (!active && method_2(Dictionary_0[(int)id])) method_3((int)id);
#else
            if (active && !IsLayerActive(_layers[(int)id])) ActivateLayer((int)id);
            if (!active && IsLayerActive(_layers[(int)id])) DeactivateLayer((int)id);
#endif
        }

        public override void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;
            if (_player != null)
                _player.OnPlayerDeadOrUnspawn -= Retire;
            Squad?.Remove(this);
            Squad = null;
            base.Dispose();
        }

        private void Retire(Player player)
        {
            _retired = true;
            Squad?.Remove(this);
            Squad = null;
        }
    }

    internal sealed class ISBCombatFallbackLayer : NativeCombat
    {
        private readonly ISBNativeBrain _brain;
        internal ISBCombatFallbackLayer(ISBNativeBrain brain) : base(brain.Bot, 35) { _brain = brain; }
        public override string Name() => _brain.LayerName("ISB Combat");
        public override bool ShallUseNow() => _brain.Alive && ISBRuntimeBindings.IsAuthority() && base.ShallUseNow();
    }

    internal sealed class ISBSearchLayer : NativeSearch
    {
        private readonly ISBNativeBrain _brain;
        internal ISBSearchLayer(ISBNativeBrain brain) : base(brain.Bot, 31, true) { _brain = brain; }
        public override string Name() => _brain.LayerName("ISB Search");
        public override bool ShallUseNow() => _brain.Alive && ISBRuntimeBindings.IsAuthority() &&
            _brain.Bot.Memory?.GoalEnemy == null && base.ShallUseNow();
    }

    internal sealed class ISBMalfunctionLayer : NativeMalfunction
    {
        private readonly ISBNativeBrain _brain;
        internal ISBMalfunctionLayer(ISBNativeBrain brain) : base(brain.Bot, 118) { _brain = brain; }
        public override string Name() => _brain.LayerName("ISB Malfunction");
        public override bool ShallUseNow() => _brain.Alive && ISBRuntimeBindings.IsAuthority() && base.ShallUseNow();
    }

    internal sealed class ISBDangerLayer : NativeDanger
    {
        private readonly ISBNativeBrain _brain;
        internal ISBDangerLayer(ISBNativeBrain brain, int priority) : base(brain.Bot, priority) { _brain = brain; }
        public override string Name() => _brain.LayerName("ISB Avoid Danger");
        public override bool ShallUseNow() => _brain.Alive && ISBRuntimeBindings.IsAuthority() && base.ShallUseNow();
    }

    internal sealed class ISBRequestLayer : NativeRequest
    {
        private readonly ISBNativeBrain _brain;
        internal ISBRequestLayer(ISBNativeBrain brain, int priority) : base(brain.Bot, priority) { _brain = brain; }
        public override string Name() => _brain.LayerName("ISB Fight Request");
        public override bool ShallUseNow() => _brain.Alive && ISBRuntimeBindings.IsAuthority() &&
            (!ISBRuntimeBindings.DefensivePoint(_brain.Bot).HasValue || ISBTacticalAgent.GetOrAdd(_brain.Bot, false)?.CanRunMission() != true) && base.ShallUseNow();
    }

    internal sealed class ISBPatrolDefenceLayer : NativePatrolDefence
    {
        private readonly ISBNativeBrain _brain;
        private Vector3? _position;
        internal ISBPatrolDefenceLayer(ISBNativeBrain brain, int priority) : base(brain.Bot, priority, true)
        {
            _brain = brain;
        }
        public override string Name() => _brain.LayerName("ISB Patrol Defence");
        public override bool ShallUseNow()
        {
            if (!_brain.Alive || !ISBRuntimeBindings.IsAuthority() || _brain.NeedsTreatment)
                return false;
            var point = ISBRuntimeBindings.DefensivePoint(_brain.Bot) ??
                (_brain.Squad?.Leader != null ? ISBRuntimeBindings.DefensivePoint(_brain.Squad.Leader.Bot) : null);
            if (!point.HasValue || _brain.Bot.Memory?.HaveEnemy == true)
                return false;
            if (!_position.HasValue || (_position.Value - point.Value).sqrMagnitude > 1f)
            {
                SetCorePosition(point.Value);
                _position = point;
            }
            return true;
        }
    }

    internal sealed class ISBNativeSquad
    {
        private static readonly Dictionary<ISBSquadKey, ISBNativeSquad> Groups = new Dictionary<ISBSquadKey, ISBNativeSquad>();
        private readonly ISBSquadKey _key;
        internal readonly List<ISBNativeBrain> Members = new List<ISBNativeBrain>();
        internal readonly ISBSquadOrders Orders = new ISBSquadOrders();
        internal readonly ISBReadiness<ISBNativeBrain> Readiness = new ISBReadiness<ISBNativeBrain>();
        private readonly List<ISBAssaultMember> _assault = new List<ISBAssaultMember>();
        private readonly List<float> _contactAges = new List<float>();
        private readonly NavMeshPath _path = new NavMeshPath();
        internal ISBNativeBrain Leader { get; private set; }
        internal EnemyInfo Contact { get; private set; }
        internal Vector3 Center { get; private set; }
        private float _nextTick, _lastHit, _nextHelp;
        private bool _replaceLeader;
        private ISBNativeSquad(ISBSquadKey key) { _key = key; Orders.Leader.NoAttackAi = false; }

        internal static object Formation(BotOwner bot) => (object)bot?.SpawnProfileData?.SpawnParams?.ShallBeGroup ?? bot?.SpawnProfileData?.SpawnParams;
        internal static bool SameFormation(BotOwner first, BotOwner second) => first?.BotsGroup != null && second?.BotsGroup != null &&
            new ISBSquadKey(first.BotsGroup, Formation(first), first.Brain?.BaseBrain is BlackDivBrain)
                .Equals(new ISBSquadKey(second.BotsGroup, Formation(second), second.Brain?.BaseBrain is BlackDivBrain));

        internal static ISBNativeSquad Join(ISBNativeBrain brain)
        {
            var group = brain.Bot.BotsGroup;
            if (group == null)
                return null;
            var key = new ISBSquadKey(group, Formation(brain.Bot), brain is BlackDivBrain);
            if (!Groups.TryGetValue(key, out var squad))
                Groups.Add(key, squad = new ISBNativeSquad(key));
            if (!squad.Members.Contains(brain))
            {
                squad.Members.Add(brain);
                squad.Readiness.Register(brain);
            }
            return squad;
        }

        internal void Remove(ISBNativeBrain brain)
        {
            Members.Remove(brain);
            Readiness.Remove(brain);
            if (ReferenceEquals(Leader, brain))
            {
                Leader = null;
                _replaceLeader = true;
            }
            if (Members.Count == 0 && Groups.TryGetValue(_key, out var current) && ReferenceEquals(current, this))
                Groups.Remove(_key);
        }

        internal static void Reset()
        {
            foreach (var group in Groups.Values)
            {
                group.Readiness.Clear();
                group.Members.Clear();
                group.Leader = null;
                group.Contact = null;
            }
            Groups.Clear();
        }

        internal void Tick(float now)
        {
            if (now < _nextTick)
                return;
            _nextTick = now + 0.25f;
            for (int i = Members.Count - 1; i >= 0; i--)
                if (!Members[i].Alive || !_key.Equals(new ISBSquadKey(Members[i].Bot.BotsGroup, Formation(Members[i].Bot), Members[i] is BlackDivBrain)))
                    Remove(Members[i]);
            if (Members.Count == 0)
                return;
            Members.Sort(CompareMembers);
            if (Leader == null || !Leader.Alive || !Members.Contains(Leader))
                Leader = Members[0];
            else if (Leader.Bot.Boss?.IamBoss != true && Members[0].Bot.Boss?.IamBoss == true)
                Leader = Members[0];
            ReconcileFollowers();
            Center = Vector3.zero;
            Contact = null;
            bool hit = false;
            float latestHit = _lastHit;
            _assault.Clear();
            foreach (var member in Members)
            {
                var bot = member.Bot;
                Center += bot.Position;
                var enemy = bot.Memory?.GoalEnemy;
                float hitAt = bot.Memory?.LastTimeHit ?? 0f;
                if (hitAt > _lastHit && now - hitAt < 1f)
                    hit = true;
                latestHit = Mathf.Max(latestHit, hitAt);
                if (enemy != null && enemy.Person != null &&
                    (Contact == null || enemy.PersonalLastSeenTime > Contact.PersonalLastSeenTime))
                    Contact = enemy;
                bool registered = Readiness.TryGetDeadline(member, out float readyAt);
                _assault.Add(new ISBAssaultMember(enemy != null, enemy?.Distance ?? float.MaxValue,
                    true, registered, readyAt));
            }
            Center /= Members.Count;
            _lastHit = latestHit;
            bool defending = ISBRuntimeBindings.DefensivePoint(Leader.Bot).HasValue;
            Orders.Update(now, ReadContact(now), hit, defending, _assault, () => UnityEngine.Random.value < 0.5f);
            if (Contact != null && now >= _nextHelp && Contact.IsVisible == false)
            {
                _nextHelp = now + 10f;
                AskForHelp(now);
            }
        }

        private void ReconcileFollowers()
        {
            var leader = Leader.Bot;
            if (_replaceLeader && leader.Boss?.IamBoss != true)
            {
                DetachFollower(leader);
                if (Members.Count > 1)
                    leader.Boss?.SetBoss(Members.Count - 1);
            }
            if (leader.Boss?.IamBoss != true)
                return;
            _replaceLeader = false;
            foreach (var member in Members)
            {
                var bot = member.Bot;
                if (bot == leader || bot.Boss?.IamBoss == true || bot.BotFollower == null ||
                    ReferenceEquals(bot.BotFollower.BossToFollow, leader.Boss))
                    continue;
                DetachFollower(bot);
                leader.Boss.OfferSelf(bot);
            }
        }

        private static void DetachFollower(BotOwner bot)
        {
            var follower = bot.BotFollower;
            var previous = follower?.BossToFollow;
            if (previous == null)
                return;
            var player = previous.Player() as Player;
#if SPT40
            if (player != null)
                player.OnPlayerDead -= follower.method_3;
            var owner = player?.AIData?.BotOwner;
            if (owner?.PeacefulActions != null)
                owner.PeacefulActions.OnStartPeacefulMove -= follower.method_4;
            if (owner?.DeadBodyWork != null)
                owner.DeadBodyWork.OnStartLookToBody -= follower.method_0;
#else
            if (player != null)
                player.OnPlayerDead -= follower.OnPlayerDead;
            var owner = player?.AIData?.BotOwner;
            if (owner?.PeacefulActions != null)
                owner.PeacefulActions.OnStartPeacefulMove -= follower.PeacefulActions;
            if (owner?.DeadBodyWork != null)
                owner.DeadBodyWork.OnStartLookToBody -= follower.OnStartLookToBody;
#endif
            previous.RemoveFollower(bot);
            follower.BossToFollow = null;
        }

        internal ISBContact? ReadContact(float now)
        {
            var enemy = Contact;
            if (enemy?.Person == null)
                return null;
            return new ISBContact(enemy.Person.ProfileId, enemy.Person.IsAI, enemy.IsVisible,
                now - (enemy.GroupInfo?.EnemyLastSeenTimeReal ?? enemy.PersonalLastSeenTime), enemy.Distance);
        }

        internal bool GroupSees(string profileId)
        {
            foreach (var member in Members)
            {
                var enemy = member.Bot.Memory?.GoalEnemy;
                if (enemy?.Person?.ProfileId == profileId && enemy.IsVisible)
                    return true;
            }
            return false;
        }

        internal bool RecentlyHit(float now, float period) => _lastHit > 0f && now - _lastHit < period;

        internal bool ReadyToAssault(float now) => ISBLeaderRules.CanStartAssault(Orders.Leader.HasDefensivePlace, _assault, now);

        internal bool MaySuppress(ISBNativeBrain brain)
        {
            int index = Members.IndexOf(brain);
            if (Members.Count == 0 || index < 0)
                return false;
            int phase = (int)(Time.time / 4f) % Members.Count;
            return (index + phase) % Members.Count < Math.Min(2, Members.Count);
        }

        internal bool ShouldCoverPartner(ISBNativeBrain brain)
        {
            var enemy = brain.Bot.Memory?.GoalEnemy;
            if (enemy == null)
                return false;
            foreach (var member in Members)
            {
                var other = member.Bot.Memory?.GoalEnemy;
                if (member != brain && other?.Person?.ProfileId == enemy.Person?.ProfileId && other.Distance > enemy.Distance)
                    return true;
            }
            return false;
        }

        private void AskForHelp(float now)
        {
            var target = ReadContact(now);
            if (!target.HasValue)
                return;
            foreach (var helper in Groups.Values)
            {
                if (helper == this || helper.Leader == null || helper.Members.Count == 0)
                    continue;
                helper._contactAges.Clear();
                foreach (var member in helper.Members)
                    helper._contactAges.Add(now - (member.Bot.Memory?.GoalEnemy?.PersonalLastSeenTime ?? float.MinValue));
                if (!ISBLeaderRules.CanHelp(helper.Orders.Leader.HasDefensivePlace,
                    Position(helper.Center), Position(Center), helper._contactAges))
                    continue;
                var enemy = helper.Contact;
                if (enemy?.Person?.ProfileId != target.Value.Id)
                    continue;
                var direction = enemy.EnemyLastPositionReal - helper.Center;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1f)
                    continue;
                var side = Vector3.Cross(direction.normalized, Vector3.up);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!ISBNavigationBudget.TryBeginSearch(Time.frameCount))
                        return;
                    var candidate = enemy.EnemyLastPositionReal + side * (attempt == 0 ? 35f : -35f);
                    if (NavMesh.SamplePosition(candidate, out var sample, 5f, -1) &&
                        NavMesh.CalculatePath(helper.Leader.Bot.Position, sample.position, -1, helper._path) &&
                        helper._path.status == NavMeshPathStatus.PathComplete)
                    {
                        helper.Orders.Flank(now, target.Value, Position(sample.position));
                        break;
                    }
                }
            }
        }

        private static ISBGroupPosition Position(Vector3 point) => new ISBGroupPosition(point.x, point.y, point.z);
        private static int CompareMembers(ISBNativeBrain a, ISBNativeBrain b)
        {
            int leadership = (a.Bot.Boss?.IamBoss == true ? 0 : 1).CompareTo(b.Bot.Boss?.IamBoss == true ? 0 : 1);
            return leadership != 0 ? leadership : string.CompareOrdinal(a.Bot.Profile.Id, b.Bot.Profile.Id);
        }
    }

    internal sealed class ISBDecisionLayer : EngineLayer
    {
        private readonly ISBNativeBrain _brain;
        private readonly BotOwner _bot;
        private readonly ISBLayerSpec _spec;
        private NativeFinder _finder;
        private string _finderEnemy;
        private float _decisionUntil;
        private int _readinessRevision = -1;
        internal ISBDecisionLayer(ISBNativeBrain brain, ISBLayerSpec spec) : base(brain.Bot, spec.Priority)
        {
            _brain = brain;
            _bot = brain.Bot;
            _spec = spec;
        }
        public override string Name() => _brain.LayerName(_spec.Name);

        public override bool ShallUseNow()
        {
            if (!_brain.Alive || !ISBRuntimeBindings.IsAuthority())
                return false;
            var enemy = _bot.Memory?.GoalEnemy;
            var squad = _brain.Squad;
            bool defence = squad?.Orders.Leader.HasDefensivePlace == true;
            switch (_spec.Id)
            {
                case ISBLayerId.Assault:
                case ISBLayerId.Flank:
                case ISBLayerId.SuppressAssault: return enemy?.Person != null && ISBLayerRules.CanAttack(true, enemy.Person.IsAI);
                case ISBLayerId.Defence: return ISBLayerRules.CanDefend(enemy != null, squad?.Leader != null, defence);
                case ISBLayerId.Fight:
                    return enemy?.Person != null && ISBLayerRules.CanFight(true, squad?.Leader != null, defence,
                        enemy.Person.IsAI, enemy.IsVisible, Time.time - (enemy.GroupInfo?.EnemyLastSeenTimeReal ?? 0f), Time.time - enemy.PersonalLastSeenTime);
                case ISBLayerId.Hide:
                    return ISBLayerRules.CanHide(enemy != null, _bot.Memory?.IsUnderFire == true || Time.time - (_bot.Memory?.LastTimeHit ?? float.MinValue) < 20f,
                        squad?.RecentlyHit(Time.time, 20f) == true);
                case ISBLayerId.DeadBody: return enemy == null && _bot.DeadBodyWork?.ShallUse == true;
                case ISBLayerId.Pursuit:
                    return enemy?.Person != null && !enemy.IsVisible && !enemy.CanShoot && squad != null &&
                        squad.Orders.Leader.CanPursue(Time.time, true, Time.time, enemy.GroupInfo?.EnemyLastSeenTimeReal ?? enemy.PersonalLastSeenTime);
                case ISBLayerId.ControlBuilding:
                    return enemy?.Person?.AIData != null && _bot.AIData != null && defence && enemy.Distance < 20f && !enemy.IsVisible &&
                        _bot.AIData.EnvironmentId == enemy.Person.AIData.EnvironmentId && _bot.AIData.EnvironmentId > 0;
                case ISBLayerId.FollowerPatrol: return enemy == null && _bot.BotFollower?.HaveBoss == true;
                case ISBLayerId.Patrol: return true;
                default: return false;
            }
        }

        public override EngineAction GetDecision()
        {
            _decisionUntil = Time.time + 2f;
            var enemy = _bot.Memory?.GoalEnemy;
            if (enemy != null)
            {
                var immediate = InFightLogic();
                if (immediate.HasValue)
                    return immediate.Value;
            }
            bool heal = ShouldHeal(enemy);
            if (_spec.Id == ISBLayerId.Hide)
                ISBRetreatCover.DiscardUnsafe(_bot);
            var squad = _brain.Squad;
            if (_spec.Id == ISBLayerId.Assault && enemy != null && enemy.Distance < 30f &&
                !_bot.Memory.IsInCover && squad != null && _readinessRevision != squad.Orders.Revision)
            {
                squad.Readiness.ReadyToAttack(_brain, Time.time);
                _readinessRevision = squad.Orders.Revision;
            }
            var input = new ISBCombatInput
            {
                HasEnemy = enemy != null, Visible = enemy?.IsVisible == true, CanShoot = enemy?.CanShoot == true,
                EnemyDistance = enemy?.Distance ?? float.MaxValue, InCover = _bot.Memory?.IsInCover == true,
                NeedsHeal = heal, GroupSeesEnemy = enemy?.Person != null && squad?.GroupSees(enemy.Person.ProfileId) == true,
                ReadyToAssault = squad?.ReadyToAssault(Time.time) == true,
                CoverPartner = squad?.ShouldCoverPartner(_brain) == true,
                HasFlankPoint = squad?.Orders.Leader.FlankPoint.HasValue == true,
                CanSuppressAdvance = squad?.MaySuppress(_brain) == true,
                HasBoss = _bot.BotFollower?.HaveBoss == true, IsLeader = _bot.Boss?.IamBoss == true,
                CanAdvanceToCover = enemy != null && enemy.Distance >= 30f
            };
            if (enemy != null && (_spec.Id == ISBLayerId.Fight || _spec.Id == ISBLayerId.Defence) && !heal)
            {
                _finder = _finder ?? _bot.FindPlaceToShoot.Register(_spec.Id == ISBLayerId.Fight ? 15 : 20,
                    _spec.Id == ISBLayerId.Fight ? 8 : 3, 0.5f);
                if (_finderEnemy != enemy.Person?.ProfileId)
                {
                    _finder.Drop();
                    _finderEnemy = enemy.Person?.ProfileId;
                }
                _finder.ManualUpdateSearch(new NativeShootPoint(enemy.EnemyLastPositionReal + Vector3.up * 1.2f), 1f, out var ignored);
                input.HasFiringPoint = _finder.LastGoodPoint.HasValue && (_finder.LastGoodPoint.Value - _bot.Position).sqrMagnitude > 2.25f;
                input.CanReachFiringPoint = input.HasFiringPoint;
            }
            if (enemy != null && !enemy.IsVisible && Time.time - enemy.PersonalLastSeenTime >= 3f && !heal &&
                !input.GroupSeesEnemy && (_spec.Id == ISBLayerId.Fight || _spec.Id == ISBLayerId.Defence))
                return ToNative(ISBAction.Advance, enemy);
            return ToNative(ISBCombatPlan.Select(_spec.Id, input), enemy);
        }

        private bool ShouldHeal(EnemyInfo enemy)
        {
            if (enemy != null && Time.time - (enemy.GroupInfo?.EnemyLastSeenTimeReal ?? enemy.PersonalLastSeenTime) < 20f)
                return false;
            var medicine = _bot.Medecine;
            return medicine != null && (medicine.Using || medicine.FirstAid?.Have2Do == true || medicine.SurgicalKit?.HaveWork == true);
        }

        private EngineAction ToNative(ISBAction action, EnemyInfo enemy)
        {
            switch (action)
            {
                case ISBAction.Shoot: return Result(BotLogicDecision.shootFromPlace);
                case ISBAction.Heal: return Result(BotLogicDecision.heal);
                case ISBAction.Advance: return Result(ISBNativeBrain.ContactAdvance);
                case ISBAction.SuppressAdvance: return Result(ISBNativeBrain.SuppressionAdvance);
                case ISBAction.Suppress:
                    return Result(enemy != null && Time.time - enemy.PersonalLastSeenTime <= 1.5f && enemy.Distance > 5f ?
                        BotLogicDecision.suppressFire : ISBNativeBrain.ContactAdvance);
                case ISBAction.Flank:
                    var flank = _brain.Squad.Orders.Leader.FlankPoint;
                    if (flank.HasValue)
                    {
                        var point = new Vector3(flank.Value.X, flank.Value.Y, flank.Value.Z);
                        if (enemy != null && Vector3.Dot(point - _bot.Position, enemy.EnemyLastPositionReal - _bot.Position) <= 0f)
                            return Result(BotLogicDecision.attackMoving);
                        return new EngineAction(BotLogicDecision.moveStealthy, Name(), new NativeFlankData(point));
                    }
                    return Result(ISBNativeBrain.ContactAdvance);
                case ISBAction.FiringPosition:
                    if (_finder?.LastGoodPoint.HasValue == true)
                    {
                        if ((_finder.LastGoodPoint.Value - _bot.Position).sqrMagnitude <= 1f)
                            return Result(ISBNativeBrain.ContactAdvance);
                        _bot.GoToSomePointData.SetPoint(_finder.LastGoodPoint.Value);
                        return Result(BotLogicDecision.goToPoint);
                    }
                    return Result(BotLogicDecision.attackMoving);
                case ISBAction.Cover: return Result(enemy == null ?
                    (_bot.CanSprintPlayer ? BotLogicDecision.runToCover : BotLogicDecision.goToCoverPoint) : BotLogicDecision.attackMoving);
                case ISBAction.Body: return Result(BotLogicDecision.deadBody);
                case ISBAction.Follow: return Result(BotLogicDecision.followerPatrol);
                case ISBAction.Patrol: return Result(BotLogicDecision.simplePatrol);
                default: return Result(BotLogicDecision.holdPosition);
            }
        }

        private EngineAction Result(BotLogicDecision action) => new EngineAction(action, Name());

        public override EngineEnd ShallEndCurrentDecision(EngineAction current)
        {
            if (!_brain.Alive || !ShallUseNow() || Time.time >= _decisionUntil)
                return new EngineEnd(_brain.LayerName("ISB reassess"), true);
            var contact = _bot.Memory?.GoalEnemy;
            if (contact?.IsVisible == true && contact.CanShoot && current.Action != BotLogicDecision.shootFromPlace &&
                current.Action != BotLogicDecision.heal && current.Action != BotLogicDecision.attackMoving)
                return new EngineEnd(_brain.LayerName("ISB visual contact"), true);
            if (current.Action == ISBNativeBrain.SuppressionAdvance || current.Action == ISBNativeBrain.ContactAdvance || current.Action == BotLogicDecision.suppressFire)
            {
                var enemy = _bot.Memory?.GoalEnemy;
                return new EngineEnd(_brain.LayerName("ISB contact changed"), enemy == null || enemy.IsVisible && enemy.CanShoot);
            }
            return base.ShallEndCurrentDecision(current);
        }

        public override CustomNavigationPoint FindPoint(CoverSearchData data,
            Func<CoverSearchData, CustomNavigationPoint> find, bool checkCurrent)
        {
            if (_spec.Id == ISBLayerId.Hide)
                return ISBRetreatCover.Find(_bot, data, (search, current) => base.FindPoint(search, find, current), checkCurrent);
            if (_spec.Id == ISBLayerId.Assault && _bot.Memory?.GoalEnemy != null)
            {
                data.CenterPos = (_bot.Position + _bot.Memory.GoalEnemy.EnemyLastPositionReal) * 0.5f;
                data.MaxDistSqr = 400f;
            }
            return base.FindPoint(data, find, checkCurrent);
        }
    }
}
