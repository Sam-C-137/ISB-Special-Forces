using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using System.Globalization;
using System.Reflection;

namespace ISBSpecialForcesServer.Controllers;
public class ISBAishiLoyaltyConditionResponse
{
    public bool enabled { get; set; }
    public bool active { get; set; }
    public string requiredItemTpl { get; set; } = string.Empty;
    public bool breakOnHit { get; set; }
    public bool logs { get; set; }
    public float damageThreshold { get; set; }
    public bool thresholdPerFaction { get; set; }
    public bool loseReputationOnKill { get; set; }
    public float reputationLostPerKill { get; set; }
    public bool showReputationLossInRaidResults { get; set; }
    public bool extendToPlayerAllies { get; set; }
    public string friendlyConditionMode { get; set; } = "any";
    public bool itemConditionConfigured { get; set; }
    public bool questTruceConfigured { get; set; }
    public bool questTruceActive { get; set; }
    public bool revengeActive { get; set; }
    public List<string> activeQuestTruceRuleIds { get; set; } = new();
}

public record ISBAishiParticipantsRequest : IRequestData
{
    public string[] profileIds { get; set; } = [];
    public string locationId { get; set; } = string.Empty;
}

public record ISBAishiParticipantReport : IRequestData
{
    public string profileId { get; set; } = string.Empty;
    public string token { get; set; } = string.Empty;
    public int victimRole { get; set; }
    public string victimId { get; set; } = string.Empty;
}

public sealed class ISBAishiParticipantState
{
    public string profileId { get; set; } = string.Empty;
    public string token { get; set; } = string.Empty;
    public bool eligible { get; set; }
    public bool broken { get; set; }
    public ISBAishiLoyaltyConditionResponse response { get; set; } = new();
    public ISBAishiBetrayalResponse? penalty { get; set; }

    public static bool SameRaid(string? requester, string? participant) => !string.IsNullOrWhiteSpace(requester) && string.Equals(requester, participant, StringComparison.Ordinal);
}

public record ISBAishiRaidStartRequest : IRequestData
{
    public bool hasRequiredItem { get; set; }
    public bool isScavRaid { get; set; }
    public string locationId { get; set; } = string.Empty;
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
    public string token { get; set; } = Guid.NewGuid().ToString("N");
    public bool broken { get; set; }
    public string raidToken { get; set; } = string.Empty;
    public bool eligibleAtRaidStart { get; set; }
    public bool applied { get; set; }
    public double standingBefore { get; set; }
    public double standingAfter { get; set; }
    public double standingDelta { get; set; }
}

[Injectable(InjectionType.Singleton)]
public class AishiLoyaltyConditionController
{
    public static readonly MongoId AishiTraderId = new MongoId("690766de550bc322a810ea1e");
    private readonly FikaRaidContext _fika;
    private readonly object _raidStateSync = new();
    private readonly Dictionary<string, ISBAishiRaidPenaltyState> _raidStates = new(StringComparer.Ordinal);
    private readonly ProfileHelper _profileHelper;
    private readonly MailSendService _mailSendService;
    private readonly ConfigController _configController;
    private readonly ISBRevengeController _revengeController;
    private readonly ISBQuestTruceController _questTruceController;
    private readonly ISBSpawnController _spawnController;
    private readonly ILogger<AishiLoyaltyConditionController> _logger;
    public AishiLoyaltyConditionController(ProfileHelper profileHelper, MailSendService mailSendService, ConfigController configController, ISBRevengeController revengeController, ISBQuestTruceController questTruceController, ISBSpawnController spawnController, ILogger<AishiLoyaltyConditionController> logger, FikaRaidContext fika)
    {
        _profileHelper = profileHelper;
        _fika = fika;
        _mailSendService = mailSendService;
        _configController = configController;
        _revengeController = revengeController;
        _questTruceController = questTruceController;
        _spawnController = spawnController;
        _logger = logger;
    }

    public ISBAishiLoyaltyConditionResponse BuildResponse(string sessionId, string? locationId = null)
    {
        var condition = GetCondition();
        var friendlyConditions = condition.friendlyConditions ?? new FriendlyConditionsConfig();
        var questEvaluation = condition.enabled ? _questTruceController.Evaluate(sessionId, locationId) : new ISBQuestTruceEvaluation();
        var revengeActive = condition.enabled && _revengeController.GetStatus(sessionId).active;
        var itemConfigured = !string.IsNullOrWhiteSpace(condition.requiredItemTpl);
        var response = new ISBAishiLoyaltyConditionResponse
        {
            enabled = condition.enabled,
            active = condition.enabled && !revengeActive && (itemConfigured || questEvaluation.active),
            requiredItemTpl = condition.requiredItemTpl ?? string.Empty,
            breakOnHit = condition.breakOnHit,
            logs = condition.logs,
            damageThreshold = condition.damageThreshold,
            thresholdPerFaction = condition.thresholdPerFaction,
            loseReputationOnKill = condition.loseReputationOnKill && !IsContainmentBreachRaid(sessionId),
            reputationLostPerKill = condition.reputationLostPerKill,
            showReputationLossInRaidResults = condition.showReputationLossInRaidResults && !IsContainmentBreachRaid(sessionId),
            extendToPlayerAllies = condition.extendToPlayerAllies,
            friendlyConditionMode = NormalizeFriendlyMode(friendlyConditions.mode),
            itemConditionConfigured = itemConfigured,
            questTruceConfigured = questEvaluation.configured,
            questTruceActive = questEvaluation.active,
            revengeActive = revengeActive,
            activeQuestTruceRuleIds = questEvaluation.activeRuleIds
        };
        if (condition.logs)
            _logger.LogInformation($"[ISB AISHI LOYALTY] Friendly condition check for '{sessionId}': radio={response.requiredItemTpl}, questTruce={response.questTruceActive}, revenge={revengeActive}, active={response.active}.");
        return response;
    }

    public List<ISBAishiParticipantState> Participants(string requester, ISBAishiParticipantsRequest request)
    {
        var result = new List<ISBAishiParticipantState>();
        if (request?.profileIds == null || request.profileIds.Length > 64 || request.profileIds.Length == 0)
            return result;
        var raid = _fika.EventSession(requester);
        var wanted = new HashSet<string>(request.profileIds, StringComparer.Ordinal);
        foreach (var pair in _profileHelper.GetProfiles())
        {
            var profileId = pair.Value.CharacterData?.PmcData?.Id.ToString();
            if (string.IsNullOrEmpty(profileId) || !wanted.Contains(profileId))
                continue;
            var session = pair.Key.ToString();
            if (!ISBAishiParticipantState.SameRaid(raid, _fika.EventSession(session)))
                continue;
            lock (_raidStateSync)
            {
                if (!_raidStates.TryGetValue(session, out var state))
                    continue;
                result.Add(new ISBAishiParticipantState { profileId = profileId, token = state.token, eligible = state.eligibleAtRaidStart, broken = state.broken || state.applied, response = BuildResponse(session, request.locationId), penalty = state.applied ? new ISBAishiBetrayalResponse { enabled = true, applied = state.standingDelta < 0, alreadyAppliedThisRaid = true, eligibleAtRaidStart = state.eligibleAtRaidStart, showInRaidResults = GetCondition().showReputationLossInRaidResults, standingBefore = state.standingBefore, standingAfter = state.standingAfter, standingDelta = state.standingDelta } : null });
            }
        }

        return result;
    }

    public ISBAishiBetrayalResponse ReportParticipant(string requester, ISBAishiParticipantReport request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.profileId) || string.IsNullOrWhiteSpace(request.token) || request.victimRole < 13701 || request.victimRole > 13716 || request.victimRole is 13707 or 13708)
            return new();
        var raid = _fika.EventSession(requester);
        foreach (var pair in _profileHelper.GetProfiles())
        {
            if (pair.Value.CharacterData?.PmcData?.Id.ToString() != request.profileId)
                continue;
            var session = pair.Key.ToString();
            if (!ISBAishiParticipantState.SameRaid(raid, _fika.EventSession(session)))
                return new();
            lock (_raidStateSync)
            {
                if (!_raidStates.TryGetValue(session, out var state) || state.token != request.token)
                    return new();
                if (state.eligibleAtRaidStart && !state.broken)
                {
                    state.broken = true;
                    _revengeController.Register(session, new ISBRevengeRegisterRequest { reason = "aishi_loyalty_broken", victimRole = request.victimRole });
                }

                if (!string.IsNullOrEmpty(request.victimId))
                {
                    _revengeController.RegisterKill(session, new ISBRevengeKillRequest { victimRole = request.victimRole, victimId = request.victimId });
                    return ApplyBetrayalPenalty(session, new ISBAishiBetrayalRequest());
                }

                return new();
            }
        }

        return new();
    }

    public void BeginRaid(string sessionId, bool hasRequiredItem, bool isScavRaid, string? locationId, string? raidToken = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_raidStateSync)
        {
            if (!string.IsNullOrEmpty(raidToken) && _raidStates.TryGetValue(sessionId, out var existing) && existing.raidToken == raidToken)
                return;
            var condition = GetCondition();
            var friendlyConditions = condition.friendlyConditions ?? new FriendlyConditionsConfig();
            var questEvaluation = _questTruceController.BeginRaid(sessionId, isScavRaid, locationId);
            var revengeActiveAtRaidStart = _revengeController.GetStatus(sessionId).active;
            var itemConfigured = !string.IsNullOrWhiteSpace(condition.requiredItemTpl);
            var itemActive = itemConfigured && hasRequiredItem;
            var eligibleByFriendlyConditions = EvaluateFriendlyConditions(NormalizeFriendlyMode(friendlyConditions.mode), itemConfigured, itemActive, questEvaluation.configured, questEvaluation.active);
            var eligibleAtRaidStart = condition.enabled && !isScavRaid && eligibleByFriendlyConditions && !revengeActiveAtRaidStart;
            lock (_raidStateSync)
            {
                _raidStates[sessionId] = new ISBAishiRaidPenaltyState
                {
                    raidToken = raidToken ?? string.Empty,
                    eligibleAtRaidStart = eligibleAtRaidStart
                };
            }

            if (condition.logs)
            {
                _logger.LogInformation($"[ISB AISHI LOYALTY] Raid penalty state initialized for {ShortId(sessionId)}. " + $"Eligible at raid start={eligibleAtRaidStart}; revenge active={revengeActiveAtRaidStart}.");
            }
        }
    }

    public ISBAishiBetrayalResponse ApplyBetrayalPenalty(string sessionId, ISBAishiBetrayalRequest? request)
    {
        var condition = GetCondition();
        var response = new ISBAishiBetrayalResponse
        {
            enabled = condition.enabled && condition.loseReputationOnKill,
            showInRaidResults = condition.showReputationLossInRaidResults,
            reason = string.IsNullOrWhiteSpace(request?.reason) ? "isb_operator_killed" : request!.reason
        };
        if (!response.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return response;
        }

        if (IsContainmentBreachRaid(sessionId))
        {
            response.enabled = false;
            response.showInRaidResults = false;
            response.reason = "containment_breach";
            return response;
        }

        lock (_raidStateSync)
        {
            if (!_raidStates.TryGetValue(sessionId, out var state))
            {
                state = new ISBAishiRaidPenaltyState
                {
                    eligibleAtRaidStart = false
                };
                _raidStates[sessionId] = state;
            }

            response.eligibleAtRaidStart = state.eligibleAtRaidStart;
            if (!state.eligibleAtRaidStart)
            {
                return response;
            }

            if (state.applied)
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
                if (traders == null || !traders.TryGetValue(AishiTraderId, out var aishi) || aishi == null)
                {
                    _logger.LogWarning($"[ISB AISHI LOYALTY] Aishi ({AishiTraderId}) was not found for profile {ShortId(sessionId)}. Standing was not changed.");
                    return response;
                }

                if (!TryApplyStandingLoss(aishi, configuredLoss, out var before, out var after))
                {
                    _logger.LogWarning($"[ISB AISHI LOYALTY] Could not read or update Aishi standing for profile {ShortId(sessionId)}.");
                    return response;
                }

                var delta = after - before;
                state.applied = true;
                state.standingBefore = before;
                state.standingAfter = after;
                state.standingDelta = delta;
                response.applied = delta < 0d;
                response.standingBefore = before;
                response.standingAfter = after;
                response.standingDelta = delta;
                if (condition.logs)
                {
                    _logger.LogInformation($"[ISB AISHI LOYALTY] First betrayal kill of the raid for {ShortId(sessionId)}. Aishi standing {before:F3} -> {after:F3} ({delta:F3}).");
                }

                if (delta < 0d)
                {
                    SendAishiBetrayalMessage(sessionId, -delta);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ISB AISHI LOYALTY] Failed to apply the first-kill standing penalty for {ShortId(sessionId)}: {ex.Message}");
                return response;
            }
        }
    }

    public void EndRaid(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _questTruceController.EndRaid(sessionId);
        lock (_raidStateSync)
        {
            _raidStates.Remove(sessionId);
        }
    }

    public void SendAishiMessage(string sessionId, int isbKills)
    {
        var condition = GetCondition();
        if (!condition.enabled || !condition.loseReputationOnKill || isbKills <= 0 || IsContainmentBreachRaid(sessionId))
        {
            return;
        }

        SendAishiBetrayalMessage(sessionId, Math.Max(0d, condition.reputationLostPerKill));
    }

    private bool IsContainmentBreachRaid(string sessionId)
    {
        return !string.IsNullOrWhiteSpace(sessionId) && _spawnController.GetLabsPreBlackoutEventStatus(sessionId).active;
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
            var message = condition.reputationMessage.Replace("{kills}", "1").Replace("{loss}", actualLoss.ToString("F2", CultureInfo.InvariantCulture));
            _mailSendService.SendDirectNpcMessageToPlayer(sessionId, AishiTraderId, MessageType.NpcTraderMessage, message, null, null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[ISB SOF] Could not send the Aishi message: {ex.Message}");
        }
    }

    private static bool TryApplyStandingLoss(object traderInfo, double loss, out double before, out double after)
    {
        before = 0d;
        after = 0d;
        var standingProperty = traderInfo.GetType().GetProperty("Standing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (standingProperty == null || !standingProperty.CanRead)
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
        var setStandingMethod = traderInfo.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(method => string.Equals(method.Name, "SetStanding", StringComparison.Ordinal) && method.GetParameters().Length == 1);
        if (setStandingMethod != null)
        {
            var parameterType = Nullable.GetUnderlyingType(setStandingMethod.GetParameters()[0].ParameterType) ?? setStandingMethod.GetParameters()[0].ParameterType;
            var convertedParameter = Convert.ChangeType(after, parameterType, CultureInfo.InvariantCulture);
            setStandingMethod.Invoke(traderInfo, new[] { convertedParameter });
            return true;
        }

        if (!standingProperty.CanWrite)
        {
            return false;
        }

        var propertyType = Nullable.GetUnderlyingType(standingProperty.PropertyType) ?? standingProperty.PropertyType;
        var convertedValue = Convert.ChangeType(after, propertyType, CultureInfo.InvariantCulture);
        standingProperty.SetValue(traderInfo, convertedValue);
        return true;
    }

    private static bool EvaluateFriendlyConditions(string mode, bool itemConfigured, bool itemActive, bool questConfigured, bool questActive)
    {
        var sourceCount = (itemConfigured ? 1 : 0) + (questConfigured ? 1 : 0);
        if (sourceCount == 0)
        {
            return false;
        }

        if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (itemConfigured && !itemActive)
            {
                return false;
            }

            if (questConfigured && !questActive)
            {
                return false;
            }

            return true;
        }

        return itemActive || questActive;
    }

    private static string NormalizeFriendlyMode(string? mode)
    {
        return string.Equals(mode?.Trim(), "all", StringComparison.OrdinalIgnoreCase) ? "all" : "any";
    }

    private AishiLoyaltyConditionConfig GetCondition()
    {
        return _configController.ModConfig.aishiLoyaltyCondition ?? new AishiLoyaltyConditionConfig();
    }

    private static string ShortId(string sessionId)
    {
        return sessionId.Length <= 8 ? sessionId : sessionId[..8];
    }
}
