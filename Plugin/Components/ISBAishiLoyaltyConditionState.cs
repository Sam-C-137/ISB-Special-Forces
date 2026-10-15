using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.SinglePlayer.Utils.InRaid;
using System;
using System.Collections;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public class ISBAishiLoyaltyConditionResponse
    {
        public bool enabled { get; set; }
        public bool active { get; set; }
        public string requiredItemTpl { get; set; }
        public bool breakOnHit { get; set; }
        public bool logs { get; set; }
        public float damageThreshold { get; set; }
        public bool thresholdPerFaction { get; set; }
        public bool loseReputationOnKill { get; set; }
        public float reputationLostPerKill { get; set; }
        public bool showReputationLossInRaidResults { get; set; }
        public bool extendToPlayerAllies { get; set; }
        public string friendlyConditionMode { get; set; }
        public bool itemConditionConfigured { get; set; }
        public bool questTruceConfigured { get; set; }
        public bool questTruceActive { get; set; }
        public bool revengeActive { get; set; }
        public List<string> activeQuestTruceRuleIds { get; set; }
    }

    public static class ISBAishiLoyaltyConditionState
    {
        private const int ISBCommanderEnum = 13707;
        private const int ISBFollowerCommanderEnum = 13708;
        public static bool Enabled;
        public static bool Logs;
        public static bool Active;
        public static bool Broken;
        public static bool EligibleForKillPenalty { get; private set; }

        public static bool BreakOnHit = true;
        public static float DamageThreshold = 300f;
        public static bool ThresholdPerFaction;
        public static bool LoseReputationOnKill;
        public static float ReputationLostPerKill;
        public static bool ShowReputationLossInRaidResults = true;
        public static bool ExtendToPlayerAllies;
        public static string RequiredItemTpl { get; private set; }
        public static bool HadRequiredItemAtRaidStart { get; private set; }
        public static string FriendlyConditionMode { get; private set; } = "any";
        public static bool ItemConditionConfigured { get; private set; }
        public static bool QuestTruceConfigured { get; private set; }
        public static bool QuestTruceActive { get; private set; }
        public static string BetrayerProfileId { get; private set; }

        private static bool _hardDisabled;
        internal static bool HardDisabled => _hardDisabled;

        private static GameWorld _initializedWorld;
        public static void EnsureRaidInitialized()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null || world is HideoutGameWorld || (world.MainPlayer == null || !world.MainPlayer.IsYourPlayer || world.MainPlayer.IsAI) || ReferenceEquals(_initializedWorld, world))
                return;
            InitRaid();
        }

        public static bool Holding => !_hardDisabled && Enabled && Active && !Broken && !ISBRevengeBridge.Active;

        public static void HardDisable()
        {
            _hardDisabled = true;
            ISBAishiParticipantBridge.Reset(null);
            Enabled = false;
            Logs = false;
            Active = false;
            EligibleForKillPenalty = false;
        }

        public static void InitRaid()
        {
            if (_hardDisabled)
                return;
            Enabled = false;
            Logs = false;
            Active = false;
            EligibleForKillPenalty = false;
            Broken = false;
            _lastDecision = null;
            _scavHostilityAnnounced = false;
            BreakOnHit = true;
            DamageThreshold = 300f;
            ThresholdPerFaction = false;
            LoseReputationOnKill = false;
            ReputationLostPerKill = 0f;
            ShowReputationLossInRaidResults = true;
            ExtendToPlayerAllies = false;
            RequiredItemTpl = string.Empty;
            HadRequiredItemAtRaidStart = false;
            FriendlyConditionMode = "any";
            ItemConditionConfigured = false;
            QuestTruceConfigured = false;
            QuestTruceActive = false;
            BetrayerProfileId = null;
            ISBAishiParticipantBridge.Reset(world: Singleton<GameWorld>.Instance);
            ISBAishiLoyaltyConditionDamageTracker.Reset();
            ISBReputationBridge.Reset();
            var world = Singleton<GameWorld>.Instance;
            _initializedWorld = null;
            var mainPlayer = world?.MainPlayer;
            if (mainPlayer == null || !mainPlayer.IsYourPlayer || mainPlayer.IsAI || world is HideoutGameWorld)
            {
                if (Plugin.DiagnosticsEnabled)
                    Plugin.LogSource.LogInfo("[ISB friendly] Waiting for the raid player before checking the radio.");
                return;
            }

            _initializedWorld = world;
            var isScavRaid = mainPlayer != null && mainPlayer.Side == EPlayerSide.Savage;
            var locationId = RaidChangesUtil.LocationId ?? string.Empty;
            if (isScavRaid)
            {
                if (Plugin.DiagnosticsEnabled)
                    Plugin.LogSource.LogInfo("[ISB] Scav raid detected. The Aishi loyalty condition is a PMC arrangement; disabling module.");
                ISBReputationBridge.BeginRaid(false, true, locationId);
                return;
            }

            bool raidEligibilityInitialized = false;
            try
            {
                var json = RequestHandler.GetJson("/ISB/aishiLoyaltyCondition/" + Uri.EscapeDataString(locationId));
                if (string.IsNullOrEmpty(json))
                {
                    return;
                }

                var response = JsonConvert.DeserializeObject<ISBAishiLoyaltyConditionResponse>(json);
                if (response == null)
                {
                    return;
                }

                Enabled = response.enabled;
                Logs = response.logs;
                BreakOnHit = response.breakOnHit;
                DamageThreshold = response.damageThreshold;
                ThresholdPerFaction = response.thresholdPerFaction;
                LoseReputationOnKill = response.loseReputationOnKill;
                ReputationLostPerKill = response.reputationLostPerKill;
                ShowReputationLossInRaidResults = response.showReputationLossInRaidResults;
                ExtendToPlayerAllies = response.extendToPlayerAllies;
                RequiredItemTpl = (response.requiredItemTpl ?? string.Empty).Trim();
                FriendlyConditionMode = NormalizeFriendlyMode(response.friendlyConditionMode);
                ItemConditionConfigured = response.itemConditionConfigured || !string.IsNullOrWhiteSpace(RequiredItemTpl);
                QuestTruceConfigured = response.questTruceConfigured;
                QuestTruceActive = response.questTruceActive;
                HadRequiredItemAtRaidStart = Enabled && ItemConditionConfigured && PlayerCarriesRequiredItem(mainPlayer, RequiredItemTpl);
                Active = Enabled && !response.revengeActive && EvaluateFriendlyConditions(FriendlyConditionMode, ItemConditionConfigured, HadRequiredItemAtRaidStart, QuestTruceConfigured, QuestTruceActive);
                EligibleForKillPenalty = Active;
                ISBAishiParticipantBridge.SetLocal(mainPlayer.ProfileId, Active, response);
                ISBReputationBridge.BeginRaid(HadRequiredItemAtRaidStart, false, locationId);
                raidEligibilityInitialized = true;
                if (Logs)
                    Plugin.LogSource.LogInfo("[ISB friendly] Map=" + locationId + "; active=" + Active + "; item=" + HadRequiredItemAtRaidStart + "; quest=" + QuestTruceActive + "; revenge=" + response.revengeActive + "; mode=" + FriendlyConditionMode + ".");
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource.LogWarning("[ISB friendly] Could not initialize raid conditions: " + ex.Message);
                Enabled = false;
                Logs = false;
                Active = false;
                EligibleForKillPenalty = false;
                HadRequiredItemAtRaidStart = false;
                QuestTruceActive = false;
            }
            finally
            {
                if (!raidEligibilityInitialized)
                    ISBReputationBridge.BeginRaid(false, false, locationId);
            }
        }

        private static bool EvaluateFriendlyConditions(string mode, bool itemConfigured, bool itemActive, bool questConfigured, bool questActive)
        {
            int sourceCount = (itemConfigured ? 1 : 0) + (questConfigured ? 1 : 0);
            if (sourceCount == 0)
                return false;
            if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (itemConfigured && !itemActive)
                    return false;
                if (questConfigured && !questActive)
                    return false;
                return true;
            }

            return itemActive || questActive;
        }

        private static string NormalizeFriendlyMode(string mode)
        {
            return string.Equals(mode?.Trim(), "all", StringComparison.OrdinalIgnoreCase) ? "all" : "any";
        }

        private static bool PlayerCarriesRequiredItem(IPlayer player, string requiredItemTpl)
        {
            return InventoryContainsRequiredItem(player?.Profile?.Inventory?.Equipment, requiredItemTpl);
        }

        private static bool InventoryContainsRequiredItem(Item root, string requiredItemTpl)
        {
            if (root == null || string.IsNullOrWhiteSpace(requiredItemTpl))
                return false;
            var pending = new Stack<Item>();
            var visited = new HashSet<Item>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (item == null || !visited.Add(item))
                    continue;
                if (string.Equals(item.TemplateId.ToString(), requiredItemTpl, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!(item is CompoundItem container))
                    continue;
                if (container.Grids != null)
                    foreach (var grid in container.Grids)
                        if (grid != null)
                            foreach (var child in grid.Items)
                                if (child != null)
                                    pending.Push(child);
                if (container.Slots != null)
                    foreach (var slot in container.Slots)
                        if (slot?.ContainedItem != null)
                            pending.Push(slot.ContainedItem);
            }

            return false;
        }

        private static EBotEnemyCause _lastCause;
        private static WildSpawnType _lastRole;
        private static string _lastDecision;
        private static bool _scavHostilityAnnounced;
        public static void LogCause(EBotEnemyCause cause, WildSpawnType role, string decision)
        {
            if (!Logs)
                return;
            if (cause == _lastCause && role == _lastRole && decision == _lastDecision)
                return;
            _lastCause = cause;
            _lastRole = role;
            _lastDecision = decision;
            Plugin.LogSource.LogInfo($"[ISB AISHI LOYALTY] AddEnemy cause={cause} ({(int)cause}) leader={role} ({(int)role}) -> {decision}");
        }

        public static void LogDamage(float damage, float botTotal, float factionTotal, float threshold, bool reached)
        {
            if (!Logs)
                return;
            var scope = ThresholdPerFaction ? $"faction {factionTotal:F1}" : $"bot {botTotal:F1}";
            Plugin.LogSource.LogInfo($"[ISB AISHI LOYALTY] Damage {damage:F1} in a ISB Operator. Cumulative {scope} / {threshold:F0}. {(reached ? "MAX REACHED" : "LOWER FROM MAX")}");
        }

        public static void AnnounceScavHostility()
        {
            if (_scavHostilityAnnounced)
                return;
            _scavHostilityAnnounced = true;
            Plugin.LogSource.LogInfo("[ISB] Player scav detected. The Aishi arrangement covers PMCs only.");
        }

        public static bool ProtectsHuman(IPlayer player) => !_hardDisabled && IsEligibleAggressor(player) && ISBAishiParticipantBridge.Holding(player.ProfileId) && !(player.IsYourPlayer && ISBRevengeBridge.Active) && !ISBSpawnProgressBridge.IsTarget(player);
        public static bool ProtectsTarget(IPlayer player) => ProtectsHuman(player) || IsPlayerAllyBot(player);
        public static bool IsProtectedAllyGroup(BotsGroup group)
        {
            if (!ExtendToPlayerAllies || group == null)
                return false;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players != null)
                foreach (var player in players)
                    if (ProtectsHuman(player) && group.IsAlly(player))
                        return true;
            return false;
        }

        public static bool IsPlayerAllyBot(IPlayer person)
        {
            if (!ExtendToPlayerAllies)
                return false;
            if (person == null || !person.IsAI)
                return false;
            var group = person.AIData?.BotOwner?.BotsGroup;
            if (group == null)
                return false;
            return IsProtectedAllyGroup(group);
        }

        public static bool IsProtectedISBBot(IPlayer person)
        {
            if (person == null || !person.IsAI)
                return false;
            var role = person.Profile?.Info?.Settings?.Role;
            if (role == null)
                return false;
            if (!WildSpawnTypeExtensions.IsISB(role.Value))
                return false;
            return !IsCommander(role.Value);
        }

        public static bool IsEligiblePlayer(IPlayer person)
        {
            if (!IsEligibleAggressor(person) || !person.IsYourPlayer)
                return false;
            return true;
        }

        public static bool IsEligibleAggressor(IPlayer person)
        {
            if (person == null || person.IsAI)
                return false;
            return person.Side != EPlayerSide.Savage;
        }

        public static bool IsCommander(WildSpawnType role)
        {
            return role == (WildSpawnType)ISBCommanderEnum || role == (WildSpawnType)ISBFollowerCommanderEnum;
        }

        public static bool IsProtectedFrom(WildSpawnType role)
        {
            if (IsCommander(role))
                return false;
            return WildSpawnTypeExtensions.IsISB(role);
        }

        public static bool IsBetrayer(IPlayer person)
        {
            return IsEligibleAggressor(person) && ISBAishiParticipantBridge.IsBroken(person.ProfileId);
        }

        public static bool ShouldForceHostility(BotsGroup group, IPlayer person)
        {
            if (!IsBetrayer(person) || group?.InitialBot?.Profile?.Info?.Settings == null)
                return false;
            return WildSpawnTypeExtensions.IsISB(group.InitialBot.Profile.Info.Settings.Role);
        }

        public static void Break(WildSpawnType victimRole, string aggressorProfileId, bool registerRevenge)
        {
            if (!ISBAishiParticipantBridge.Break(aggressorProfileId))
                return;
            var aggressor = FindPlayer(aggressorProfileId);
            if (IsEligiblePlayer(aggressor))
            {
                Broken = true;
                BetrayerProfileId = aggressorProfileId;
            }

            var appliedGroups = 0;
            if (ISBFikaAuthority.IsAiAuthority)
            {
                appliedGroups = ApplyCurrentRaidHostility(aggressor);
            }

            if (Logs)
                Plugin.LogSource.LogInfo($"[ISB] Aishi loyalty condition broken. {appliedGroups} active ISB group(s) were made hostile by the AI authority. " + "Each BotsGroup receives AddEnemy only once for the remainder of the raid.");
            if (registerRevenge)
                ISBRevengeBridge.RegisterRevenge(victimRole, aggressorProfileId);
            else if (ISBFikaAuthority.IsAiAuthority)
                ISBAishiParticipantBridge.Report(aggressorProfileId, victimRole, null);
        }

        public static bool ApplyHostilityToGroupOnce(BotsGroup group)
        {
            if (!ISBFikaAuthority.IsAiAuthority)
                return false;
            if (group == null)
                return false;
            bool applied = false;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players != null)
                foreach (var player in players)
                    if (IsBetrayer(player))
                        applied |= ApplyHostilityToGroupOnce(group, player);
            return applied;
        }

        private static bool ApplyHostilityToGroupOnce(BotsGroup group, IPlayer aggressor)
        {
            if (group == null || aggressor == null)
                return false;
            if (!ShouldForceHostility(group, aggressor))
                return false;
            if (!ISBAishiParticipantBridge.MarkHostile(aggressor.ProfileId, group))
                return false;
            try
            {
                RemoveNeutralReference(group, aggressor);
                group.AddEnemy(aggressor, EBotEnemyCause.addPlayer);
                return true;
            }
            catch (Exception ex)
            {
                ISBAishiParticipantBridge.UnmarkHostile(aggressor.ProfileId, group);
                Plugin.LogSource.LogError($"[ISB AISHI LOYALTY] Could not make an ISB group hostile to the betrayer: {ex.Message}");
                return false;
            }
        }

        private static int ApplyCurrentRaidHostility(IPlayer aggressor)
        {
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null || aggressor == null)
                return 0;
            var groups = new HashSet<BotsGroup>();
            foreach (var player in players)
            {
                if (player == null || !player.IsAI)
                    continue;
                if (player.HealthController == null || !player.HealthController.IsAlive)
                    continue;
                var role = player.Profile?.Info?.Settings?.Role;
                if (role == null || !WildSpawnTypeExtensions.IsISB(role.Value))
                    continue;
                var group = player.AIData?.BotOwner?.BotsGroup;
                if (group != null)
                    groups.Add(group);
            }

            var applied = 0;
            foreach (var group in groups)
            {
                if (ApplyHostilityToGroupOnce(group, aggressor))
                    applied++;
            }

            return applied;
        }

        private static IPlayer FindPlayer(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return null;
            var gameWorld = Singleton<GameWorld>.Instance;
            var players = gameWorld?.AllAlivePlayersList;
            if (players != null)
            {
                foreach (var player in players)
                {
                    if (player == null || player.IsAI)
                        continue;
                    if (string.Equals(player.ProfileId, profileId, StringComparison.Ordinal))
                        return player;
                }
            }

            var mainPlayer = gameWorld?.MainPlayer;
            return mainPlayer?.ProfileId == profileId ? mainPlayer : null;
        }

        private static void RemoveNeutralReference(BotsGroup group, IPlayer aggressor)
        {
            object neutralsObject = group.Neutrals;
            if (!(neutralsObject is IDictionary neutrals) || neutrals.Count == 0)
                return;
            var keys = new ArrayList(neutrals.Keys);
            foreach (var key in keys)
            {
                if (MatchesPlayerReference(key, aggressor))
                    neutrals.Remove(key);
            }
        }

        private static bool MatchesPlayerReference(object candidate, IPlayer aggressor)
        {
            if (candidate == null || aggressor == null)
                return false;
            if (ReferenceEquals(candidate, aggressor))
                return true;
            if (candidate is IPlayer player)
                return string.Equals(player.ProfileId, aggressor.ProfileId, StringComparison.Ordinal);
            if (candidate is BotOwner owner)
                return string.Equals(owner.ProfileId, aggressor.ProfileId, StringComparison.Ordinal);
            if (candidate is string profileId)
                return string.Equals(profileId, aggressor.ProfileId, StringComparison.Ordinal);
            return false;
        }
    }
}
