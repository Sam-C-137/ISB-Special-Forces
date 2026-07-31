using Comfort.Common;
using EFT;
using MoreBotsAPI.Components;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    internal static class ISBHuntTargetPriority
    {
        private static readonly HashSet<int> BlackDivisionRoles = new HashSet<int>
        {
            848420,
            848421,
            848422,
            848423
        };

        public static void PrepareManager(BotHuntManager manager)
        {
            var hunter = manager?.botOwner?.GetPlayer;
            if (!IsISBHunter(hunter))
                return;

            var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
            if (mainPlayer != null)
                manager.priorityTargets?.Remove(mainPlayer);
        }

        public static void EnforcePlayerLast(BotHuntManager manager)
        {
            var hunter = manager?.botOwner?.GetPlayer;
            if (!IsISBHunter(hunter))
                return;

            var gameWorld = Singleton<GameWorld>.Instance;
            var mainPlayer = gameWorld?.MainPlayer;
            if (mainPlayer == null)
                return;

            manager.priorityTargets?.Remove(mainPlayer);

            if (manager.huntTarget != mainPlayer)
                return;

            var replacement = FindNonPlayerTarget(gameWorld, hunter);
            if (replacement == null)
                return;

            manager.huntTarget = replacement;

            if (ISBSpecialForces.Behavior.Orders.State.ISBOrderSettings.Logs)
            {
                Plugin.LogSource.LogInfo(
                    $"[ISB HUNT PRIORITY] '{hunter.Profile?.Nickname}' deferred the player and selected " +
                    $"'{replacement.Profile?.Nickname}' ({replacement.Profile?.Info?.Settings?.Role}) first.");
            }
        }

        private static Player FindNonPlayerTarget(GameWorld gameWorld, Player hunter)
        {
            var candidates = gameWorld.AllAlivePlayersList
                .Where(player => player != null
                    && player != hunter
                    && player != gameWorld.MainPlayer
                    && player.IsAI
                    && player.HealthController != null
                    && player.HealthController.IsAlive)
                .ToList();

            var blackDivision = candidates
                .Where(player => BlackDivisionRoles.Contains((int)player.Profile.Info.Settings.Role))
                .OrderBy(player => HorizontalDistance(hunter.Position, player.Position))
                .FirstOrDefault();

            if (blackDivision != null)
                return blackDivision;

            return candidates
                .Where(player =>
                {
                    var role = player.Profile.Info.Settings.Role;
                    return role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR;
                })
                .OrderBy(player => HorizontalDistance(hunter.Position, player.Position))
                .FirstOrDefault();
        }

        private static bool IsISBHunter(Player hunter)
        {
            return hunter != null
                && hunter.Profile?.Info?.Settings != null
                && WildSpawnTypeExtensions.IsISB(hunter.Profile.Info.Settings.Role);
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }
    }
}
