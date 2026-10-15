using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
#if ISB_SPT41
using SPTarkov.Server.Core.Helpers.Server;
#endif
using IOPath = System.IO.Path;

namespace ISBSpecialForcesServer.Controllers;
public sealed class SpawnProgressRequest : IRequestData
{
    public string token { get; set; } = string.Empty;
    public bool started { get; set; }
    public bool isbSpawned { get; set; }
    public bool containmentOccurred { get; set; }
    public bool blackoutOccurred { get; set; }
    public bool scp173Spawned { get; set; }
    public double blackoutBaseChance { get; set; } = -1;
}

public sealed class SpawnProgressResponse
{
    public string token { get; set; } = string.Empty;
    public bool blackoutSelected { get; set; }
    public bool blackoutReady { get; set; }
    public string[] revengeTargets { get; set; } = [];
}

[Injectable(InjectionType.Singleton)]
public sealed class ISBSpawnProgressController
{
    private sealed class SpawnBaseline(double chance)
    {
        public double Chance { get; } = chance;
    }
    private readonly ConditionalWeakTable<BossLocationSpawn, SpawnBaseline> _baselines = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, SpawnChanceRaid> _raids = new(StringComparer.Ordinal);
    private readonly FikaRaidContext _fika;
    private readonly ISBRevengeController _revenge;
    private readonly ISBQuestTruceController _truce;
    private readonly ILogger<ISBSpawnProgressController> _logger;
    private readonly string _path;
    private SpawnChanceLedger _ledger;
    public ISBSpawnProgressController(ModHelper mods, FikaRaidContext fika, ISBRevengeController revenge, ISBQuestTruceController truce, ILogger<ISBSpawnProgressController> logger)
    {
        _fika = fika;
        _revenge = revenge;
        _truce = truce;
        _logger = logger;
        _path = IOPath.Combine(mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "data", "spawn-progression.json");
        try
        {
            _ledger = File.Exists(_path) ? JsonSerializer.Deserialize<SpawnChanceLedger>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ISB: could not load spawn progression.");
            _ledger = new();
        }
    }

    private string Raid(string session) => _fika.EventSession(session) ?? session;
    public string CurrentToken(string session)
    {
        lock (_sync)
            return _raids.TryGetValue(Raid(session), out var raid) ? raid.token : string.Empty;
    }

    public void Prepare(string? session, string map, bool scav)
    {
        if (string.IsNullOrWhiteSpace(session))
            return;
        string raid = Raid(session), owner = _fika.QuestOwner(raid) ?? session;
        if (scav)
        {
            lock (_sync)
                _raids.Remove(raid);
            return;
        }

        var cycle = _revenge.GetSpawnCycle(raid, map);
        lock (_sync)
        {
            if (_raids.TryGetValue(raid, out var existing) && existing.map == map)
                return;
            _raids[raid] = new()
            {
                owner = owner,
                map = map,
                revengeCycle = cycle
            };
            _ledger.Profile(owner, cycle);
        }
    }

    public double ContainmentChance(string? session, double baseline, bool eligible)
    {
        if (string.IsNullOrWhiteSpace(session))
            return baseline;
        lock (_sync)
        {
            if (!_raids.TryGetValue(Raid(session), out var raid))
                return baseline;
            raid.containmentEligible |= eligible && baseline > 0;
            return SpawnChanceLedger.Chance(baseline, _ledger.Profile(raid.owner, raid.revengeCycle).containment, 10);
        }
    }

    public double Scp173Chance(string? session, double baseline, bool enabled, string[] maps)
    {
        if (string.IsNullOrWhiteSpace(session))
            return baseline;
        lock (_sync)
        {
            if (!_raids.TryGetValue(Raid(session), out var raid) || !enabled || baseline <= 0 || !maps.Contains(raid.map, StringComparer.OrdinalIgnoreCase))
                return baseline;
            raid.scp173Eligible = true;
            return SpawnChanceLedger.Chance(baseline, _ledger.Profile(raid.owner, raid.revengeCycle).scp173, 5);
        }
    }

    public void Apply(string session, string map, LocationBase location)
    {
        lock (_sync)
        {
            if (!_raids.TryGetValue(Raid(session), out var raid) || raid.map != map || location.BossLocationSpawn == null)
                return;
            foreach (var spawn in location.BossLocationSpawn)
            {
                if (spawn.BossName?.StartsWith("ISB", StringComparison.OrdinalIgnoreCase) != true || spawn.BossChance <= 0)
                    continue;
                if (!string.IsNullOrEmpty(spawn.TriggerId) && spawn.TriggerId != "hunt" && !spawn.TriggerId.StartsWith("isb_config_checkpoint_", StringComparison.Ordinal))
                    continue;
                raid.isbEligible = true;
                var baseline = _baselines.GetValue(spawn, wave => new SpawnBaseline(wave.BossChance ?? 0));
                spawn.BossChance = (float)_ledger.IsbChance(raid, baseline.Chance);
            }
        }
    }

    public SpawnProgressResponse Update(string session, SpawnProgressRequest? request)
    {
        var targets = _revenge.GetRaidTargets(Raid(session));
        lock (_sync)
        {
            if (!_raids.TryGetValue(Raid(session), out var raid))
                return new()
                {
                    revengeTargets = targets
                };
            if (request != null && request.token == raid.token)
            {
                raid.started |= request.started;
                raid.isbSpawned |= request.isbSpawned;
                raid.containmentOccurred |= request.containmentOccurred;
                raid.blackoutOccurred |= request.blackoutOccurred;
                raid.scp173Spawned |= request.scp173Spawned;
                if (raid.map == "laboratory" && request.blackoutBaseChance >= 0 && request.blackoutBaseChance <= 100)
                {
                    raid.blackoutEligible |= request.blackoutBaseChance > 0;
                    raid.blackoutSelected ??= Random.Shared.NextDouble() * 100 < SpawnChanceLedger.Chance(request.blackoutBaseChance, _ledger.Profile(raid.owner, raid.revengeCycle).blackout, 10);
                }
            }

            return new()
            {
                token = raid.token,
                blackoutReady = raid.blackoutSelected.HasValue,
                blackoutSelected = raid.blackoutSelected == true,
                revengeTargets = targets
            };
        }
    }

    public void Finish(string session)
    {
        var key = Raid(session);
        if (key != session && _fika.QuestOwner(key) != session)
            return;
        lock (_sync)
        {
            if (!_raids.Remove(key, out var raid) || !raid.started)
                return;
            _ledger.Finish(raid);
            _ledger.Profile(raid.owner, _revenge.GetSpawnCycle(key, raid.map));
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(_path)!);
                File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_ledger, new JsonSerializerOptions { WriteIndented = true }));
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(_path + ".tmp", _path, true);
                        break;
                    }
                    catch (Exception error) when (attempt < 3 && (error is IOException or UnauthorizedAccessException))
                    {
                        Thread.Sleep(25 * (attempt + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ISB: could not save spawn progression.");
            }
        }
    }

    public void CancelUnstarted(string session)
    {
        lock (_sync)
            if (_raids.TryGetValue(Raid(session), out var raid) && !raid.started)
                _raids.Remove(Raid(session));
    }
}

#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
#else
[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
#endif
public sealed class ISBSpawnProgressRouter(JsonUtil json, HttpResponseUtil http, ISBSpawnProgressController progress) : StaticRouter(json, [new RouteAction("/ISB/spawn/progress", 
#if ISB_SPT41
(url, info, session, output, cancellationToken) => 
#else
            (url, info, session, output) =>
#endif
new ValueTask<object>(http.NoBody(progress.Update(session, null)))), new RouteAction<SpawnProgressRequest>("/ISB/spawn/report", 
#if ISB_SPT41
(url, info, session, output, cancellationToken) => 
#else
            (url, info, session, output) =>
#endif
new ValueTask<string>(http.NoBody(progress.Update(session, info))))]);
