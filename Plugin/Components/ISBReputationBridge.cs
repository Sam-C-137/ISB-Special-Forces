using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Threading.Tasks;

namespace ISBSpecialForces.Components
{
    public sealed class ISBAishiBetrayalResponse
    {
        public bool enabled { get; set; }
        public bool applied { get; set; }
        public bool alreadyAppliedThisRaid { get; set; }
        public bool eligibleAtRaidStart { get; set; }
        public bool showInRaidResults { get; set; }
        public double standingBefore { get; set; }
        public double standingAfter { get; set; }
        public double standingDelta { get; set; }
        public string reason { get; set; }
    }

    public static class ISBReputationBridge
    {
        public const string AishiTraderId = "690766de550bc322a810ea1e";

        private static readonly object Sync = new object();
        private static bool _penaltyRequestedThisRaid;
        private static int _raidGeneration;

        public static void Reset()
        {
            lock (Sync)
            {
                _penaltyRequestedThisRaid = false;
                _raidGeneration++;
            }

            ISBAishiRaidResultState.Reset();
        }

        public static void BeginRaid()
        {
            try
            {
                RequestHandler.PostJson("/ISB/aishi/raid/start", "{}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB AISHI LOYALTY] Could not initialize the server-side raid penalty state: {ex.Message}");
            }
        }

        public static bool ApplyKillPenaltyOnce()
        {
            if (!ISBAishiLoyaltyConditionState.Holding
                || !ISBAishiLoyaltyConditionState.LoseReputationOnKill)
            {
                return false;
            }

            if (ISBAishiLoyaltyConditionState.ReputationLostPerKill <= 0f)
                return false;

            int raidGeneration;

            lock (Sync)
            {
                if (_penaltyRequestedThisRaid)
                    return false;

                _penaltyRequestedThisRaid = true;
                raidGeneration = _raidGeneration;
            }

            var localStanding = TryGetLocalAishiStanding(out var standingBefore)
                ? standingBefore
                : Math.Max(0d, ISBAishiLoyaltyConditionState.ReputationLostPerKill);

            ISBAishiRaidResultState.ArmExpectedPenalty(
                ISBAishiLoyaltyConditionState.ShowReputationLossInRaidResults,
                localStanding,
                ISBAishiLoyaltyConditionState.ReputationLostPerKill);

            var request = JsonConvert.SerializeObject(new
            {
                reason = "first_isb_kill_breaks_aishi_pact"
            });

            _ = ProcessKillPenaltyRequestAsync(raidGeneration, request);
            return true;
        }

        private static async Task ProcessKillPenaltyRequestAsync(int raidGeneration, string request)
        {
            try
            {
                var json = await RequestHandler.PostJsonAsync("/ISB/aishi/betrayal", request)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(json))
                {
                    QueueFailure(
                        raidGeneration,
                        "[ISB AISHI LOYALTY] The server returned an empty response while applying the betrayal penalty.");
                    return;
                }

                var response = JsonConvert.DeserializeObject<ISBAishiBetrayalResponse>(json);

                if (response == null)
                {
                    QueueFailure(
                        raidGeneration,
                        "[ISB AISHI LOYALTY] The betrayal penalty response could not be read.");
                    return;
                }

                ISBAishiReputationWorker.Enqueue(
                    () => ApplyKillPenaltyResponse(raidGeneration, response));
            }
            catch (Exception ex)
            {
                QueueFailure(
                    raidGeneration,
                    $"[ISB AISHI LOYALTY] Failed to apply the one-time Aishi standing penalty: {ex.Message}",
                    error: true);
            }
        }

        private static void ApplyKillPenaltyResponse(
            int raidGeneration,
            ISBAishiBetrayalResponse response)
        {
            if (!IsCurrentRaid(raidGeneration))
                return;

            if (!response.enabled || !response.eligibleAtRaidStart)
            {
                ISBAishiRaidResultState.Reset();
                return;
            }

            ISBAishiRaidResultState.Set(
                response.showInRaidResults,
                response.standingBefore,
                response.standingAfter,
                response.standingDelta);

            if (response.showInRaidResults && response.standingDelta < 0d)
            {
                Plugin.LogSource.LogInfo(
                    $"[ISB AISHI LOYALTY] Post-raid Standing entry armed");
            }

            if (response.standingDelta < 0d)
            {
                Plugin.LogSource.LogInfo(
                    $"[ISB AISHI LOYALTY] First ISB kill of the raid: Aishi standing " +
                    $"{response.standingBefore:F3} -> {response.standingAfter:F3} " +
                    $"({response.standingDelta:F3}). No further kill penalty will be applied this raid.");
            }
            else if (response.alreadyAppliedThisRaid)
            {
                Plugin.LogSource.LogInfo(
                    "[ISB AISHI LOYALTY] The Aishi standing penalty had already been applied in this raid.");
            }
        }

        private static bool IsCurrentRaid(int raidGeneration)
        {
            lock (Sync)
                return raidGeneration == _raidGeneration;
        }

        private static void QueueFailure(
            int raidGeneration,
            string message,
            bool error = false)
        {
            ISBAishiReputationWorker.Enqueue(() =>
            {
                if (!IsCurrentRaid(raidGeneration))
                    return;

                if (error)
                    Plugin.LogSource.LogError(message);
                else
                    Plugin.LogSource.LogWarning(message);
            });
        }

        private static bool TryGetLocalAishiStanding(out double standing)
        {
            standing = 0d;

            try
            {
                var profile = Singleton<GameWorld>.Instance?.MainPlayer?.Profile;
                if (profile?.TradersInfo == null)
                    return false;

                if (!profile.TradersInfo.TryGetValue(AishiTraderId, out var aishi) || aishi == null)
                    return false;

                standing = Convert.ToDouble(aishi.Standing);
                return true;
            }
            catch
            {
                standing = 0d;
                return false;
            }
        }
    }
}
