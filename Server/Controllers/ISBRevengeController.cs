using SPTarkov.Server.Core.Helpers;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using System.Reflection;
using System.Text.Json;

namespace ISBSpecialForcesServer.Controllers;
public record ISBRevengeRaidStartRequest : IRequestData
{
    public string locationId { get; set; } = string.Empty;
    public bool isScavRaid { get; set; }
}

public record ISBRevengeRegisterRequest : IRequestData
{
    public string reason { get; set; } = "aishi_loyalty_broken";
    public int victimRole { get; set; }
}

public record ISBRevengeKillRequest : IRequestData
{
    public int victimRole { get; set; }
    public string victimId { get; set; } = string.Empty;
}

public sealed class ISBRevengeStatusResponse
{
    public bool enabled { get; set; }
    public bool active { get; set; }
    public int remainingRaids { get; set; }
    public bool isScavRaid { get; set; }
}

public sealed class ISBRevengeRecord
{
    public bool resetHostilityEachRaid { get; set; }
    public int remainingRaids { get; set; }
    public bool activeThisRaid { get; set; }
    public bool currentRaidIsScav { get; set; }
    public string revengeCycle { get; set; } = string.Empty;
    public string currentRaidToken { get; set; } = string.Empty;
    public string registeredRaidToken { get; set; } = string.Empty;
    public bool killedIsbThisRaid { get; set; }
    public List<string> amnestyQuestIds { get; set; } = [];
    public string activeAmnestyQuestId { get; set; } = string.Empty;
    public DateTime lastUpdatedUtc { get; set; }
}

public sealed class ISBRevengeStore
{
    public Dictionary<string, ISBRevengeRecord> profiles { get; set; } = new(StringComparer.Ordinal);
}

[Injectable(InjectionType.Singleton)]
public class ISBRevengeController
{
    private readonly object _sync = new();
    private readonly ConfigController _configController;
    private readonly ISBQuestTruceController _questTruceController;
    private readonly ILogger<ISBRevengeController> _logger;
    private readonly string _storePath;
    private readonly FikaRaidContext _fika;
    private readonly ProfileHelper _profiles;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private ISBRevengeStore _store;
    public ISBRevengeController(ModHelper modHelper, ConfigController configController, ISBQuestTruceController questTruceController, ILogger<ISBRevengeController> logger, FikaRaidContext fika, ProfileHelper profiles)
    {
        _configController = configController;
        _fika = fika;
        _profiles = profiles;
        _questTruceController = questTruceController;
        _logger = logger;
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _storePath = Path.Combine(modPath, "data", "revenge.json");
        _store = LoadStore();
    }

    public ISBRevengeStatusResponse BeginRaid(string sessionId, bool isScavRaid, string? locationId = null, string? raidToken = null)
    {
        var config = GetConfig();
        if (!config.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return DisabledStatus(isScavRaid);
        }

        var raidScopedTruce = !isScavRaid && _configController.ModConfig.aishiLoyaltyCondition.enabled && _questTruceController.Evaluate(sessionId, locationId).resetHostilityEachRaid;
        lock (_sync)
        {
            var record = GetOrCreateRecord(sessionId);
            if (!string.IsNullOrEmpty(raidToken) && record.currentRaidToken == raidToken)
                return BuildStatus(record, config.enabled);
            record.killedIsbThisRaid = false;
            record.currentRaidToken = string.IsNullOrEmpty(raidToken) ? Guid.NewGuid().ToString("N") : raidToken;
            record.registeredRaidToken = string.Empty;
            record.currentRaidIsScav = isScavRaid;
            record.resetHostilityEachRaid = raidScopedTruce;
            record.activeThisRaid = record.remainingRaids > 0 && !isScavRaid && !raidScopedTruce;
            record.lastUpdatedUtc = DateTime.UtcNow;
            SaveStore();
            if (config.logs)
            {
                _logger.LogInformation($"[ISB REVENGE] Raid started for {ShortId(sessionId)}. Active={record.activeThisRaid}, remaining={record.remainingRaids}, scav={isScavRaid}.");
            }

            return BuildStatus(record, config.enabled);
        }
    }

    public ISBRevengeStatusResponse GetStatus(string sessionId)
    {
        var config = GetConfig();
        if (!config.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return DisabledStatus(false);
        }

        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(sessionId, out var record) || record == null)
            {
                return new ISBRevengeStatusResponse
                {
                    enabled = true,
                    active = false,
                    remainingRaids = 0,
                    isScavRaid = false
                };
            }

            return BuildStatus(record, true);
        }
    }

    public ISBRevengeStatusResponse Register(string sessionId, ISBRevengeRegisterRequest? request)
    {
        var config = GetConfig();
        if (!config.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return DisabledStatus(false);
        }

        var raidAmount = Math.Max(0, config.raidAmount);
        if (raidAmount == 0)
        {
            return GetStatus(sessionId);
        }

        lock (_sync)
        {
            var record = GetOrCreateRecord(sessionId);
            if (record.resetHostilityEachRaid)
            {
                record.activeThisRaid = !record.currentRaidIsScav;
                record.lastUpdatedUtc = DateTime.UtcNow;
                SaveStore();
                return BuildStatus(record, true);
            }

            if (record.currentRaidIsScav || string.IsNullOrEmpty(record.currentRaidToken))
                return BuildStatus(record, true);
            if (request?.reason == "aishi_loyalty_broken" && record.registeredRaidToken == record.currentRaidToken)
                return BuildStatus(record, true);
            int previous = record.remainingRaids;
            if (config.stackAdditionalKills)
            {
                record.remainingRaids += raidAmount;
            }
            else if (config.refreshOnAdditionalKill)
            {
                record.remainingRaids = Math.Max(record.remainingRaids, raidAmount);
            }
            else if (record.remainingRaids <= 0)
            {
                record.remainingRaids = raidAmount;
            }

            if (previous <= 0 && record.remainingRaids > 0)
                record.revengeCycle = Guid.NewGuid().ToString("N");
            EnsureAmnestyQuest(record);
            record.registeredRaidToken = record.currentRaidToken;
            record.activeThisRaid = record.remainingRaids > 0 && !record.currentRaidIsScav;
            record.lastUpdatedUtc = DateTime.UtcNow;
            SaveStore();
            if (config.logs)
            {
                var reason = string.IsNullOrWhiteSpace(request?.reason) ? "unspecified" : request!.reason;
                _logger.LogInformation($"[ISB REVENGE] Registered for {ShortId(sessionId)}. Remaining {previous} -> {record.remainingRaids}; reason={reason}; victimRole={request?.victimRole ?? 0}.");
            }

            return BuildStatus(record, true);
        }
    }

    public void EndRaid(string sessionId)
    {
        var config = GetConfig();
        if (!config.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(sessionId, out var record) || record == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(record.currentRaidToken))
            {
                return;
            }

            var registeredDuringThisRaid = string.Equals(record.registeredRaidToken, record.currentRaidToken, StringComparison.Ordinal);
            var previous = record.remainingRaids;
            var raidScoped = record.resetHostilityEachRaid;
            var shouldConsume = !record.resetHostilityEachRaid && record.remainingRaids > 0 && !registeredDuringThisRaid && (!record.currentRaidIsScav || config.consumeOnScavRaid);
            if (shouldConsume)
            {
                record.remainingRaids = Math.Max(0, record.remainingRaids - 1);
            }

            record.activeThisRaid = false;
            record.resetHostilityEachRaid = false;
            record.currentRaidIsScav = false;
            record.currentRaidToken = string.Empty;
            record.registeredRaidToken = string.Empty;
            record.lastUpdatedUtc = DateTime.UtcNow;
            if (record.remainingRaids <= 0 && record.amnestyQuestIds.Count == 0)
            {
                _store.profiles.Remove(sessionId);
            }

            SaveStore();
            if (config.logs && (previous > 0 || raidScoped))
            {
                var result = raidScoped ? "raid-scoped hostility cleared" : registeredDuringThisRaid ? "not consumed because it was registered/refreshed in this raid" : shouldConsume ? $"consumed: {previous} -> {Math.Max(0, previous - 1)}" : $"not consumed (scav raid, remaining={previous})";
                _logger.LogInformation($"[ISB REVENGE] Raid ended for {ShortId(sessionId)}; {result}.");
            }
        }
    }

    public void RegisterKill(string sessionId, ISBRevengeKillRequest? request)
    {
        if (!GetConfig().enabled || request == null || request.victimRole < 13701 || request.victimRole > 13716 || request.victimRole is 13707 or 13708 || string.IsNullOrWhiteSpace(request.victimId))
            return;
        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(sessionId, out var record) || record.currentRaidIsScav || string.IsNullOrEmpty(record.currentRaidToken) || record.killedIsbThisRaid)
                return;
            record.killedIsbThisRaid = true;
            EnsureAmnestyQuest(record);
            SaveStore();
        }
    }

    private static void EnsureAmnestyQuest(ISBRevengeRecord record)
    {
        if (!record.killedIsbThisRaid || record.remainingRaids <= 0 || record.resetHostilityEachRaid || !string.IsNullOrEmpty(record.activeAmnestyQuestId))
            return;
        record.activeAmnestyQuestId = "495342" + Guid.NewGuid().ToString("N")[..18];
        record.amnestyQuestIds.Add(record.activeAmnestyQuestId);
    }

    public (string[] quests, string active, bool pending) GetAmnestyState(string sessionId)
    {
        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(sessionId, out var record))
                return ([], string.Empty, false);
            return (record.amnestyQuestIds.ToArray(), record.activeAmnestyQuestId, GetConfig().enabled && record.remainingRaids > 0);
        }
    }

    public string? GetAmnestyOwner(string questId)
    {
        lock (_sync)
            return _store.profiles.FirstOrDefault(pair => pair.Value.amnestyQuestIds.Contains(questId)).Key;
    }

    public bool ClearForAmnesty(string sessionId, string questId)
    {
        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(sessionId, out var record) || record.activeAmnestyQuestId != questId)
                return false;
            record.remainingRaids = 0;
            record.activeThisRaid = false;
            record.activeAmnestyQuestId = string.Empty;
            record.registeredRaidToken = string.Empty;
            record.lastUpdatedUtc = DateTime.UtcNow;
            SaveStore();
            return true;
        }
    }

    public string GetCycle(string session)
    {
        if (!GetConfig().enabled)
            return string.Empty;
        lock (_sync)
        {
            if (!_store.profiles.TryGetValue(session, out var record) || record.remainingRaids <= 0)
                return string.Empty;
            if (string.IsNullOrEmpty(record.revengeCycle))
            {
                record.revengeCycle = Guid.NewGuid().ToString("N");
                SaveStore();
            }

            return record.revengeCycle;
        }
    }

    public string GetSpawnCycle(string raid, string map)
    {
        string[] sessions;
        lock (_sync)
            sessions = _store.profiles.Keys.ToArray();
        return string.Join("|", sessions.Where(s => _fika.EventSession(s) == raid && !_questTruceController.Evaluate(s, map).resetHostilityEachRaid).OrderBy(s => s, StringComparer.Ordinal).Select(GetCycle).Where(cycle => !string.IsNullOrEmpty(cycle)));
    }

    public string[] GetRaidTargets(string raid)
    {
        if (!GetConfig().enabled)
            return[];
        string[] sessions;
        lock (_sync)
            sessions = _store.profiles.Where(p => p.Value.activeThisRaid && !p.Value.currentRaidIsScav && (p.Value.remainingRaids > 0 || p.Value.resetHostilityEachRaid)).Select(p => p.Key).ToArray();
        return sessions.Where(s => _fika.EventSession(s) == raid).Select(s => _profiles.GetPmcProfile(s)?.Id.ToString()).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).Distinct().ToArray();
    }

    private RevengeConfig GetConfig()
    {
        return _configController.ModConfig.revenge ?? new RevengeConfig();
    }

    private ISBRevengeRecord GetOrCreateRecord(string sessionId)
    {
        if (_store.profiles.TryGetValue(sessionId, out var record) && record != null)
        {
            return record;
        }

        record = new ISBRevengeRecord();
        _store.profiles[sessionId] = record;
        return record;
    }

    private static ISBRevengeStatusResponse BuildStatus(ISBRevengeRecord record, bool enabled)
    {
        return new ISBRevengeStatusResponse
        {
            enabled = enabled,
            active = enabled && record.activeThisRaid && (record.resetHostilityEachRaid || record.remainingRaids > 0),
            remainingRaids = Math.Max(0, record.remainingRaids),
            isScavRaid = record.currentRaidIsScav
        };
    }

    private static ISBRevengeStatusResponse DisabledStatus(bool isScavRaid)
    {
        return new ISBRevengeStatusResponse
        {
            enabled = false,
            active = false,
            remainingRaids = 0,
            isScavRaid = isScavRaid
        };
    }

    private ISBRevengeStore LoadStore()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new ISBRevengeStore();
            }

            var json = File.ReadAllText(_storePath);
            var loaded = JsonSerializer.Deserialize<ISBRevengeStore>(json, _jsonOptions) ?? new ISBRevengeStore();
            loaded.profiles = loaded.profiles == null ? new Dictionary<string, ISBRevengeRecord>(StringComparer.Ordinal) : new Dictionary<string, ISBRevengeRecord>(loaded.profiles, StringComparer.Ordinal);
            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[ISB REVENGE] Failed to load persistent state. Starting with an empty store: {ex.Message}");
            return new ISBRevengeStore();
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
            _logger.LogError($"[ISB REVENGE] Failed to save persistent state: {ex.Message}");
        }
    }

    private static string ShortId(string sessionId)
    {
        if (sessionId.Length <= 8)
        {
            return sessionId;
        }

        return sessionId[..8];
    }
}
