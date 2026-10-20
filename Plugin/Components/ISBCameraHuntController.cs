using Comfort.Common;
using EFT;
using ISBSpecialForces.Behavior;
using ISBSpecialForces.Behavior.Tactics;
using ISBSpecialForces.Controllers;
using MoreBotsAPI.Components;
using SPT.SinglePlayer.Utils.InRaid;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public static class ISBAnnouncementEvents
    {
        public static void OnAudioStarted(string audioName)
        {
            if (ISBCameraHuntPolicy.IsTrigger(audioName))
                MonoBehaviourSingleton<ISBCameraHuntController>.Instance?.Activate();
        }
    }

    internal sealed class ISBCameraHuntController : MonoBehaviourSingleton<ISBCameraHuntController>
    {
        private sealed class Hunter
        {
            internal BotOwner Bot;
            internal BotHuntManager Manager;
            internal BotsGroup Group;
            internal bool OriginalActive, OriginalIgnoreRegroup;
            internal IPlayer OriginalTarget;
            internal List<IPlayer> OriginalPriorities;
        }

        private sealed class Squad
        {
            internal readonly List<BotHuntManager> Members = new List<BotHuntManager>();
            internal readonly List<BotHuntManager> Followers = new List<BotHuntManager>();
            internal IPlayer Target, OriginalTarget;
            internal bool HadOriginalTarget;
        }

        private readonly Dictionary<BotOwner, Hunter> _hunters = new Dictionary<BotOwner, Hunter>();
        private readonly Dictionary<BotsGroup, Squad> _squads = new Dictionary<BotsGroup, Squad>();
        private readonly List<BotOwner> _removedBots = new List<BotOwner>();
        private readonly List<BotsGroup> _removedGroups = new List<BotsGroup>();
        private readonly List<IPlayer> _targets = new List<IPlayer>();
        private readonly Dictionary<WildSpawnType, bool> _blackDivisionRoles = new Dictionary<WildSpawnType, bool>();
        private GameWorld _world;
        private HuntManager _huntManager;
        private bool _active, _faulted;
        private float _nextTick;
        internal static bool AppliesTo(BotOwner bot)
        {
            var controller = MonoBehaviourSingleton<ISBCameraHuntController>.Instance;
            return bot != null && controller != null && controller._active && controller._world != null && ReferenceEquals(controller._world, Singleton<GameWorld>.Instance) && controller._hunters.ContainsKey(bot) && !bot.IsDead && bot.GetManager()?.HasAssignedCheckpoint() != true;
        }

        internal void Activate()
        {
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance;
            if (world == null || game == null || !string.Equals(world.LocationId ?? RaidChangesUtil.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase))
                return;
            if (!(game is LocalGame) && !ISBFikaAuthority.IsAiAuthority)
                return;
            if (_active && ReferenceEquals(_world, world))
                return;
            ResetRaid();
            _world = world;
            _huntManager = MonoBehaviourSingleton<HuntManager>.Instance;
            if (_huntManager == null)
                return;
            _active = true;
            _nextTick = 0f;
            Update();
            if (_active)
                ISB.Compatibility.ISBEffectNotifications.Publish("camera_hunt", Plugin.LogSource);
        }

        public void ResetRaid()
        {
            _active = false;
            foreach (var hunter in _hunters.Values)
                RestoreHunter(hunter);
            foreach (var entry in _squads)
                RestoreGroup(entry.Key, entry.Value, false);
            _hunters.Clear();
            _squads.Clear();
            _targets.Clear();
            _removedBots.Clear();
            _removedGroups.Clear();
            _blackDivisionRoles.Clear();
            _world = null;
            _huntManager = null;
            _nextTick = 0f;
            _faulted = false;
        }

        private void Update()
        {
            if (!ISBFikaAuthority.IsAiAuthority || !_active || Time.time < _nextTick)
                return;
            _nextTick = Time.time + 1f;
            if (_world == null || !ReferenceEquals(_world, Singleton<GameWorld>.Instance))
            {
                ResetRaid();
                return;
            }

            try
            {
                RefreshParticipants();
                foreach (var entry in _squads)
                    UpdateTarget(entry.Key, entry.Value);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private void RefreshParticipants()
        {
            _targets.Clear();
            foreach (var squad in _squads.Values)
                squad.Members.Clear();
            var players = _world.AllAlivePlayersList;
            if (players == null)
                return;
            foreach (var person in players)
            {
                if (person?.HealthController?.IsAlive != true)
                    continue;
                var bot = person.AIData?.BotOwner;
                if (bot != null && EligibleHunter(bot))
                    Register(bot);
                else
                    _targets.Add(person);
            }

            _removedBots.Clear();
            foreach (var entry in _hunters)
            {
                var hunter = entry.Value;
                if (!EligibleHunter(hunter.Bot) || hunter.Manager == null || hunter.Bot.BotsGroup != hunter.Group)
                {
                    RestoreHunter(hunter);
                    _removedBots.Add(entry.Key);
                    continue;
                }

                _squads[hunter.Group].Members.Add(hunter.Manager);
            }

            foreach (var bot in _removedBots)
                _hunters.Remove(bot);
            _removedGroups.Clear();
            foreach (var entry in _squads)
                if (entry.Value.Members.Count == 0)
                {
                    RestoreGroup(entry.Key, entry.Value, true);
                    _removedGroups.Add(entry.Key);
                }

            foreach (var group in _removedGroups)
                _squads.Remove(group);
        }

        private static bool EligibleHunter(BotOwner bot)
        {
            if (bot == null || bot.IsDead || !bot.isActiveAndEnabled || bot.GetPlayer?.HealthController?.IsAlive != true || bot.BotsGroup == null || bot.Profile?.Info?.Settings == null || !WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role))
                return false;
            return bot.GetManager()?.HasAssignedCheckpoint() == false;
        }

        private void Register(BotOwner bot)
        {
            if (_hunters.ContainsKey(bot))
                return;
            var manager = bot.GetComponent<BotHuntManager>() ?? bot.gameObject.AddComponent<BotHuntManager>();
            var group = bot.BotsGroup;
            if (!_squads.ContainsKey(group))
            {
                var squad = new Squad();
                squad.HadOriginalTarget = _huntManager.huntGroups.TryGetValue(group, out squad.OriginalTarget);
                _squads.Add(group, squad);
            }

            var hunter = new Hunter
            {
                Bot = bot,
                Manager = manager,
                Group = group,
                OriginalActive = manager.active,
                OriginalIgnoreRegroup = manager.ignoreRegroup,
                OriginalTarget = manager.huntTarget,
                OriginalPriorities = manager.priorityTargets
            };
            _hunters.Add(bot, hunter);
            if (!manager.active || manager.botOwner != bot || manager.huntManager != _huntManager)
                manager.Init(bot, _huntManager);
            manager.priorityTargets = new List<IPlayer>(1);
            manager.shouldRegroup = manager.isRegrouping = manager.shouldSearch = false;
            bot.GetComponent<ISBTacticalAgent>()?.CancelMove();
        }

        private bool IsBlackDivision(WildSpawnType role)
        {
            if (!_blackDivisionRoles.TryGetValue(role, out var result))
            {
                result = (int)role == 848424 || (int)role == 848426 || ISBBlackDivisionUtility.IsBlackDivisionRole(role);
                _blackDivisionRoles.Add(role, result);
            }

            return result;
        }

        private int PriorityFor(BotOwner hunter, IPlayer target)
        {
            if (target?.HealthController?.IsAlive != true || target.Profile?.Info?.Settings == null || ReferenceEquals(target, hunter.GetPlayer) || target.AIData?.BotOwner?.BotsGroup == hunter.BotsGroup)
                return -1;
            var role = target.Profile.Info.Settings.Role;
            if (target.IsAI && WildSpawnTypeExtensions.IsISB(role))
                return -1;
            bool protectedTarget = ISBAishiLoyaltyConditionState.ProtectsTarget(target);
            protectedTarget |= target.IsAI && ISBForcedHostilityState.Enabled && ISBForcedHostilityState.IsFriendlyToISB(role);
            protectedTarget |= hunter.BotsGroup.IsAlly(target);
            protectedTarget |= !hunter.BotsGroup.IsPlayerEnemy(target);
            var hunterRole = hunter.Profile.Info.Settings.Role;
            bool nativeTarget = _huntManager.validHuntRoles.TryGetValue(hunterRole, out var roles) && roles.Contains(role);
            nativeTarget |= _huntManager.validPMCHunts.TryGetValue(hunterRole, out var sides) && sides.Contains(target.Side);
            return ISBCameraHuntPolicy.TargetPriority(!target.IsAI, IsBlackDivision(role), nativeTarget, protectedTarget);
        }

        private void UpdateTarget(BotsGroup group, Squad squad)
        {
            if (squad.Members.Count == 0)
                return;
            var leader = squad.Members[0].botOwner;
            foreach (var member in squad.Members)
                if (member.botOwner.Boss?.IamBoss == true)
                {
                    leader = member.botOwner;
                    break;
                }

            IPlayer selected = null;
            int selectedPriority = -1;
            float selectedDistance = float.MaxValue;
            foreach (var target in _targets)
            {
                int priority = PriorityFor(leader, target);
                if (priority < 0)
                    continue;
                float distance = (leader.Position - target.Position).sqrMagnitude;
                if (!ISBCameraHuntPolicy.PreferCandidate(priority, distance, ReferenceEquals(target, squad.Target), selectedPriority, selectedDistance, ReferenceEquals(selected, squad.Target)))
                    continue;
                selected = target;
                selectedPriority = priority;
                selectedDistance = distance;
            }

            bool changed = !ReferenceEquals(selected, squad.Target);
            squad.Target = selected;
            if (selected == null)
                _huntManager.huntGroups.Remove(group);
            else
            {
                _huntManager.huntGroups[group] = selected;
                if (!group.Enemies.ContainsKey(selected))
                    group.AddEnemy(selected, EBotEnemyCause.addPlayer);
            }

            foreach (var manager in squad.Members)
            {
                if (manager == null || ISBRevengeHuntController.AppliesTo(manager.botOwner))
                    continue;
                bool targetChanged = changed || !ReferenceEquals(manager.huntTarget, selected);
                manager.huntTarget = selected;
                manager.priorityTargets.Clear();
                if (selected != null)
                    manager.priorityTargets.Add(selected);
                if (targetChanged)
                {
                    manager.shouldSearch = false;
                    if (selected != null)
                        manager.knownLocation = selected.Position;
                }

                PrepareRegroup(manager);
            }
        }

        internal static bool TryRetarget(BotHuntManager manager)
        {
            var controller = MonoBehaviourSingleton<ISBCameraHuntController>.Instance;
            if (manager == null || !AppliesTo(manager.botOwner))
                return false;
            try
            {
                if (controller._squads.TryGetValue(manager.botOwner.BotsGroup, out var squad))
                    controller.UpdateTarget(manager.botOwner.BotsGroup, squad);
            }
            catch (Exception ex)
            {
                controller.Fail(ex);
            }

            return true;
        }

        internal static bool TryGetFollowers(BotHuntManager manager, out List<BotHuntManager> followers)
        {
            followers = null;
            var controller = MonoBehaviourSingleton<ISBCameraHuntController>.Instance;
            if (manager == null || !AppliesTo(manager.botOwner) || !controller._squads.TryGetValue(manager.botOwner.BotsGroup, out var squad))
                return false;
            squad.Followers.Clear();
            foreach (var member in squad.Members)
                if (member != null && AppliesTo(member.botOwner))
                    squad.Followers.Add(member);
            followers = squad.Followers;
            return true;
        }

        internal static void PrepareRegroup(BotHuntManager manager)
        {
            if (manager == null || !AppliesTo(manager.botOwner))
                return;
            var bot = manager.botOwner;
            var leader = bot.BotFollower?.BossToFollow;
            bool canRegroup = bot.Boss?.IamBoss == true || (leader != null && leader.IsAlive && AppliesTo(leader.Player()?.AIData?.BotOwner));
            manager.ignoreRegroup = !canRegroup;
            if (!canRegroup)
                manager.shouldRegroup = manager.isRegrouping = false;
        }

        private static void RestoreHunter(Hunter hunter)
        {
            if (hunter.Manager == null)
                return;
            var manager = hunter.Manager;
            manager.active = hunter.OriginalActive;
            manager.ignoreRegroup = hunter.OriginalIgnoreRegroup;
            manager.huntTarget = hunter.OriginalTarget;
            manager.priorityTargets = hunter.OriginalPriorities;
            manager.shouldSearch = manager.shouldRegroup = manager.isRegrouping = false;
            if (hunter.OriginalTarget?.HealthController?.IsAlive == true)
                manager.knownLocation = hunter.OriginalTarget.Position;
        }

        private void RestoreGroup(BotsGroup group, Squad squad, bool restoreTarget)
        {
            if (_huntManager == null || !_huntManager.huntGroups.TryGetValue(group, out var current) || !ReferenceEquals(current, squad.Target))
                return;
            if (restoreTarget && squad.HadOriginalTarget && squad.OriginalTarget?.HealthController?.IsAlive == true)
                _huntManager.huntGroups[group] = squad.OriginalTarget;
            else
                _huntManager.huntGroups.Remove(group);
        }

        private void Fail(Exception ex)
        {
            if (!_faulted)
                Plugin.LogSource?.LogError("[ISB] Camera hunt stopped: " + ex.GetBaseException());
            ResetRaid();
            _faulted = true;
        }

        public override void OnDestroy()
        {
            ResetRaid();
            base.OnDestroy();
        }
    }
}
