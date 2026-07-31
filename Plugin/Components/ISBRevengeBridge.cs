using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
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
        public static bool Enabled { get; private set; }
        public static bool Active { get; private set; }
        public static int RemainingRaids { get; private set; }

        public static void InitRaid()
        {
            ResetRaid();

            try
            {
                var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
                var isScavRaid = mainPlayer != null && mainPlayer.Side == EPlayerSide.Savage;
                var startRequest = JsonConvert.SerializeObject(new
                {
                    isScavRaid
                });

                RequestHandler.PostJson("/ISB/revenge/raid/start", startRequest);
                RefreshStatus(true);
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

            var request = JsonConvert.SerializeObject(new
            {
                reason = "aishi_loyalty_broken",
                victimRole = (int)victimRole
            });

            _ = RegisterRevengeAsync(request);
        }

        private static async Task RegisterRevengeAsync(string request)
        {
            try
            {
                await RequestHandler.PostJsonAsync("/ISB/revenge/register", request)
                    .ConfigureAwait(false);

                var json = await RequestHandler.GetJsonAsync("/ISB/revenge/status")
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(json))
                    return;

                var response = JsonConvert.DeserializeObject<ISBRevengeStatusResponse>(json);
                if (response == null)
                    return;

                ISBAishiReputationWorker.Enqueue(() =>
                {
                    ApplyStatus(response, false);

                    if (Enabled && RemainingRaids > 0)
                    {
                        Plugin.LogSource.LogInfo(
                            $"[ISB REVENGE] Revenge registered by the ISB. Future hostile raids remaining: {RemainingRaids}.");
                    }
                });
            }
            catch (Exception ex)
            {
                ISBAishiReputationWorker.Enqueue(() =>
                    Plugin.LogSource.LogError($"[ISB REVENGE] Failed to register revenge: {ex.Message}"));
            }
        }

        public static bool HasPendingRevenge(BotsGroup group, IPlayer person)
        {
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
            Enabled = false;
            Active = false;
            RemainingRaids = 0;
        }

        private static void RefreshStatus(bool logStatus)
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

            ApplyStatus(response, logStatus);
        }

        private static void ApplyStatus(ISBRevengeStatusResponse response, bool logStatus)
        {
            Enabled = response.enabled;
            Active = response.active;
            RemainingRaids = response.remainingRaids;

            if (!logStatus || !Enabled)
                return;

            if (Active)
            {
                Plugin.LogSource.LogInfo(
                    $"[ISB REVENGE] Active for this raid. ISB operators remember your actions. Remaining hostile raids: {RemainingRaids}.");
            }
            else if (RemainingRaids > 0 && response.isScavRaid)
            {
                Plugin.LogSource.LogInfo(
                    $"[ISB REVENGE] Pending but inactive during this Scav raid. Remaining hostile PMC raids: {RemainingRaids}.");
            }
        }
    }
}
