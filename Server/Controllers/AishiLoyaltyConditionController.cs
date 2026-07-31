using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Servers;
using System.Globalization;
using System.Reflection;

namespace ISBSpecialForcesServer.Controllers;

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

public record ISBAishiRaidStartRequest : IRequestData
{
}

public record ISBAishiBetrayalRequest : IRequestData
{
    public string reason { get; set; } = "isb_operator_killed";
}

public record ISBAishiMailRequest : IRequestData
{
    public int isbKills { get; set; }
}

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
    public string reason { get; set; } = string.Empty;
}

internal sealed class ISBAishiRaidPenaltyState
{
    public bool eligibleAtRaidStart { get; set; }
    public bool registered { get; set; }
    public bool raidEnding { get; set; }
    public bool commitInProgress { get; set; }
    public bool committed { get; set; }
    public double standingBefore { get; set; }
    public double standingAfter { get; set; }
    public double standingDelta { get; set; }
}

[Injectable(InjectionType.Singleton)]
public class AishiLoyaltyConditionController
{
    public static readonly MongoId AishiTraderId = new MongoId("690766de550bc322a810ea1e");

    private readonly object _raidStateSync = new();
    private readonly Dictionary<string, ISBAishiRaidPenaltyState> _raidStates = new(StringComparer.Ordinal);
    private readonly ProfileHelper _profileHelper;
    private const string BeforeSaveCallbackId = "ISB.SOF.AishiDeferredBetrayalPenalty";

    private readonly MailSendService _mailSendService;
    private readonly ConfigController _configController;
    private readonly ISBRevengeController _revengeController;
    private readonly ILogger<AishiLoyaltyConditionController> _logger;

    public AishiLoyaltyConditionController(
        ProfileHelper profileHelper,
        MailSendService mailSendService,
        SaveServer saveServer,
        ConfigController configController,
        ISBRevengeController revengeController,
        ILogger<AishiLoyaltyConditionController> logger)
    {
        _profileHelper = profileHelper;
        _mailSendService = mailSendService;
        _configController = configController;
        _revengeController = revengeController;
        _logger = logger;

#pragma warning disable CS0618
        saveServer.AddBeforeSaveCallback(BeforeSaveCallbackId, profile =>
        {
            CommitPendingPenaltyBeforeSave(profile);
            return profile;
        });
#pragma warning restore CS0618
    }

    public ISBAishiLoyaltyConditionResponse BuildResponse(string sessionId)
    {
        var condition = GetCondition();

        var response = new ISBAishiLoyaltyConditionResponse
        {
            enabled = condition.enabled,
            active = false,
            loyaltyLevel = 0,
            minLoyaltyLevel = condition.minLoyaltyLevel,
            breakOnHit = condition.breakOnHit,
            logs = condition.logs,
            damageThreshold = condition.damageThreshold,
            thresholdPerFaction = condition.thresholdPerFaction,
            loseReputationOnKill = condition.loseReputationOnKill,
            reputationLostPerKill = condition.reputationLostPerKill,
            showReputationLossInRaidResults = condition.showReputationLossInRaidResults,
            extendToPlayerAllies = condition.extendToPlayerAllies
        };

        if (!condition.enabled)
        {
            return response;
        }

        var loyalty = GetAishiLoyaltyLevel(sessionId);
        var revengeActive = _revengeController.GetStatus(sessionId).active;

        response.loyaltyLevel = loyalty;
        response.active = loyalty >= condition.minLoyaltyLevel && !revengeActive;

        if (condition.logs)
        {
            _logger.LogInformation(
                $"\x1b[38;2;150;60;220m[ISB SOF] Aishi loyalty check for '{sessionId}': Aishi loyalty {loyalty}, required {condition.minLoyaltyLevel}, active {response.active}.\x1b[0m");
        }

        return response;
    }

    public void BeginRaid(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var condition = GetCondition();
        var loyalty = condition.enabled ? GetAishiLoyaltyLevel(sessionId) : 0;
        var revengeActiveAtRaidStart = _revengeController.GetStatus(sessionId).active;
        var eligibleAtRaidStart = condition.enabled
            && loyalty >= condition.minLoyaltyLevel
            && !revengeActiveAtRaidStart;

        lock (_raidStateSync)
        {
            _raidStates[sessionId] = new ISBAishiRaidPenaltyState
            {
                eligibleAtRaidStart = eligibleAtRaidStart
            };
        }

        if (condition.logs)
        {
            _logger.LogInformation(
                $"[ISB AISHI LOYALTY] Raid penalty state initialized for {ShortId(sessionId)}. " +
                $"Eligible at raid start={eligibleAtRaidStart}; revenge active={revengeActiveAtRaidStart}.");
        }
    }

    public ISBAishiBetrayalResponse ApplyBetrayalPenalty(
        string sessionId,
        ISBAishiBetrayalRequest? request)
    {
        var condition = GetCondition();
        var response = new ISBAishiBetrayalResponse
        {
            enabled = condition.enabled && condition.loseReputationOnKill,
            showInRaidResults = condition.showReputationLossInRaidResults,
            reason = string.IsNullOrWhiteSpace(request?.reason)
                ? "isb_operator_killed"
                : request!.reason
        };

        if (!response.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return response;
        }

        lock (_raidStateSync)
        {
            if (!_raidStates.TryGetValue(sessionId, out var state))
            {
                var loyalty = GetAishiLoyaltyLevel(sessionId);
                var revengeActive = _revengeController.GetStatus(sessionId).active;
                state = new ISBAishiRaidPenaltyState
                {
                    eligibleAtRaidStart = loyalty >= condition.minLoyaltyLevel
                        && !revengeActive
                };
                _raidStates[sessionId] = state;
            }

            response.eligibleAtRaidStart = state.eligibleAtRaidStart;

            if (!state.eligibleAtRaidStart)
            {
                return response;
            }

            if (state.registered)
            {
                response.applied = state.standingDelta < 0d;
                response.alreadyAppliedThisRaid = true;
                response.standingBefore = state.standingBefore;
                response.standingAfter = state.standingAfter;
                response.standingDelta = state.standingDelta;
                return response;
            }

            var configuredLoss = Math.Max(0d, condition.reputationLostPerKill);

            if (configuredLoss <= 0d)
            {
                return response;
            }

            try
            {
                var pmcProfile = _profileHelper.GetPmcProfile(sessionId);
                var traders = pmcProfile?.TradersInfo;

                if (traders == null
                    || !traders.TryGetValue(AishiTraderId, out var aishi)
                    || aishi == null)
                {
                    _logger.LogWarning(
                        $"[ISB AISHI LOYALTY] Aishi ({AishiTraderId}) was not found for profile {ShortId(sessionId)}. Penalty was not registered.");
                    return response;
                }

                if (!TryReadStanding(aishi, out var before))
                {
                    _logger.LogWarning(
                        $"[ISB AISHI LOYALTY] Could not read Aishi standing for profile {ShortId(sessionId)}.");
                    return response;
                }

                var after = Math.Max(0d, before - configuredLoss);
                var delta = after - before;

                state.registered = true;
                state.standingBefore = before;
                state.standingAfter = after;
                state.standingDelta = delta;

                response.applied = delta < 0d;
                response.standingBefore = before;
                response.standingAfter = after;
                response.standingDelta = delta;

                if (condition.logs)
                {
                    _logger.LogInformation(
                        $"[ISB AISHI LOYALTY] First betrayal kill registered for {ShortId(sessionId)}. " +
                        $"Aishi standing will be changed at raid end: {before:F3} -> {after:F3} ({delta:F3}).");
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"[ISB AISHI LOYALTY] Failed to register the first-kill standing penalty for {ShortId(sessionId)}: {ex.Message}");
                return response;
            }
        }
    }

    public void PrepareRaidEnd(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_raidStateSync)
        {
            if (!_raidStates.TryGetValue(sessionId, out var state))
            {
                return;
            }

            if (!state.eligibleAtRaidStart || !state.registered)
            {
                _raidStates.Remove(sessionId);
                return;
            }

            state.raidEnding = true;
        }
    }

    private void CommitPendingPenaltyBeforeSave(SptProfile profile)
    {
        var sessionId = profile.ProfileInfo?.ProfileId?.ToString();

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        ISBAishiRaidPenaltyState? state = null;

        lock (_raidStateSync)
        {
            if (!_raidStates.TryGetValue(sessionId, out state)
                || !state.eligibleAtRaidStart
                || !state.registered
                || !state.raidEnding
                || state.committed
                || state.commitInProgress)
            {
                return;
            }

            state.commitInProgress = true;
        }

        var completed = false;

        try
        {
            var condition = GetCondition();
            var configuredLoss = condition.enabled && condition.loseReputationOnKill
                ? Math.Max(0d, condition.reputationLostPerKill)
                : 0d;

            if (configuredLoss <= 0d)
            {
                completed = true;
                return;
            }

            var traders = profile.CharacterData?.PmcData?.TradersInfo;

            if (traders == null
                || !traders.TryGetValue(AishiTraderId, out var aishi)
                || aishi == null)
            {
                _logger.LogWarning(
                    $"[ISB AISHI LOYALTY] Aishi ({AishiTraderId}) was not found for profile {ShortId(sessionId)} during the final raid save. Standing was not changed.");
                return;
            }

            if (!TryApplyStandingLossDirect(aishi, configuredLoss, out var before, out var after))
            {
                _logger.LogWarning(
                    $"[ISB AISHI LOYALTY] Could not update Aishi standing for profile {ShortId(sessionId)} during the final raid save.");
                return;
            }

            var delta = after - before;

            lock (_raidStateSync)
            {
                state.standingBefore = before;
                state.standingAfter = after;
                state.standingDelta = delta;
                state.committed = true;
            }

            var actualLoss = Math.Max(0d, -delta);

            if (actualLoss > 0d)
            {
                SendAishiBetrayalMessage(sessionId, actualLoss);
            }

            if (condition.logs)
            {
                _logger.LogInformation(
                    $"[ISB AISHI LOYALTY] Deferred betrayal penalty committed during the final raid save for {ShortId(sessionId)}. " +
                    $"Aishi standing {before:F3} -> {after:F3} ({delta:F3}).");
            }

            completed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                $"[ISB AISHI LOYALTY] Failed to commit the standing penalty for {ShortId(sessionId)} during the final raid save: {ex.Message}");
        }
        finally
        {
            lock (_raidStateSync)
            {
                if (completed)
                {
                    _raidStates.Remove(sessionId);
                }
                else if (state != null)
                {
                    state.commitInProgress = false;
                }
            }
        }
    }

    public void EndRaid(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_raidStateSync)
        {
            _raidStates.Remove(sessionId);
        }
    }
    public void SendAishiMessage(string sessionId, int isbKills)
    {
        var condition = GetCondition();

        if (!condition.enabled || !condition.loseReputationOnKill || isbKills <= 0)
        {
            return;
        }

        SendAishiBetrayalMessage(sessionId, Math.Max(0d, condition.reputationLostPerKill));
    }

    private void SendAishiBetrayalMessage(string sessionId, double actualLoss)
    {
        var condition = GetCondition();

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(condition.reputationMessage))
        {
            return;
        }

        try
        {
            var message = condition.reputationMessage
                .Replace("{kills}", "1")
                .Replace("{loss}", actualLoss.ToString("F2", CultureInfo.InvariantCulture));

            _mailSendService.SendDirectNpcMessageToPlayer(
                sessionId,
                AishiTraderId,
                MessageType.NpcTraderMessage,
                message,
                null,
                null,
                null,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[ISB SOF] Could not send the Aishi message: {ex.Message}");
        }
    }

    private static bool TryReadStanding(object traderInfo, out double standing)
    {
        standing = 0d;

        var standingProperty = traderInfo.GetType().GetProperty(
            "Standing",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (standingProperty == null || !standingProperty.CanRead)
        {
            return false;
        }

        var currentValue = standingProperty.GetValue(traderInfo);

        if (currentValue == null)
        {
            return false;
        }

        standing = Convert.ToDouble(currentValue, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryApplyStandingLossDirect(
        object traderInfo,
        double loss,
        out double before,
        out double after)
    {
        before = 0d;
        after = 0d;

        var standingProperty = traderInfo.GetType().GetProperty(
            "Standing",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (standingProperty == null || !standingProperty.CanRead || !standingProperty.CanWrite)
        {
            return false;
        }

        var currentValue = standingProperty.GetValue(traderInfo);

        if (currentValue == null)
        {
            return false;
        }

        before = Convert.ToDouble(currentValue, CultureInfo.InvariantCulture);
        after = Math.Max(0d, before - loss);

        var propertyType = Nullable.GetUnderlyingType(standingProperty.PropertyType)
            ?? standingProperty.PropertyType;
        var convertedValue = Convert.ChangeType(after, propertyType, CultureInfo.InvariantCulture);

        standingProperty.SetValue(traderInfo, convertedValue);
        return true;
    }

    private AishiLoyaltyConditionConfig GetCondition()
    {
        return _configController.ModConfig.aishiLoyaltyCondition
            ?? new AishiLoyaltyConditionConfig();
    }

    private int GetAishiLoyaltyLevel(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return 0;
        }

        try
        {
            var pmcProfile = _profileHelper.GetPmcProfile(sessionId);
            var traders = pmcProfile?.TradersInfo;

            if (traders == null)
            {
                return 0;
            }

            if (!traders.TryGetValue(AishiTraderId, out var aishi) || aishi == null)
            {
                return 0;
            }

            return aishi.LoyaltyLevel ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[ISB SOF] Failed to read Aishi loyalty '{sessionId}': {ex.Message}");
            return 0;
        }
    }

    private static string ShortId(string sessionId)
    {
        return sessionId.Length <= 8 ? sessionId : sessionId[..8];
    }
}
