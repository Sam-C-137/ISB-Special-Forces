using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBAishiParticipantState
    {
        public string profileId { get; set; }
        public string token { get; set; }
        public bool eligible { get; set; }
        public bool broken { get; set; }
        public ISBAishiLoyaltyConditionResponse response { get; set; }
        public ISBAishiBetrayalResponse penalty { get; set; }

        internal readonly HashSet<BotsGroup> HostileGroups = new HashSet<BotsGroup>();
        internal readonly HashSet<string> ReportedKills = new HashSet<string>(StringComparer.Ordinal);
        internal bool BreakReported;
    }

    public sealed class ISBAishiParticipantBridge : MonoBehaviour
    {
        private static readonly Dictionary<string, ISBAishiParticipantState> States = new Dictionary<string, ISBAishiParticipantState>(StringComparer.Ordinal);
        private static GameWorld _world;
        private static int _generation;
        private static bool _busy, _errorReported;
        private static float _nextPoll;
        private static Task _reports = Task.CompletedTask;
        private readonly List<string> _profiles = new List<string>(8);
        internal static void Reset(GameWorld world)
        {
            States.Clear();
            _world = world;
            _generation++;
            _busy = false;
            _nextPoll = 0;
            _errorReported = false;
            _reports = Task.CompletedTask;
        }

        internal static void SetLocal(string id, bool eligible, ISBAishiLoyaltyConditionResponse response) => Accept(new ISBAishiParticipantState { profileId = id, eligible = eligible, response = response });
        internal static bool Accept(ISBAishiParticipantState state)
        {
            if (state == null || string.IsNullOrEmpty(state.profileId) || state.response == null)
                return false;
            ISBAishiLoyaltyConditionState.BreakOnHit = state.response.breakOnHit;
            ISBAishiLoyaltyConditionState.DamageThreshold = state.response.damageThreshold;
            ISBAishiLoyaltyConditionState.ThresholdPerFaction = state.response.thresholdPerFaction;
            ISBAishiLoyaltyConditionState.ExtendToPlayerAllies = state.response.extendToPlayerAllies;
            if (States.TryGetValue(state.profileId, out var old) && (string.IsNullOrEmpty(old.token) || old.token == state.token))
            {
                bool broken = old.broken || state.broken || state.response.revengeActive;
                bool changed = old.token != state.token || old.eligible != state.eligible || old.broken != broken || old.response.enabled != state.response.enabled || old.response.extendToPlayerAllies != state.response.extendToPlayerAllies;
                old.token = state.token;
                old.eligible = state.eligible;
                old.response = state.response;
                old.broken = broken;
                return changed;
            }

            States[state.profileId] = state;
            return true;
        }

        internal static bool Holding(string id) => !string.IsNullOrEmpty(id) && States.TryGetValue(id, out var state) && state.eligible && !state.broken && state.response?.enabled == true && !state.response.revengeActive;
        internal static bool IsBroken(string id) => !string.IsNullOrEmpty(id) && States.TryGetValue(id, out var state) && state.broken;
        internal static bool Break(string id)
        {
            if (!Holding(id))
                return false;
            States[id].broken = true;
            return true;
        }

        internal static bool MarkHostile(string id, BotsGroup group) => States.TryGetValue(id, out var state) && state.HostileGroups.Add(group);
        internal static void UnmarkHostile(string id, BotsGroup group)
        {
            if (States.TryGetValue(id, out var state))
                state.HostileGroups.Remove(group);
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world is HideoutGameWorld)
                world = null;
            if (!ReferenceEquals(_world, world))
                Reset(world);
            if (world == null || ISBAishiLoyaltyConditionState.HardDisabled || Time.unscaledTime < _nextPoll)
                return;
            ISBRevengeBridge.EnsureRaidInitialized();
            ISBAishiLoyaltyConditionState.EnsureRaidInitialized();
            _nextPoll = Time.unscaledTime + 5f;
            if (_busy || !Chainloader.PluginInfos.ContainsKey("com.fika.core"))
                return;
            _profiles.Clear();
            var players = world.AllAlivePlayersList;
            if (players != null)
                foreach (var player in players)
                    if (ISBAishiLoyaltyConditionState.IsEligibleAggressor(player) && !string.IsNullOrEmpty(player.ProfileId) && _profiles.Count < 64 && !_profiles.Contains(player.ProfileId))
                        _profiles.Add(player.ProfileId);
            if (_profiles.Count == 0)
                return;
            _busy = true;
            var request = new JObject
            {
                ["profileIds"] = new JArray(_profiles),
                ["locationId"] = world.LocationId ?? SPT.SinglePlayer.Utils.InRaid.RaidChangesUtil.LocationId ?? string.Empty
            }.ToString(Formatting.None);
            _ = Fetch(_generation, request);
        }

        private static async Task Fetch(int generation, string request)
        {
            try
            {
                var bytes = await RequestHandler.HttpClient.PostAsync("/ISB/aishi/participants", Encoding.UTF8.GetBytes(request)).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(bytes);
                var states = JsonConvert.DeserializeObject<List<ISBAishiParticipantState>>(json);
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    _busy = false;
                    if (states == null)
                        return;
                    bool changed = false;
                    foreach (var state in states)
                    {
                        changed |= Accept(state);
                        var local = _world?.MainPlayer;
                        if (local != null && local.IsYourPlayer && !local.IsAI && local.ProfileId == state?.profileId)
                            ISBReputationBridge.SynchronizePenalty(state.penalty);
                    }

                    if (!changed || !ISBFikaAuthority.IsAiAuthority)
                        return;
                    var players = _world?.AllAlivePlayersList;
                    if (players != null)
                        foreach (var player in players)
                        {
                            var bot = player?.AIData?.BotOwner;
                            if (bot?.Profile?.Info?.Settings != null && WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role))
                                Patches.BotOwnerActivatePatch.ApplyAishiLoyaltyCondition(bot);
                        }
                });
            }
            catch (Exception ex)
            {
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    _busy = false;
                    if (_errorReported)
                        return;
                    _errorReported = true;
                    Plugin.LogSource?.LogError("ISB could not synchronize participant loyalty: " + ex.GetType().Name);
                });
            }
        }

        internal static void Report(string id, WildSpawnType role, string victimId)
        {
            if (!ISBFikaAuthority.IsAiAuthority || !States.TryGetValue(id, out var state) || string.IsNullOrEmpty(state.token))
                return;
            if (string.IsNullOrEmpty(victimId))
            {
                if (state.BreakReported)
                    return;
                state.BreakReported = true;
            }
            else if (!state.ReportedKills.Add(victimId))
                return;
            var request = JsonConvert.SerializeObject(new { profileId = id, token = state.token, victimRole = (int)role, victimId });
            _reports = SendReport(_reports, _generation, request);
        }

        private static async Task SendReport(Task previous, int generation, string request)
        {
            await previous.ConfigureAwait(false);
            for (int attempt = 0; attempt < 3 && generation == _generation; attempt++)
            {
                try
                {
                    await RequestHandler.PostJsonAsync("/ISB/aishi/participant/report", request).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt < 2)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                        continue;
                    }

                    ISBAishiReputationWorker.Enqueue(() =>
                    {
                        if (generation == _generation)
                            Plugin.LogSource?.LogError("ISB could not record participant betrayal: " + ex.Message);
                    });
                }
            }
        }

        private void OnDestroy() => Reset(null);
    }
}
