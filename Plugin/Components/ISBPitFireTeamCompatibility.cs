using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using ISBSpecialForces.Patches;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBPitFireTeamCompatibility : MonoBehaviour
    {
        public const string PitFireTeamGuid = "xyz.pit.fireteam";

        private const float ReconcileInterval = 0.75f;
        private const string PitFireGroupTypeName = "pitTeam.Patches.BotsGroupPlayer";
        private const string PitFireBossPlayersTypeName = "pitTeam.Modules.BossPlayers";

        private static bool _initialized;
        private static bool _installed;
        private static MethodInfo _isFollowerProfileIdMethod;
        private static MethodInfo _getSainByBotOwnerMethod;
        private static MethodInfo _getSainByProfileMethod;
        private static Type _sainEnableType;

        private float _nextReconcileTime;
        private bool _wasHolding;

        public static bool Installed => _installed;

        public static bool Initialize()
        {
            if (_initialized)
                return _installed;

            _initialized = true;

            if (!Chainloader.PluginInfos.TryGetValue(PitFireTeamGuid, out var pluginInfo) || pluginInfo?.Instance == null)
                return false;

            _installed = true;

            try
            {
                var pitFireAssembly = pluginInfo.Instance.GetType().Assembly;
                var bossPlayersType = pitFireAssembly.GetType(PitFireBossPlayersTypeName, false);
                _isFollowerProfileIdMethod = bossPlayersType?.GetMethod(
                    "IsFollowerProfileId",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);

                ISBPitFireTeamDynamicPatcher.Enable(pitFireAssembly);
                Plugin.LogSource.LogInfo("[ISB SOF] PitFireTeam detected. Aishi LL4 alliance compatibility enabled (targeted safe mode).");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[ISB SOF] PitFireTeam compatibility initialized with fallback detection: {ex.Message}");
            }

            return true;
        }

        public static bool IsPitFireFollower(IPlayer player)
        {
            if (!_installed || player == null || !player.IsAI || string.IsNullOrEmpty(player.ProfileId))
                return false;

            try
            {
                if (_isFollowerProfileIdMethod != null &&
                    _isFollowerProfileIdMethod.Invoke(null, new object[] { player.ProfileId }) is bool isFollower)
                {
                    return isFollower;
                }
            }
            catch
            {
            }

            var group = player.AIData?.BotOwner?.BotsGroup;
            if (group == null || !string.Equals(group.GetType().FullName, PitFireGroupTypeName, StringComparison.Ordinal))
                return false;

            var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
            return mainPlayer != null && group.IsAlly(mainPlayer);
        }

        public static bool IsPitFireGroup(BotsGroup group)
        {
            if (!_installed || group == null)
                return false;

            if (string.Equals(group.GetType().FullName, PitFireGroupTypeName, StringComparison.Ordinal))
                return true;

            for (var i = 0; i < group.MembersCount; i++)
            {
                var member = group.Member(i);
                if (member?.GetPlayer != null && IsPitFireFollower(member.GetPlayer))
                    return true;
            }

            return false;
        }

        public static bool ShouldBlockGroupEnemy(BotsGroup group, IPlayer target)
        {
            if (!CanProtect() || group == null || target == null)
                return false;

            var initialBot = group.InitialBot;
            if (initialBot?.GetPlayer != null &&
                ISBAishiLoyaltyConditionState.IsProtectedISBBot(initialBot.GetPlayer) &&
                IsPitFireFollower(target))
            {
                return true;
            }

            return IsPitFireGroup(group) && ISBAishiLoyaltyConditionState.IsProtectedISBBot(target);
        }

        public static bool ShouldBlockOwnerEnemy(BotOwner owner, IPlayer target)
        {
            if (!CanProtect() || owner?.GetPlayer == null || target == null)
                return false;

            if (ISBAishiLoyaltyConditionState.IsProtectedISBBot(owner.GetPlayer) && IsPitFireFollower(target))
                return true;

            return IsPitFireFollower(owner.GetPlayer) && ISBAishiLoyaltyConditionState.IsProtectedISBBot(target);
        }

        public static BotOwner FindOwnerForMemory(object memory)
        {
            if (memory == null)
                return null;

            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return null;

            foreach (var player in players)
            {
                var owner = player?.AIData?.BotOwner;
                if (owner != null && ReferenceEquals(owner.Memory, memory))
                    return owner;
            }

            return null;
        }

        public static void SyncBot(BotOwner bot)
        {
            if (!CanProtect() || bot?.GetPlayer == null)
                return;

            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;

            if (ISBAishiLoyaltyConditionState.IsProtectedISBBot(bot.GetPlayer))
            {
                foreach (var player in players)
                {
                    if (IsPitFireFollower(player))
                        MakeFriendly(bot, player.AIData?.BotOwner);
                }
            }
            else if (IsPitFireFollower(bot.GetPlayer))
            {
                foreach (var player in players)
                {
                    if (ISBAishiLoyaltyConditionState.IsProtectedISBBot(player))
                        MakeFriendly(bot, player.AIData?.BotOwner);
                }
            }
        }

        private void Update()
        {
            if (!_installed || Time.time < _nextReconcileTime)
                return;

            _nextReconcileTime = Time.time + ReconcileInterval;

            var holding = CanProtect();
            if (holding)
            {
                ReconcileAll();
            }
            else if (_wasHolding && (ISBAishiLoyaltyConditionState.Broken || ISBRevengeBridge.Active))
            {
                ForceHostilityAll();
            }

            _wasHolding = holding;
        }

        private static bool CanProtect()
        {
            return _installed &&
                   ISBAishiLoyaltyConditionState.Holding &&
                   ISBAishiLoyaltyConditionState.ExtendToPlayerAllies;
        }

        private static void ReconcileAll()
        {
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;

            var followers = new List<BotOwner>();
            var isbBots = new List<BotOwner>();

            foreach (var player in players)
            {
                if (player == null || !player.IsAI || player.HealthController == null || !player.HealthController.IsAlive)
                    continue;

                var owner = player.AIData?.BotOwner;
                if (owner == null)
                    continue;

                if (IsPitFireFollower(player))
                    followers.Add(owner);
                else if (ISBAishiLoyaltyConditionState.IsProtectedISBBot(player))
                    isbBots.Add(owner);
            }

            foreach (var follower in followers)
            {
                foreach (var isbBot in isbBots)
                    MakeFriendly(follower, isbBot);
            }
        }

        private static void MakeFriendly(BotOwner first, BotOwner second)
        {
            if (first?.GetPlayer == null || second?.GetPlayer == null || first.ProfileId == second.ProfileId)
                return;

            RemoveHostility(first, second);
            RemoveHostility(second, first);
        }

        private static void RemoveHostility(BotOwner owner, BotOwner friendlyBot)
        {
            if (owner == null || friendlyBot?.GetPlayer == null)
                return;

            var friendlyPlayer = friendlyBot.GetPlayer;

            try
            {
                var group = owner.BotsGroup;
                if (group != null)
                {
                    group.RemoveEnemy(friendlyBot);

                    if (group.Neutrals == null || !group.Neutrals.ContainsKey(friendlyBot))
                        group.AddNeutral(friendlyBot);

                    if (!group.IsAlly(friendlyPlayer))
                        group.AddAlly(friendlyPlayer);
                }
            }
            catch
            {
            }

            try
            {
                owner.Memory?.DeleteInfoAboutEnemy(friendlyBot);

                if (owner.Memory?.GoalEnemy?.ProfileId == friendlyBot.ProfileId)
                    owner.Memory.GoalEnemy = null;
            }
            catch
            {
            }

            try
            {
                var enemyInfos = owner.EnemiesController?.EnemyInfos as IDictionary;
                enemyInfos?.Remove(friendlyPlayer);
            }
            catch
            {
            }

            ClearSainEnemy(owner, friendlyBot.ProfileId);
        }

        private static void ForceHostilityAll()
        {
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;

            var followers = new List<BotOwner>();
            var isbBots = new List<BotOwner>();

            foreach (var player in players)
            {
                if (player == null || !player.IsAI || player.HealthController == null || !player.HealthController.IsAlive)
                    continue;

                var owner = player.AIData?.BotOwner;
                if (owner == null)
                    continue;

                if (IsPitFireFollower(player))
                    followers.Add(owner);
                else if (ISBAishiLoyaltyConditionState.IsProtectedISBBot(player))
                    isbBots.Add(owner);
            }

            foreach (var follower in followers)
            {
                foreach (var isbBot in isbBots)
                {
                    MakeHostile(follower, isbBot);
                    MakeHostile(isbBot, follower);
                }
            }
        }

        private static void MakeHostile(BotOwner owner, BotOwner enemyBot)
        {
            if (owner?.BotsGroup == null || enemyBot?.GetPlayer == null)
                return;

            try
            {
                owner.BotsGroup.RemoveAlly(enemyBot);
                owner.BotsGroup.Neutrals?.Remove(enemyBot);
                owner.BotsGroup.AddEnemy(enemyBot, EBotEnemyCause.addPlayer);
            }
            catch
            {
            }
        }

        private static void ClearSainEnemy(BotOwner owner, string enemyProfileId)
        {
            if (owner == null || string.IsNullOrEmpty(enemyProfileId))
                return;

            try
            {
                _sainEnableType ??=
                    AccessTools.TypeByName("SAIN.SAINEnableClass") ??
                    AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");

                if (_sainEnableType == null)
                    return;

                if (_getSainByBotOwnerMethod == null && _getSainByProfileMethod == null)
                {
                    foreach (var method in _sainEnableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (method.Name != "GetSAIN")
                            continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                            _getSainByBotOwnerMethod = method;
                        else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].IsOut)
                            _getSainByProfileMethod = method;
                    }
                }

                object sainBot = null;

                if (_getSainByBotOwnerMethod != null)
                {
                    sainBot = _getSainByBotOwnerMethod.Invoke(null, new object[] { owner });
                }
                else if (_getSainByProfileMethod != null && !string.IsNullOrEmpty(owner.ProfileId))
                {
                    var args = new object[] { owner.ProfileId, null };
                    if (_getSainByProfileMethod.Invoke(null, args) is bool found && found)
                        sainBot = args[1];
                }

                if (sainBot == null)
                    return;

                var enemyController =
                    AccessTools.Property(sainBot.GetType(), "EnemyController")?.GetValue(sainBot) ??
                    AccessTools.Field(sainBot.GetType(), "EnemyController")?.GetValue(sainBot);

                if (enemyController == null)
                    return;

                var controllerType = enemyController.GetType();
                controllerType.GetMethod("RemoveEnemy", new[] { typeof(string) })?
                    .Invoke(enemyController, new object[] { enemyProfileId });

                var goalEnemy =
                    AccessTools.Property(controllerType, "GoalEnemy")?.GetValue(enemyController) ??
                    AccessTools.Field(controllerType, "_goalEnemy")?.GetValue(enemyController);

                var goalEnemyId = goalEnemy == null
                    ? null
                    : AccessTools.Property(goalEnemy.GetType(), "EnemyProfileId")?.GetValue(goalEnemy) as string;

                if (string.Equals(goalEnemyId, enemyProfileId, StringComparison.Ordinal))
                    controllerType.GetMethod("ClearEnemy", Type.EmptyTypes)?.Invoke(enemyController, Array.Empty<object>());
            }
            catch
            {
            }
        }
    }
}
