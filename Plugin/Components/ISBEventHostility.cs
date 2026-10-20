using System.Collections.Generic;
using Comfort.Common;
using EFT;
using ISB.Compatibility;

namespace ISBSpecialForces.Components
{
    internal static class ISBEventHostility
    {
        private static readonly HashSet<BotsGroup> Groups = new HashSet<BotsGroup>();
        private static readonly List<IPlayer> ManagedTargets = new List<IPlayer>();

        internal static bool IsBlackDivisionGroup(BotsGroup group) =>
            BlackDivisionEventCheckpoints.Installed &&
            group?.InitialBot?.Profile?.Info?.Settings != null &&
            BlackDivisionCheckpointPolicy.IsRole((int)group.InitialBot.Profile.Info.Settings.Role);

        internal static bool IsBlackDivisionEnemy(BotsGroup group, IPlayer target) =>
            IsBlackDivisionGroup(group) && target != null && target.ProfileId != group.InitialBot.ProfileId &&
            (!target.IsAI || target.AIData?.BotOwner?.BotsGroup != group &&
                (target.Profile?.Info?.Settings == null || !BlackDivisionCheckpointPolicy.IsRole((int)target.Profile.Info.Settings.Role)));

        internal static bool TryGetBotRelation(BotsGroup group, IPlayer target, out bool enemy)
        {
            enemy = false;
            var source = group?.InitialBot;
            if (source?.Profile?.Info?.Settings == null || target?.IsAI != true || target.Profile?.Info?.Settings == null)
                return false;
            var sourceRole = source.Profile.Info.Settings.Role;
            var targetRole = target.Profile.Info.Settings.Role;
            var sourceBlackDivision = BlackDivisionEventCheckpoints.Installed && BlackDivisionCheckpointPolicy.IsRole((int)sourceRole);
            var targetBlackDivision = BlackDivisionEventCheckpoints.Installed && BlackDivisionCheckpointPolicy.IsRole((int)targetRole);
            var sourceISB = WildSpawnTypeExtensions.IsISB(sourceRole);
            var targetISB = WildSpawnTypeExtensions.IsISB(targetRole);
            if (!sourceBlackDivision && !targetBlackDivision && (!ISBForcedHostilityState.Enabled || !sourceISB && !targetISB))
                return false;
            var targetOwner = target.AIData?.BotOwner;
            if (target.ProfileId == source.ProfileId || targetOwner?.BotsGroup == group)
                return true;
            if (sourceBlackDivision || targetBlackDivision)
            {
                enemy = sourceBlackDivision != targetBlackDivision;
                return true;
            }
            var isb = sourceISB ? source : targetOwner;
            if (isb?.Settings == null)
                return false;
            var otherRole = sourceISB ? targetRole : sourceRole;
            if (ISBForcedHostilityState.IsFriendlyToISB(otherRole) ||
                isb.Settings.GetAlwaysFriendlyBotTypes().Contains(otherRole))
                return true;
            var otherGroup = sourceISB ? targetOwner?.BotsGroup : group;
            if (!ISBAishiLoyaltyConditionState.IsCommander(isb.Profile.Info.Settings.Role) &&
                ISBAishiLoyaltyConditionState.IsProtectedAllyGroup(otherGroup))
                return true;
            if (isb.Settings.GetFriendlyBotTypes().Contains(otherRole) || isb.Settings.GetWarnBotTypes().Contains(otherRole))
                return false;
            enemy = true;
            return true;
        }

        internal static bool IsForcedEnemy(BotsGroup group, IPlayer target)
        {
            if (TryGetBotRelation(group, target, out var enemy)) return enemy;
            return IsBlackDivisionEnemy(group, target);
        }

        private static bool Relevant(BotsGroup group) => group?.InitialBot?.Profile?.Info?.Settings != null &&
            (WildSpawnTypeExtensions.IsISB(group.InitialBot.Profile.Info.Settings.Role) || IsBlackDivisionGroup(group));

        internal static void Refresh(GameWorld world)
        {
            if (!ISBFikaAuthority.IsAiAuthority || world?.AllAlivePlayersList == null) return;
            Groups.Clear();
            ManagedTargets.Clear();
            try
            {
                foreach (var player in world.AllAlivePlayersList)
                {
                    var bot = player?.AIData?.BotOwner;
                    if (bot == null || bot.IsDead || bot.BotsGroup == null) continue;
                    Groups.Add(bot.BotsGroup);
                    if (Relevant(bot.BotsGroup)) ManagedTargets.Add(player);
                }
                foreach (var group in Groups)
                {
                    IEnumerable<IPlayer> targets = Relevant(group) ? world.AllAlivePlayersList : ManagedTargets;
                    foreach (var target in targets) Register(group, target);
                }
            }
            finally
            {
                Groups.Clear();
                ManagedTargets.Clear();
            }
        }

        internal static void OnBotActivated(BotOwner bot)
        {
            var world = Singleton<GameWorld>.Instance;
            if (!ISBFikaAuthority.IsAiAuthority || bot?.BotsGroup == null || world?.AllAlivePlayersList == null) return;
            var managed = Relevant(bot.BotsGroup);
            foreach (var player in world.AllAlivePlayersList)
            {
                var group = player?.AIData?.BotOwner?.BotsGroup;
                if (group == bot.BotsGroup || !managed && !Relevant(group)) continue;
                Register(bot.BotsGroup, player);
                if (group != null) Register(group, bot.GetPlayer);
            }
        }

        private static void Register(BotsGroup group, IPlayer target)
        {
            if (group?.InitialBot == null || target?.HealthController?.IsAlive != true || target.ProfileId == group.InitialBot.ProfileId ||
                target.IsAI && target.AIData?.BotOwner?.BotsGroup == group ||
                !target.IsAI && target.IsYourPlayer && !ISBFikaAuthority.CanPresentLocally ||
                !group.IsPlayerEnemy(target)) return;
            IPlayer registered = null;
            foreach (var existing in group.Enemies.Keys)
                if (existing.ProfileId == target.ProfileId)
                {
                    registered = existing;
                    break;
                }
            target = registered ?? (target.IsAI ? (IPlayer)target.AIData?.BotOwner ?? target : target);
            if (registered == null && !group.AddEnemy(target, EBotEnemyCause.addPlayer)) return;
            group.Neutrals.Remove(target);
            group.Allies.Remove(target);
            var owner = target.IsAI ? target.AIData?.BotOwner : null;
            if (owner != null)
            {
                group.Neutrals.Remove(owner);
                group.Allies.Remove(owner);
                if (owner.GetPlayer != null)
                {
                    group.Neutrals.Remove(owner.GetPlayer);
                    group.Allies.Remove(owner.GetPlayer);
                }
            }
            foreach (var member in group.Members)
            {
                if (member?.EnemiesController?.EnemyInfos != null && member.EnemiesController.EnemyInfos.TryGetValue(Behavior.ISB.ISBThreatMemory.Identity(member, target), out var enemy))
                    enemy.SetIgnoreState(false);
            }
        }
    }
}
