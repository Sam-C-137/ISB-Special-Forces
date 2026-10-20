using System;
using Comfort.Common;
using EFT;
using ISB.Compatibility;
using ISBSpecialForces.Controllers;
using MoreBotsAPI.Components;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    internal sealed class BlackDivisionEventHunt : MonoBehaviour
    {
        private float _nextTick;
        private static bool EventActive(GameWorld world) => world != null && string.Equals(world.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase) && ISBFikaAuthority.IsAiAuthority && BlackDivisionEventCheckpoints.Installed && MonoBehaviourSingleton<ISBCheckpointManager>.Instance?.BlackDivisionContainmentActive == true;
        private static bool Eligible(BotHuntManager manager)
        {
            var bot = manager?.botOwner;
            return manager != null && manager.active && manager.huntManager != null && bot != null && bot.GetPlayer != null && !bot.IsDead && bot.isActiveAndEnabled && bot.BotsGroup != null && bot.Profile?.Info?.Settings != null && BlackDivisionCheckpointPolicy.IsRole((int)bot.Profile.Info.Settings.Role) && bot.GetManager()?.HasAssignedCheckpoint() != true;
        }

        private static bool IsTarget(IPlayer target, BotOwner hunter) => target?.IsAI == true && target.HealthController?.IsAlive == true && target.Profile?.Info?.Settings != null && WildSpawnTypeExtensions.IsISB(target.Profile.Info.Settings.Role) && target.AIData?.BotOwner?.BotsGroup != hunter.BotsGroup && hunter.BotsGroup.IsPlayerEnemy(target);
        internal static bool TryRetarget(BotHuntManager manager)
        {
            var world = Singleton<GameWorld>.Instance;
            if (!EventActive(world) || !Eligible(manager))
                return false;
            var bot = manager.botOwner;
            IPlayer selected = null;
            if (manager.huntManager.huntGroups.TryGetValue(bot.BotsGroup, out var shared) && IsTarget(shared, bot))
                selected = shared;
            if (selected == null && IsTarget(manager.huntTarget, bot))
                selected = manager.huntTarget;
            if (selected == null)
            {
                float distance = float.MaxValue;
                foreach (var candidate in world.AllAlivePlayersList)
                {
                    if (!IsTarget(candidate, bot))
                        continue;
                    float candidateDistance = (candidate.Position - bot.Position).sqrMagnitude;
                    if (candidateDistance >= distance)
                        continue;
                    selected = candidate;
                    distance = candidateDistance;
                }
            }

            if (selected == null)
                return false;
            if (!ReferenceEquals(manager.huntTarget, selected))
            {
                manager.huntTarget = selected;
                manager.knownLocation = selected.Position;
                manager.shouldSearch = false;
            }

            manager.huntManager.huntGroups[bot.BotsGroup] = selected;
            if (!bot.BotsGroup.IsPlayerEnemy(selected))
                bot.BotsGroup.AddEnemy(selected, EBotEnemyCause.addPlayer);
            return true;
        }

        private void Update()
        {
            if (Time.time < _nextTick)
                return;
            _nextTick = Time.time + 2f;
            var world = Singleton<GameWorld>.Instance;
            ISBEventHostility.Refresh(world);
            if (!EventActive(world) || world.AllAlivePlayersList == null)
                return;
            foreach (var player in world.AllAlivePlayersList)
            {
                var bot = player?.AIData?.BotOwner;
                if (bot == null || bot.GetPlayer == null || bot.GetPlayer.Profile?.Info?.Settings == null || !BlackDivisionCheckpointPolicy.IsRole((int)bot.Profile.Info.Settings.Role))
                    continue;
                var manager = bot.GetComponent<BotHuntManager>();
                if (!Eligible(manager) || TryRetarget(manager))
                    continue;
                if (manager.huntTarget?.HealthController?.IsAlive == false)
                    manager.huntManager.FindNewHuntTarget(manager);
            }
        }
    }
}
