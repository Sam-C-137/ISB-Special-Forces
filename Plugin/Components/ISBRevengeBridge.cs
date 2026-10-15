using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ISBSpecialForces.Components
{
    public sealed class ISBRevengeStatusResponse
    {
        public bool enabled { get; set; }
        public bool active { get; set; }
        public int remainingRaids { get; set; }
        public bool isScavRaid { get; set; }
    }

    public static class ISBRevengeBridge
    {
        private static int _generation;
        private static Task _pending = Task.CompletedTask;
        private static readonly HashSet<string> KilledBots = new HashSet<string>();
        public static bool Enabled { get; private set; }
        public static bool Active { get; private set; }
        public static int RemainingRaids { get; private set; }

        private static GameWorld _initializedWorld;
        public static void EnsureRaidInitialized()
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world?.MainPlayer;
            if (world == null || world is HideoutGameWorld || player == null || !player.IsYourPlayer || player.IsAI || ReferenceEquals(_initializedWorld, world))
                return;
            InitializeLocalRaid(world);
        }

        public static void InitRaid()
        {
            ResetRaid();
            _initializedWorld = null;
            EnsureRaidInitialized();
        }

        private static void InitializeLocalRaid(GameWorld world)
        {
            _initializedWorld = world;
            try
            {
                var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
                var isScavRaid = mainPlayer != null && mainPlayer.Side == EPlayerSide.Savage;
                var startRequest = JsonConvert.SerializeObject(new { isScavRaid, locationId = SPT.SinglePlayer.Utils.InRaid.RaidChangesUtil.LocationId ?? string.Empty });
                RequestHandler.PostJson("/ISB/revenge/raid/start", startRequest);
                RefreshStatus();
            }
            catch (System.Exception ex)
            {
                ResetRaid();
                Plugin.LogSource.LogError($"[ISB REVENGE] Failed to initialize raid state: {ex.Message}");
            }
        }

        public static void RegisterRevenge(WildSpawnType victimRole, string aggressorProfileId)
        {
            if (string.IsNullOrEmpty(aggressorProfileId))
            {
                Plugin.LogSource.LogWarning("[ISB REVENGE] Aggressor profile id is empty.");
                return;
            }

            if (!WildSpawnTypeExtensions.IsISB(victimRole))
                return;
            var request = JsonConvert.SerializeObject(new { reason = "aishi_loyalty_broken", victimRole = (int)victimRole });
            QueueRequest("/ISB/revenge/register", request);
        }

        public static void RegisterKill(WildSpawnType victimRole, string victimId)
        {
            if (!Enabled || string.IsNullOrEmpty(victimId) || !KilledBots.Add(victimId))
                return;
            var request = JsonConvert.SerializeObject(new { victimRole = (int)victimRole, victimId });
            QueueRequest("/ISB/revenge/kill", request);
        }

        private static void QueueRequest(string route, string request)
        {
            var previous = _pending;
            var generation = _generation;
            _pending = RegisterRevengeAsync(previous, generation, route, request);
        }

        private static async Task RegisterRevengeAsync(Task previous, int generation, string route, string request)
        {
            try
            {
                await previous.ConfigureAwait(false);
                if (generation != _generation)
                    return;
                await RequestHandler.PostJsonAsync(route, request).ConfigureAwait(false);
                var json = await RequestHandler.GetJsonAsync("/ISB/revenge/status").ConfigureAwait(false);
                if (string.IsNullOrEmpty(json))
                    return;
                var response = JsonConvert.DeserializeObject<ISBRevengeStatusResponse>(json);
                if (response == null)
                    return;
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    ApplyStatus(response);
                    if (Enabled && RemainingRaids > 0)
                    {
                        Plugin.LogSource.LogInfo($"[ISB REVENGE] Revenge registered by the ISB. Future hostile raids remaining: {RemainingRaids}.");
                    }
                });
            }
            catch (Exception ex)
            {
                ISBAishiReputationWorker.Enqueue(() => Plugin.LogSource.LogError($"[ISB REVENGE] Failed to register revenge: {ex.Message}"));
            }
        }

        public static bool HasPendingRevenge(BotsGroup group, IPlayer person)
        {
            if (group?.InitialBot?.Profile?.Info?.Settings != null && ISBSpawnProgressBridge.IsTarget(person) && WildSpawnTypeExtensions.IsISB(group.InitialBot.Profile.Info.Settings.Role))
                return true;
            if (!Enabled || !Active || group == null || person == null)
            {
                return false;
            }

            if (!ISBAishiLoyaltyConditionState.IsEligiblePlayer(person))
            {
                return false;
            }

            var initialBot = group.InitialBot;
            if (initialBot?.Profile?.Info?.Settings == null)
            {
                return false;
            }

            return WildSpawnTypeExtensions.IsISB(initialBot.Profile.Info.Settings.Role);
        }

        public static void ResetRaid()
        {
            _generation++;
            KilledBots.Clear();
            Enabled = false;
            Active = false;
            RemainingRaids = 0;
        }

        private static void RefreshStatus()
        {
            var json = RequestHandler.GetJson("/ISB/revenge/status");
            if (string.IsNullOrEmpty(json))
            {
                ResetRaid();
                return;
            }

            var response = JsonConvert.DeserializeObject<ISBRevengeStatusResponse>(json);
            if (response == null)
            {
                ResetRaid();
                return;
            }

            ApplyStatus(response);
        }

        private static void ApplyStatus(ISBRevengeStatusResponse response)
        {
            Enabled = response.enabled;
            Active = response.active;
            RemainingRaids = response.remainingRaids;
        }
    }
}
