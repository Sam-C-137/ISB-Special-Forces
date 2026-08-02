using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
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

            if (response.standingDelta < 0d)
            {
                RefreshLocalAishiStanding(response.standingAfter);

                if (response.showInRaidResults)
                {
                    Plugin.LogSource.LogInfo(
                        $"[ISB AISHI LOYALTY] Post-raid Standing entry armed with delta {response.standingDelta:F3}.");

                    ISBNativeFenceStandingBridge.TryRecord(response.standingDelta);
                }

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

        private static void RefreshLocalAishiStanding(double standingAfter)
        {
            try
            {
                var profile = Singleton<GameWorld>.Instance?.MainPlayer?.Profile;
                if (profile?.TradersInfo == null
                    || !profile.TradersInfo.TryGetValue(AishiTraderId, out var aishi)
                    || aishi == null)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB AISHI LOYALTY] Aishi ({AishiTraderId}) was not found in the local profile trader list.");
                    return;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var setStandingMethod = aishi.GetType()
                    .GetMethods(flags)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "SetStanding", StringComparison.Ordinal)
                        && method.GetParameters().Length == 1);

                if (setStandingMethod != null)
                {
                    var parameterType = Nullable.GetUnderlyingType(
                        setStandingMethod.GetParameters()[0].ParameterType)
                        ?? setStandingMethod.GetParameters()[0].ParameterType;
                    var convertedParameter = Convert.ChangeType(
                        standingAfter,
                        parameterType,
                        CultureInfo.InvariantCulture);

                    setStandingMethod.Invoke(aishi, new[] { convertedParameter });
                    return;
                }

                var standingProperty = aishi.GetType().GetProperty("Standing", flags);
                if (standingProperty == null || !standingProperty.CanWrite)
                {
                    throw new MissingMemberException(
                        aishi.GetType().FullName,
                        "SetStanding/Standing");
                }

                var propertyType = Nullable.GetUnderlyingType(standingProperty.PropertyType)
                    ?? standingProperty.PropertyType;
                var convertedValue = Convert.ChangeType(
                    standingAfter,
                    propertyType,
                    CultureInfo.InvariantCulture);

                standingProperty.SetValue(aishi, convertedValue);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB AISHI LOYALTY] Server standing was updated, but the local profile mirror could not be refreshed: {ex.Message}");
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
