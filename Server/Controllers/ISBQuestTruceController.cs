using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace ISBSpecialForcesServer.Controllers;
public sealed class ISBQuestTruceEvaluation
{
    public bool resetHostilityEachRaid { get; set; }
    public bool configured { get; set; }
    public bool active { get; set; }
    public List<string> activeRuleIds { get; set; } = new();
}

public sealed class ISBQuestTruceRuleState
{
    public bool initialized { get; set; }
    public bool lastStatusMatched { get; set; }
    public bool statusGateOpen { get; set; }
    public bool activated { get; set; }
    public bool permanent { get; set; }
    public int remainingRaids { get; set; }
    public bool activeThisRaid { get; set; }
    public bool currentRaidIsScav { get; set; }
    public bool currentRaidEligibleMap { get; set; }
    public string currentRaidToken { get; set; } = string.Empty;
    public DateTime lastUpdatedUtc { get; set; }
}

public sealed class ISBQuestTruceProfileState
{
    public Dictionary<string, ISBQuestTruceRuleState> rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ISBQuestTruceStore
{
    public Dictionary<string, ISBQuestTruceProfileState> profiles { get; set; } = new(StringComparer.Ordinal);
}

[Injectable(InjectionType.Singleton)]
public class ISBQuestTruceController
{
    private readonly object _sync = new();
    private readonly ProfileHelper _profileHelper;
    private readonly ConfigController _configController;
    private readonly ILogger<ISBQuestTruceController> _logger;
    private readonly string _storePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private ISBQuestTruceStore _store;
    public ISBQuestTruceController(ModHelper modHelper, ProfileHelper profileHelper, ConfigController configController, ILogger<ISBQuestTruceController> logger)
    {
        _profileHelper = profileHelper;
        _configController = configController;
        _logger = logger;
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _storePath = Path.Combine(modPath, "data", "quest_truce.json");
        _store = LoadStore();
    }

    public ISBQuestTruceEvaluation Evaluate(string sessionId, string? locationId = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ISBQuestTruceEvaluation();
        }

        var config = GetConfig();
        var validRules = GetValidRules(config);
        if (!config.enabled || validRules.Count == 0)
        {
            return new ISBQuestTruceEvaluation
            {
                configured = false,
                active = false
            };
        }

        lock (_sync)
        {
            var profileState = GetOrCreateProfileState(sessionId);
            var result = new ISBQuestTruceEvaluation
            {
                configured = true
            };
            var changed = false;
            foreach (var rule in validRules)
            {
                var ruleKey = GetRuleKey(rule);
                var state = GetOrCreateRuleState(profileState, ruleKey, ref changed);
                var questStatus = TryGetQuestStatus(sessionId, rule.questId);
                var matched = questStatus.HasValue && GetStatuses(rule).Contains(questStatus.Value);
                if (EvaluateRule(rule, state, matched, ref changed) && rule.AppliesToMap(locationId))
                {
                    result.active = true;
                    result.activeRuleIds.Add(ruleKey);
                    result.resetHostilityEachRaid |= rule.resetHostilityEachRaid;
                }
            }

            if (changed)
            {
                SaveStore();
            }

            return result;
        }
    }

    public ISBQuestTruceEvaluation BeginRaid(string sessionId, bool isScavRaid, string? locationId)
    {
        var evaluation = Evaluate(sessionId, locationId);
        if (!evaluation.configured || string.IsNullOrWhiteSpace(sessionId))
        {
            return evaluation;
        }

        var config = GetConfig();
        var validRules = GetValidRules(config);
        lock (_sync)
        {
            var profileState = GetOrCreateProfileState(sessionId);
            var eligibleMap = IsISBEligibleMap(locationId);
            var changed = false;
            foreach (var rule in validRules)
            {
                var ruleKey = GetRuleKey(rule);
                var state = GetOrCreateRuleState(profileState, ruleKey, ref changed);
                var questStatus = TryGetQuestStatus(sessionId, rule.questId);
                var matched = questStatus.HasValue && GetStatuses(rule).Contains(questStatus.Value);
                var active = IsRuleActive(rule, state, matched) && rule.AppliesToMap(locationId);
                state.activeThisRaid = active;
                state.currentRaidIsScav = isScavRaid;
                state.currentRaidEligibleMap = eligibleMap;
                state.currentRaidToken = Guid.NewGuid().ToString("N");
                state.lastUpdatedUtc = DateTime.UtcNow;
                changed = true;
            }

            if (changed)
            {
                SaveStore();
            }
        }

        if (config.logs)
            _logger.LogInformation($"[ISB QUEST TRUCE] Raid started for {sessionId[..Math.Min(8, sessionId.Length)]}. Active={evaluation.active}; rules={string.Join(",", evaluation.activeRuleIds)}; scav={isScavRaid}; map={locationId}.");
        return evaluation;
    }

    public void EndRaid(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var config = GetConfig();
        var rules = config.enabled ? GetValidRules(config) : new List<QuestTruceRuleConfig>();
        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(sessionId, out var profileState) || profileState == null)
            {
                return;
            }

            var configuredRules = rules.ToDictionary(GetRuleKey, StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var entry in profileState.rules)
            {
                var state = entry.Value;
                if (state == null || string.IsNullOrEmpty(state.currentRaidToken))
                {
                    continue;
                }

                if (configuredRules.TryGetValue(entry.Key, out var rule))
                {
                    var durationMode = NormalizeDurationMode(rule.truceDuration?.mode);
                    if (durationMode == "raids" && state.activeThisRaid && state.remainingRaids > 0)
                    {
                        var consume = (!state.currentRaidIsScav || rule.consumeOnScavRaid) && (!rule.consumeOnlyOnISBEligibleMaps || state.currentRaidEligibleMap);
                        if (consume)
                        {
                            state.remainingRaids = Math.Max(0, state.remainingRaids - 1);
                        }
                    }
                }

                state.activeThisRaid = false;
                state.currentRaidIsScav = false;
                state.currentRaidEligibleMap = false;
                state.currentRaidToken = string.Empty;
                state.lastUpdatedUtc = DateTime.UtcNow;
                changed = true;
            }

            if (changed)
            {
                SaveStore();
            }
        }
    }

    private bool EvaluateRule(QuestTruceRuleConfig rule, ISBQuestTruceRuleState state, bool matched, ref bool changed)
    {
        var durationMode = NormalizeDurationMode(rule.truceDuration?.mode);
        if (!state.initialized)
        {
            state.initialized = true;
            state.lastStatusMatched = matched;
            state.lastUpdatedUtc = DateTime.UtcNow;
            changed = true;
            if (matched && rule.activateIfAlreadyMatched)
            {
                state.statusGateOpen = true;
                ActivatePersistentRule(rule, state, durationMode, ref changed);
            }
        }
        else
        {
            if (matched && !state.lastStatusMatched)
            {
                state.statusGateOpen = true;
                ActivatePersistentRule(rule, state, durationMode, ref changed);
            }

            if (state.lastStatusMatched != matched)
            {
                state.lastStatusMatched = matched;
                state.lastUpdatedUtc = DateTime.UtcNow;
                changed = true;
            }
        }

        return IsRuleActive(rule, state, matched);
    }

    private static void ActivatePersistentRule(QuestTruceRuleConfig rule, ISBQuestTruceRuleState state, string durationMode, ref bool changed)
    {
        if (durationMode == "whilestatusmatches" || state.activated)
        {
            return;
        }

        state.activated = true;
        if (durationMode == "infinite")
        {
            state.permanent = true;
        }
        else if (durationMode == "raids")
        {
            state.remainingRaids = Math.Max(1, rule.truceDuration?.raids ?? 1);
        }

        state.lastUpdatedUtc = DateTime.UtcNow;
        changed = true;
    }

    private static bool IsRuleActive(QuestTruceRuleConfig rule, ISBQuestTruceRuleState state, bool matched)
    {
        var durationMode = NormalizeDurationMode(rule.truceDuration?.mode);
        if (durationMode == "infinite")
        {
            return state.activated && state.permanent;
        }

        if (durationMode == "raids")
        {
            return state.activated && state.remainingRaids > 0;
        }

        return matched && state.statusGateOpen;
    }

    internal int? TryGetQuestStatus(string sessionId, string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
        {
            return null;
        }

        try
        {
            var profile = _profileHelper.GetPmcProfile(sessionId);
            var quests = GetMemberValue(profile, "Quests", "quests");
            if (quests is not IEnumerable enumerable)
            {
                return null;
            }

            foreach (var rawEntry in enumerable)
            {
                if (TryReadQuestEntry(rawEntry, out var foundId, out var status) && string.Equals(foundId, questId, StringComparison.OrdinalIgnoreCase))
                {
                    return status;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static bool TryReadQuestEntry(object? rawEntry, out string questId, out int status)
    {
        questId = string.Empty;
        status = 0;
        if (rawEntry == null)
        {
            return false;
        }

        if (TryReadQuestObject(rawEntry, null, out questId, out status))
        {
            return true;
        }

        var key = GetMemberValue(rawEntry, "Key", "key");
        var value = GetMemberValue(rawEntry, "Value", "value");
        return value != null && TryReadQuestObject(value, key, out questId, out status);
    }

    private static bool TryReadQuestObject(object questObject, object? fallbackId, out string questId, out int status)
    {
        questId = string.Empty;
        status = 0;
        var idValue = GetMemberValue(questObject, "QId", "Qid", "QID", "qid", "QuestId", "QuestID", "questId", "Id", "id") ?? fallbackId;
        var statusValue = GetMemberValue(questObject, "Status", "status");
        if (idValue == null || statusValue == null)
        {
            return false;
        }

        questId = Convert.ToString(idValue)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(questId))
        {
            return false;
        }

        try
        {
            status = Convert.ToInt32(statusValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? GetMemberValue(object? source, params string[] names)
    {
        if (source == null)
        {
            return null;
        }

        var type = source.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in names)
        {
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(source);
                }
                catch
                {
                }
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                try
                {
                    return field.GetValue(source);
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private bool IsISBEligibleMap(string? locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId))
        {
            return false;
        }

        var locations = _configController.ModConfig.locations;
        if (locations == null || locations.Count == 0)
        {
            return false;
        }

        var normalized = NormalizeMapId(locationId);
        return locations.Keys.Any(key => string.Equals(NormalizeMapId(key), normalized, StringComparison.Ordinal));
    }

    private static string NormalizeMapId(string value)
    {
        return new string (value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private QuestTruceConditionsConfig GetConfig()
    {
        return _configController.ModConfig.aishiLoyaltyCondition?.friendlyConditions?.questTruce ?? new QuestTruceConditionsConfig();
    }

    private static List<QuestTruceRuleConfig> GetValidRules(QuestTruceConditionsConfig config)
    {
        if (config.conditions == null)
        {
            return new List<QuestTruceRuleConfig>();
        }

        return config.conditions.Where(rule => rule != null && rule.enabled && !string.IsNullOrWhiteSpace(rule.questId) && GetStatuses(rule).Count > 0).ToList();
    }

    private static HashSet<int> GetStatuses(QuestTruceRuleConfig rule)
    {
        var statuses = new HashSet<int>();
        if (rule.questStatuses != null)
        {
            foreach (var status in rule.questStatuses)
            {
                statuses.Add(status);
            }
        }

        if (rule.questStatus.HasValue)
        {
            statuses.Add(rule.questStatus.Value);
        }

        return statuses;
    }

    private static string GetRuleKey(QuestTruceRuleConfig rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.id))
        {
            return rule.id.Trim();
        }

        var statuses = string.Join("-", GetStatuses(rule).OrderBy(value => value));
        return $"{rule.questId.Trim()}:{statuses}:{NormalizeDurationMode(rule.truceDuration?.mode)}";
    }

    private static string NormalizeDurationMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "infinite" => "infinite",
            "raids" => "raids",
            "raid" => "raids",
            "whilestatusmatches" => "whilestatusmatches",
            "status" => "whilestatusmatches",
            _ => "whilestatusmatches"
        };
    }

    private ISBQuestTruceProfileState GetOrCreateProfileState(string sessionId)
    {
        if (_store.profiles.TryGetValue(sessionId, out var state) && state != null)
        {
            state.rules ??= new Dictionary<string, ISBQuestTruceRuleState>(StringComparer.OrdinalIgnoreCase);
            return state;
        }

        state = new ISBQuestTruceProfileState();
        _store.profiles[sessionId] = state;
        return state;
    }

    private static ISBQuestTruceRuleState GetOrCreateRuleState(ISBQuestTruceProfileState profileState, string ruleKey, ref bool changed)
    {
        if (profileState.rules.TryGetValue(ruleKey, out var state) && state != null)
        {
            return state;
        }

        state = new ISBQuestTruceRuleState();
        profileState.rules[ruleKey] = state;
        changed = true;
        return state;
    }

    private ISBQuestTruceStore LoadStore()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new ISBQuestTruceStore();
            }

            var json = File.ReadAllText(_storePath);
            var loaded = JsonSerializer.Deserialize<ISBQuestTruceStore>(json, _jsonOptions) ?? new ISBQuestTruceStore();
            loaded.profiles = loaded.profiles == null ? new Dictionary<string, ISBQuestTruceProfileState>(StringComparer.Ordinal) : new Dictionary<string, ISBQuestTruceProfileState>(loaded.profiles, StringComparer.Ordinal);
            foreach (var profile in loaded.profiles.Values)
            {
                profile.rules = profile.rules == null ? new Dictionary<string, ISBQuestTruceRuleState>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, ISBQuestTruceRuleState>(profile.rules, StringComparer.OrdinalIgnoreCase);

            }

            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ISB QUEST TRUCE] Failed to load persistent state. Starting with an empty store: {ex.Message}");
            return new ISBQuestTruceStore();
        }
    }

    private void SaveStore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _storePath + ".tmp";
            var json = JsonSerializer.Serialize(_store, _jsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _storePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError($"[ISB QUEST TRUCE] Failed to save persistent state: {ex.Message}");
        }
    }

    private static string ShortId(string sessionId)
    {
        return sessionId.Length <= 8 ? sessionId : sessionId[..8];
    }
}
