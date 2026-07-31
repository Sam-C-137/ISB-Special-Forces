using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using System.Reflection;
using System.Text.Json;

namespace ISBSpecialForcesServer.Controllers;

public record ISBRevengeRaidStartRequest : IRequestData
{
    public bool isScavRaid { get; set; }
}

public record ISBRevengeRegisterRequest : IRequestData
{
    public string reason { get; set; } = "aishi_loyalty_broken";
    public int victimRole { get; set; }
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
    public int remainingRaids { get; set; }
    public bool activeThisRaid { get; set; }
    public bool currentRaidIsScav { get; set; }
    public string currentRaidToken { get; set; } = string.Empty;
    public string registeredRaidToken { get; set; } = string.Empty;
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
    private readonly ILogger<ISBRevengeController> _logger;
    private readonly string _storePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private ISBRevengeStore _store;

    public ISBRevengeController(
        ModHelper modHelper,
        ConfigController configController,
        ILogger<ISBRevengeController> logger)
    {
        _configController = configController;
        _logger = logger;

        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _storePath = Path.Combine(modPath, "data", "revenge.json");
        _store = LoadStore();
    }

    public ISBRevengeStatusResponse BeginRaid(string sessionId, bool isScavRaid)
    {
        var config = GetConfig();

        if (!config.enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return DisabledStatus(isScavRaid);
        }

        lock (_sync)
        {
            var record = GetOrCreateRecord(sessionId);

            record.currentRaidToken = Guid.NewGuid().ToString("N");
            record.registeredRaidToken = string.Empty;
            record.currentRaidIsScav = isScavRaid;
            record.activeThisRaid = record.remainingRaids > 0 && !isScavRaid;
            record.lastUpdatedUtc = DateTime.UtcNow;

            SaveStore();

            if (config.logs && record.remainingRaids > 0)
            {
                _logger.LogInformation(
                    $"[ISB REVENGE] Raid started for {ShortId(sessionId)}. Active={record.activeThisRaid}, remaining={record.remainingRaids}, scav={isScavRaid}.");
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
            var previous = record.remainingRaids;

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

            record.registeredRaidToken = record.currentRaidToken;
            record.activeThisRaid = record.remainingRaids > 0 && !record.currentRaidIsScav;
            record.lastUpdatedUtc = DateTime.UtcNow;

            SaveStore();

            if (config.logs)
            {
                var reason = string.IsNullOrWhiteSpace(request?.reason)
                    ? "unspecified"
                    : request!.reason;

                _logger.LogInformation(
                    $"[ISB REVENGE] Registered for {ShortId(sessionId)}. Remaining {previous} -> {record.remainingRaids}; reason={reason}; victimRole={request?.victimRole ?? 0}.");
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

            var registeredDuringThisRaid = string.Equals(
                record.registeredRaidToken,
                record.currentRaidToken,
                StringComparison.Ordinal);

            var shouldConsume = record.remainingRaids > 0
                && !registeredDuringThisRaid
                && (!record.currentRaidIsScav || config.consumeOnScavRaid);

            var previous = record.remainingRaids;

            if (shouldConsume)
            {
                record.remainingRaids = Math.Max(0, record.remainingRaids - 1);
            }

            record.activeThisRaid = false;
            record.currentRaidIsScav = false;
            record.currentRaidToken = string.Empty;
            record.registeredRaidToken = string.Empty;
            record.lastUpdatedUtc = DateTime.UtcNow;

            if (record.remainingRaids <= 0)
            {
                _store.profiles.Remove(sessionId);
            }

            SaveStore();

            if (config.logs && previous > 0)
            {
                var result = registeredDuringThisRaid
                    ? "not consumed because it was registered/refreshed in this raid"
                    : shouldConsume
                        ? $"consumed: {previous} -> {Math.Max(0, previous - 1)}"
                        : $"not consumed (scav raid, remaining={previous})";

                _logger.LogInformation($"[ISB REVENGE] Raid ended for {ShortId(sessionId)}; {result}.");
            }
        }
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
            active = enabled && record.activeThisRaid && record.remainingRaids > 0,
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

            loaded.profiles = loaded.profiles == null
                ? new Dictionary<string, ISBRevengeRecord>(StringComparer.Ordinal)
                : new Dictionary<string, ISBRevengeRecord>(loaded.profiles, StringComparer.Ordinal);

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
