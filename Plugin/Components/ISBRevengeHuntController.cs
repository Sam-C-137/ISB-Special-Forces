using Comfort.Common;
using EFT;
using ISBSpecialForces.Behavior.BlackDivision;
using MoreBotsAPI.Components;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    internal sealed class ISBRevengeHuntController : MonoBehaviourSingleton<ISBRevengeHuntController>
    {
        private sealed class Hunter
        {
            internal BotHuntManager Manager;
            internal IPlayer Target;
            internal List<IPlayer> Priorities;
            internal bool Active, IgnoreRegroup;
        }

        private readonly Dictionary<BotOwner, Hunter> _hunters = new Dictionary<BotOwner, Hunter>();
        private readonly List<BotOwner> _remove = new List<BotOwner>();
        private readonly List<IPlayer> _targets = new List<IPlayer>();
        private GameWorld _world;
        private float _next;
        private bool _faulted;
        internal static bool AppliesTo(BotOwner bot)
        {
            var c = Instance;
            return bot != null && c != null && ReferenceEquals(c._world, Singleton<GameWorld>.Instance) && c._hunters.ContainsKey(bot) && !bot.IsDead;
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            if (!ReferenceEquals(world, _world))
            {
                ResetRaid();
                _world = world;
            }

            if (_world == null || _faulted || !ISBFikaAuthority.IsAiAuthority || Time.time < _next)
                return;
            _next = Time.time + 0.5f;
            try
            {
                var hunt = MonoBehaviourSingleton<HuntManager>.Instance;
                if (hunt == null || _world.AllAlivePlayersList == null)
                    return;
                _targets.Clear();
                foreach (var player in _world.AllAlivePlayersList)
                    if (ISBSpawnProgressBridge.IsTarget(player) && player.HealthController?.IsAlive == true)
                        _targets.Add(player);
                _remove.Clear();
                foreach (var entry in _hunters)
                    if (_targets.Count == 0 || entry.Key == null || entry.Key.IsDead || !entry.Key.isActiveAndEnabled)
                        _remove.Add(entry.Key);
                foreach (var bot in _remove)
                {
                    Restore(_hunters[bot]);
                    _hunters.Remove(bot);
                }

                if (_targets.Count == 0)
                    return;
                foreach (var player in _world.AllAlivePlayersList)
                {
                    var bot = player?.AIData?.BotOwner;
                    if (bot?.Profile?.Info?.Settings == null || bot.IsDead || !bot.isActiveAndEnabled || bot.BotsGroup == null || !WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role))
                        continue;
                    if (!_hunters.TryGetValue(bot, out var saved))
                    {
                        var manager = bot.GetComponent<BotHuntManager>() ?? bot.gameObject.AddComponent<BotHuntManager>();
                        saved = new Hunter
                        {
                            Manager = manager,
                            Active = manager.active,
                            IgnoreRegroup = manager.ignoreRegroup,
                            Target = manager.huntTarget,
                            Priorities = manager.priorityTargets
                        };
                        _hunters.Add(bot, saved);
                        if (!manager.active || manager.botOwner != bot || manager.huntManager != hunt)
                            manager.Init(bot, hunt);
                        manager.priorityTargets = new List<IPlayer>();
                        bot.GetComponent<ISBTacticalAgent>()?.CancelMove();
                    }

                    Target(bot, saved.Manager);
                }
            }
            catch (Exception ex)
            {
                ResetRaid();
                _world = world;
                _faulted = true;
                Plugin.LogSource.LogError("ISB RevengeHunt stopped: " + ex);
            }
        }

        private void Target(BotOwner bot, BotHuntManager manager)
        {
            IPlayer target = null;
            float distance = float.MaxValue;
            foreach (var player in _targets)
            {
                float d = (bot.Position - player.Position).sqrMagnitude;
                if (d < distance)
                {
                    target = player;
                    distance = d;
                }
            }

            if (target == null)
                return;
            if (!bot.BotsGroup.IsPlayerEnemy(target))
                bot.BotsGroup.AddEnemy(target, EBotEnemyCause.addPlayer);
            manager.active = true;
            manager.huntTarget = target;
            manager.knownLocation = target.Position;
            manager.priorityTargets.Clear();
            manager.priorityTargets.Add(target);
            manager.ignoreRegroup = true;
            manager.shouldSearch = manager.shouldRegroup = manager.isRegrouping = false;
        }

        internal static bool TryRetarget(BotHuntManager manager)
        {
            if (manager == null || !AppliesTo(manager.botOwner))
                return false;
            Instance.Target(manager.botOwner, manager);
            return true;
        }

        private static void Restore(Hunter saved)
        {
            var m = saved.Manager;
            if (m == null)
                return;
            m.active = saved.Active;
            m.ignoreRegroup = saved.IgnoreRegroup;
            m.huntTarget = saved.Target;
            m.priorityTargets = saved.Priorities;
            m.shouldSearch = m.shouldRegroup = m.isRegrouping = false;
            if (saved.Target?.HealthController?.IsAlive == true)
                m.knownLocation = saved.Target.Position;
        }

        public void ResetRaid()
        {
            foreach (var saved in _hunters.Values)
                Restore(saved);
            _hunters.Clear();
            _targets.Clear();
            _remove.Clear();
            _world = null;
            _next = 0;
            _faulted = false;
        }

        public override void OnDestroy()
        {
            ResetRaid();
            base.OnDestroy();
        }
    }
}
