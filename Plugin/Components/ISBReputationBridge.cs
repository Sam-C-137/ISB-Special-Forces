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
        private static readonly object Sync = new object ();
        private static bool _penaltyRequestedThisRaid;
        private static bool _penaltyMirroredThisRaid;
        private static int _raidGeneration;
        public static void Reset()
        {
            lock (Sync)
            {
                _penaltyRequestedThisRaid = false;
                _penaltyMirroredThisRaid = false;
                _raidGeneration++;
            }

            ISBAishiRaidResultState.Reset();
        }

        public static void BeginRaid(bool hasRequiredItem, bool isScavRaid, string locationId)
        {
            try
            {
                var request = JsonConvert.SerializeObject(new { hasRequiredItem, isScavRaid, locationId = locationId ?? string.Empty });
                RequestHandler.PostJson("/ISB/aishi/raid/start", request);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ISB AISHI LOYALTY] Could not initialize the server-side raid friendly state: {ex.Message}");
            }
        }

        public static bool ApplyKillPenaltyOnce()
        {
            if (!ISBAishiLoyaltyConditionState.EligibleForKillPenalty || !ISBAishiLoyaltyConditionState.LoseReputationOnKill)
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

            var localStanding = TryGetLocalAishiStanding(out var standingBefore) ? standingBefore : Math.Max(0d, ISBAishiLoyaltyConditionState.ReputationLostPerKill);
            ISBAishiRaidResultState.ArmExpectedPenalty(ISBAishiLoyaltyConditionState.ShowReputationLossInRaidResults, localStanding, ISBAishiLoyaltyConditionState.ReputationLostPerKill);
            var request = JsonConvert.SerializeObject(new { reason = "first_isb_kill_breaks_aishi_pact" });
            _ = ProcessKillPenaltyRequestAsync(raidGeneration, request);
            return true;
        }

        private static async Task ProcessKillPenaltyRequestAsync(int raidGeneration, string request)
        {
            try
            {
                var json = await RequestHandler.PostJsonAsync("/ISB/aishi/betrayal", request).ConfigureAwait(false);
                if (string.IsNullOrEmpty(json))
                {
                    QueueFailure(raidGeneration, "[ISB AISHI LOYALTY] The server returned an empty response while applying the betrayal penalty.");
                    return;
                }

                var response = JsonConvert.DeserializeObject<ISBAishiBetrayalResponse>(json);
                if (response == null)
                {
                    QueueFailure(raidGeneration, "[ISB AISHI LOYALTY] The betrayal penalty response could not be read.");
                    return;
                }

                ISBAishiReputationWorker.Enqueue(() => ApplyKillPenaltyResponse(raidGeneration, response));
            }
            catch (Exception ex)
            {
                QueueFailure(raidGeneration, $"[ISB AISHI LOYALTY] Failed to apply the one-time Aishi standing penalty: {ex.Message}");
            }
        }

        internal static void SynchronizePenalty(ISBAishiBetrayalResponse response)
        {
            if (response != null)
                ApplyKillPenaltyResponse(_raidGeneration, response);
        }

        private static void ApplyKillPenaltyResponse(int raidGeneration, ISBAishiBetrayalResponse response)
        {
            if (!IsCurrentRaid(raidGeneration))
                return;
            if (!response.enabled || !response.eligibleAtRaidStart)
            {
                ISBAishiRaidResultState.Reset();
                return;
            }

            if (!response.applied && !response.alreadyAppliedThisRaid)
                return;
            lock (Sync)
            {
                if (_penaltyMirroredThisRaid)
                    return;
                _penaltyMirroredThisRaid = true;
                _penaltyRequestedThisRaid = true;
            }

            ISBAishiRaidResultState.Set(response.showInRaidResults, response.standingBefore, response.standingAfter, response.standingDelta);
            if (response.standingDelta < 0d)
            {
                RefreshLocalAishiStanding(response.standingAfter);
                if (response.showInRaidResults)
                {
                    if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                    {
                        Plugin.LogSource.LogInfo($"[ISB AISHI LOYALTY] Post-raid Standing entry armed with delta {response.standingDelta:F3}.");
                    }

                    ISBNativeFenceStandingBridge.TryRecord(response.standingDelta);
                }
            }
        }

        private static bool IsCurrentRaid(int raidGeneration)
        {
            lock (Sync)
                return raidGeneration == _raidGeneration;
        }

        private static void QueueFailure(int raidGeneration, string message)
        {
            ISBAishiReputationWorker.Enqueue(() =>
            {
                if (!IsCurrentRaid(raidGeneration))
                    return;
                Plugin.LogSource.LogError(message);
            });
        }

        private static void RefreshLocalAishiStanding(double standingAfter)
        {
            try
            {
                var profile = Singleton<GameWorld>.Instance?.MainPlayer?.Profile;
                if (profile?.TradersInfo == null || !profile.TradersInfo.TryGetValue(AishiTraderId, out var aishi) || aishi == null)
                {
                    Plugin.LogSource.LogWarning($"[ISB AISHI LOYALTY] Aishi ({AishiTraderId}) was not found in the local profile trader list.");
                    return;
                }

                aishi.SetStanding(standingAfter);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ISB AISHI LOYALTY] Server standing was updated, but the local profile mirror could not be refreshed: {ex.Message}");
            }
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
