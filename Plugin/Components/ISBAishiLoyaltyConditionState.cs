using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public class ISBAishiLoyaltyConditionResponse
    {
        public bool enabled { get; set; }
        public bool active { get; set; }
        public int loyaltyLevel { get; set; }
        public int minLoyaltyLevel { get; set; }
        public bool breakOnHit { get; set; }
        public bool logs { get; set; }
        public float damageThreshold { get; set; }
        public bool thresholdPerFaction { get; set; }
        public bool loseReputationOnKill { get; set; }
        public float reputationLostPerKill { get; set; }
        public bool showReputationLossInRaidResults { get; set; }
        public bool extendToPlayerAllies { get; set; }
    }

    public static class ISBAishiLoyaltyConditionState
    {
        private const int ISBCommanderEnum = 13707;
        private const int ISBFollowerCommanderEnum = 13708;

        public static bool Enabled;
        public static bool Active;
        public static bool Broken;
        public static bool BreakOnHit = true;
        public static bool Logs;
        public static float DamageThreshold = 300f;
        public static bool ThresholdPerFaction;
        public static bool LoseReputationOnKill;
        public static float ReputationLostPerKill;
        public static bool ShowReputationLossInRaidResults = true;
        public static bool ExtendToPlayerAllies;
        public static int LoyaltyLevel;
        public static string BetrayerProfileId { get; private set; }

        private static bool _hardDisabled;
        private static readonly HashSet<BotsGroup> AppliedHostilityGroups = new HashSet<BotsGroup>();

        public static bool Holding =>
            !_hardDisabled
            && Enabled
            && Active
            && !Broken
            && !ISBRevengeBridge.Active;

        public static void HardDisable()
        {
            _hardDisabled = true;
            Enabled = false;
            Active = false;
        }

        public static void InitRaid()
        {
            if (_hardDisabled)
                return;

            Enabled = false;
            Active = false;
            Broken = false;
            BreakOnHit = true;
            Logs = false;
            DamageThreshold = 300f;
            ThresholdPerFaction = false;
            LoseReputationOnKill = false;
            ReputationLostPerKill = 0f;
            ShowReputationLossInRaidResults = true;
            ExtendToPlayerAllies = false;
            LoyaltyLevel = 0;
            BetrayerProfileId = null;

            AppliedHostilityGroups.Clear();

            ISBAishiLoyaltyConditionDamageTracker.Reset();
            ISBReputationBridge.Reset();
            ISBReputationBridge.BeginRaid();

            _scavHostilityAnnounced = false;
            _lastCauseKey = null;
            _repeatedCauseCount = 0;

            var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;

            if (mainPlayer != null && mainPlayer.Side == EPlayerSide.Savage)
            {
                Plugin.LogSource.LogInfo("[ISB] Scav raid detected. The Aishi loyalty condition is a PMC arrangement; disabling module.");
                return;
            }

            try
            {
                var json = RequestHandler.GetJson("/ISB/aishiLoyaltyCondition");

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
                Active = response.active;
                BreakOnHit = response.breakOnHit;
                Logs = response.logs;
                DamageThreshold = response.damageThreshold;
                ThresholdPerFaction = response.thresholdPerFaction;
                LoseReputationOnKill = response.loseReputationOnKill;
                ReputationLostPerKill = response.reputationLostPerKill;
                ShowReputationLossInRaidResults = response.showReputationLossInRaidResults;
                ExtendToPlayerAllies = response.extendToPlayerAllies;
                LoyaltyLevel = response.loyaltyLevel;

                if (Holding)
                {
                    Plugin.LogSource.LogInfo($"[ISB] AishiLoyaltyCondition is holding. LL {LoyaltyLevel}/{response.minLoyaltyLevel}. ISB operators will ignore you unless provoked.");
                }
                else if (Enabled)
                {
                    Plugin.LogSource.LogInfo($"[ISB] AishiLoyaltyCondition inactive. LL {LoyaltyLevel}/{response.minLoyaltyLevel}.");
                }
            }
            catch (System.Exception ex)
            {
                Enabled = false;
                Active = false;
            }
        }

        private static string _lastCauseKey;
        private static int _repeatedCauseCount;

        public static void LogCause(EBotEnemyCause cause, WildSpawnType role, string decision)
        {
            if (!Logs)
                return;

            var key = $"{cause}|{role}|{decision}";

            if (key == _lastCauseKey)
            {
                _repeatedCauseCount++;
                return;
            }

            if (_repeatedCauseCount > 0)
            {
                _repeatedCauseCount = 0;
            }

            _lastCauseKey = key;

            Plugin.LogSource.LogInfo($"[ISB AISHI LOYALTY] AddEnemy cause={cause} ({(int)cause}) leader={role} ({(int)role}) -> {decision}");
        }

        public static void LogDamage(float damage, float botTotal, float factionTotal, float threshold, bool reached)
        {
            if (!Logs)
                return;

            var scope = ThresholdPerFaction ? $"faction {factionTotal:F1}" : $"bot {botTotal:F1}";

            Plugin.LogSource.LogInfo($"[ISB AISHI LOYALTY] Damage {damage:F1} in a ISB Operator. Cumulative {scope} / {threshold:F0}. {(reached ? "MAX REACHED" : "LOWER FROM MAX")}");
        }

        private static bool _scavHostilityAnnounced;

        public static void AnnounceScavHostility()
        {
            if (_scavHostilityAnnounced)
                return;

            _scavHostilityAnnounced = true;

            Plugin.LogSource.LogInfo("[ISB] Player scav detected. The Aishi arrangement covers PMCs only.");
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

            var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;

            if (!IsEligiblePlayer(mainPlayer))
                return false;

            return group.IsAlly(mainPlayer);
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
            if (!Holding)
                return false;

            if (IsCommander(role))
                return false;

            return WildSpawnTypeExtensions.IsISB(role);
        }

        public static bool IsBetrayer(IPlayer person)
        {
            return Broken
                && person != null
                && !person.IsAI
                && !string.IsNullOrEmpty(BetrayerProfileId)
                && string.Equals(person.ProfileId, BetrayerProfileId, StringComparison.Ordinal);
        }

        public static bool ShouldForceHostility(BotsGroup group, IPlayer person)
        {
            if (!IsBetrayer(person) || group?.InitialBot?.Profile?.Info?.Settings == null)
                return false;

            return WildSpawnTypeExtensions.IsISB(group.InitialBot.Profile.Info.Settings.Role);
        }

        public static void Break(
            WildSpawnType victimRole,
            string aggressorProfileId,
            bool registerRevenge)
        {
            if (!Holding)
                return;

            Broken = true;
            BetrayerProfileId = aggressorProfileId;

            var appliedGroups = 0;

            if (ISBFikaAuthority.IsAiAuthority)
            {
                var aggressor = FindBetrayer();
                appliedGroups = ApplyCurrentRaidHostility(aggressor);
            }

            Plugin.LogSource.LogInfo(
                $"[ISB] Aishi loyalty condition broken. {appliedGroups} active ISB group(s) were made hostile by the AI authority. " +
                "Each BotsGroup receives AddEnemy only once for the remainder of the raid.");

            if (registerRevenge)
                ISBRevengeBridge.RegisterRevenge(victimRole, aggressorProfileId);
        }

        public static bool ApplyHostilityToGroupOnce(BotsGroup group)
        {
            if (!ISBFikaAuthority.IsAiAuthority)
                return false;

            if (!Broken || group == null || AppliedHostilityGroups.Contains(group))
                return false;

            return ApplyHostilityToGroupOnce(group, FindBetrayer());
        }

        private static bool ApplyHostilityToGroupOnce(BotsGroup group, IPlayer aggressor)
        {
            if (!Broken || group == null || aggressor == null)
                return false;

            if (!ShouldForceHostility(group, aggressor))
                return false;

            if (!AppliedHostilityGroups.Add(group))
                return false;

            try
            {
                RemoveNeutralReference(group, aggressor);
                group.AddEnemy(aggressor, EBotEnemyCause.addPlayer);
                return true;
            }
            catch (Exception ex)
            {
                AppliedHostilityGroups.Remove(group);
                Plugin.LogSource.LogWarning(
                    $"[ISB AISHI LOYALTY] Could not make an ISB group hostile to the betrayer: {ex.Message}");
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

        private static IPlayer FindBetrayer()
        {
            if (string.IsNullOrEmpty(BetrayerProfileId))
                return null;

            var gameWorld = Singleton<GameWorld>.Instance;
            var players = gameWorld?.AllAlivePlayersList;

            if (players != null)
            {
                foreach (var player in players)
                {
                    if (player == null || player.IsAI)
                        continue;

                    if (string.Equals(player.ProfileId, BetrayerProfileId, StringComparison.Ordinal))
                        return player;
                }
            }

            var mainPlayer = gameWorld?.MainPlayer;
            return IsBetrayer(mainPlayer) ? mainPlayer : null;
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
