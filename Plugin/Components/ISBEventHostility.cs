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
        private static readonly HashSet<string> ChaosProfiles = new HashSet<string>(System.StringComparer.Ordinal);
        private static System.Runtime.CompilerServices.ConditionalWeakTable<BotSpawnParams, BotsGroup> ChaosGroups = new System.Runtime.CompilerServices.ConditionalWeakTable<BotSpawnParams, BotsGroup>();

        internal static void RememberVariants(IEnumerable<Profile> profiles, string variant)
        {
            bool chaos = string.Equals(variant, GhostBotVariant.ChaosName, System.StringComparison.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                if (string.IsNullOrEmpty(profile?.Id)) continue;
                if (chaos) ChaosProfiles.Add(profile.Id);
                else ChaosProfiles.Remove(profile.Id);
            }
        }

        internal static void ClearVariants()
        {
            ChaosProfiles.Clear();
            ChaosGroups = new System.Runtime.CompilerServices.ConditionalWeakTable<BotSpawnParams, BotsGroup>();
        }
        internal static bool IsChaos(string profileId) => profileId != null && ChaosProfiles.Contains(profileId);

        internal static bool SelectChaosGroup(BotSpawner __instance, BotOwner bot, BotZone zone, ref BotsGroup __result)
        {
            if (bot?.Profile == null || !IsChaos(bot.Profile.Id)) return true;
            var spawn = bot.SpawnProfileData?.SpawnParams;
            if (spawn != null && ChaosGroups.TryGetValue(spawn, out var existing))
            {
                __instance.SetBotAsEnemy(bot);
                __result = existing;
                return false;
            }
            var enemies = new List<BotOwner>();
            foreach (var enemy in __instance.GetBotEnemiesList(bot))
                if (enemy != null && !IsChaos(enemy.Profile?.Id)) enemies.Add(enemy);
            __instance.SetBotAsEnemy(bot);
            var group = new BotsGroup(zone, __instance.BotGame, bot, enemies, __instance._deadBodiesController, __instance._allPlayers, true);
            if (spawn?.ShallBeGroup != null) group.TargetMembersCount = spawn.ShallBeGroup.StartCount;
            __instance.Groups.AddNoKey(group, zone);
            if (spawn != null) ChaosGroups.Add(spawn, group);
            __result = group;
            return false;
        }

        internal static void ChaosAlliance(BotsGroup __instance, IPlayer player, ref bool __result)
        {
            if (TryGetChaosRelation(__instance, player, out bool enemy)) __result = !enemy;
        }

        internal static bool TryGetChaosRelation(BotsGroup group, IPlayer target, out bool enemy)
        {
            enemy = false;
            var source = group?._initialBot;
            if (source?.Profile == null || target == null) return false;
            bool sourceChaos = IsChaos(source.Profile.Id), targetChaos = IsChaos(target.ProfileId);
            if (!sourceChaos && !targetChaos) return false;
            enemy = source.Profile.Id != target.ProfileId && sourceChaos != targetChaos;
            return true;
        }

        internal static bool IsBlackDivisionGroup(BotsGroup group) =>
            BlackDivisionEventCheckpoints.Installed &&
            group?._initialBot?.Profile?.Info?.Settings != null &&
            BlackDivisionCheckpointPolicy.IsRole((int)group._initialBot.Profile.Info.Settings.Role);

        internal static bool IsBlackDivisionEnemy(BotsGroup group, IPlayer target) =>
            IsBlackDivisionGroup(group) && target != null && target.ProfileId != group._initialBot.ProfileId &&
            (!target.IsAI || target.AIData?.BotOwner?.BotsGroup != group &&
                (target.Profile?.Info?.Settings == null || !BlackDivisionCheckpointPolicy.IsRole((int)target.Profile.Info.Settings.Role)));

        internal static bool TryGetBotRelation(BotsGroup group, IPlayer target, out bool enemy)
        {
            if (TryGetChaosRelation(group, target, out enemy)) return true;
            enemy = false;
            var source = group?._initialBot;
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

        private static bool Relevant(BotsGroup group) => group?._initialBot?.Profile?.Info?.Settings != null &&
            (IsChaos(group._initialBot.Profile.Id) || WildSpawnTypeExtensions.IsISB(group._initialBot.Profile.Info.Settings.Role) || IsBlackDivisionGroup(group));

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
            if (TryGetChaosRelation(group, target, out bool chaosEnemy) && !chaosEnemy)
            {
                IPlayer friendly = null;
                foreach (var existing in group.Enemies.Keys)
                    if (existing.ProfileId == target.ProfileId) { friendly = existing; break; }
                if (friendly != null) group.RemoveEnemy(friendly, EBotEnemyCause.initial);
                return;
            }
            if (group?._initialBot == null || target?.HealthController?.IsAlive != true || target.ProfileId == group._initialBot.ProfileId ||
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
            for (int i = 0; i < group.MembersCount; i++)
            {
                var member = group.Member(i);
                if (member?.EnemiesController?.EnemyInfos != null && member.EnemiesController.EnemyInfos.TryGetValue(Behavior.ISB.ISBThreatMemory.Identity(member, target), out var enemy))
                    enemy.SetIgnoreState(false);
            }
        }
    }
}
