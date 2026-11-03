using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using ISB.Compatibility;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBEventPopulationQueue : MonoBehaviour
    {
        private sealed class State
        {
            public bool active;
            public bool chaosInsurgencyOnly;
            public EventPopulationConfig containementBreachQueue;
            public EventPopulationConfig blackoutQueue;
            public bool[] populationFactions;
        }
        private sealed class Reservation
        {
            public BossLocationSpawn Wave;
            public int[] Remaining;
            public bool Special;
            public bool Expired;
            public float Deadline;
            public readonly HashSet<BotCreationData> Creations = new HashSet<BotCreationData>();
            public readonly HashSet<string> Registered = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Profiles = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        private sealed class Job
        {
            public BotBossSpawn BossSpawner;
            public BossLocationSpawn Wave;
            public BotSpawnParams Parameters;
            public BotSpawner Spawner;
            public BotZone Zone;
            public BotCreationData Data;
            public Action<BotOwner> Callback;
            public CancellationToken Token;
            public List<ISpawnPoint> Points;
            public int[] Counts;
            public bool Special;
            public bool Generic;
            public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private static ISBEventPopulationQueue _instance;
        private static readonly MethodInfo Activation = AccessTools.Method(typeof(BotSpawner), "method_7", new[] { typeof(List<ISpawnPoint>), typeof(BotZone), typeof(BotCreationData), typeof(Action<BotOwner>), typeof(CancellationToken) });
        private static readonly FieldInfo ProfileData = AccessTools.Field(typeof(BotCreationData), "_profileData");
        private readonly LinkedList<Job> _jobs = new LinkedList<Job>();
        private readonly Dictionary<BotSpawnParams, Reservation> _reservations = new Dictionary<BotSpawnParams, Reservation>();
        private readonly HashSet<string> _specialProfiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _aliveIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly int[] _current = new int[6];
        private readonly Dictionary<int, BossLocationSpawn> _templates = new Dictionary<int, BossLocationSpawn>();
        private GameWorld _world;
        private BotBossSpawn _bossSpawner;
        private State _state;
        private bool _known, _fetching, _waveBypass, _activationBypass, _reported, _started, _wedgeDispatched;
        private int _generation;
        private float _nextFetch, _nextPump, _nextGeneric, _startedAt;
        private int _global;
        private EventPopulationConfig Config => _state == null ? null : _state.active ? _state.containementBreachQueue : _state.blackoutQueue;
        private bool Active => _known && Config?.enabled == true && (_state.active || _started);
        private bool ChaosOnly => _known && _state?.active == true && _state.chaosInsurgencyOnly;

        private void Awake()
        {
            _instance = this;
            if (Activation == null) throw new MissingMethodException("ISB: native bot activation boundary is missing.");
            var harmony = new Harmony("com.samc137.isb.eventpopulation");
            harmony.Patch(Activation, prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(GateActivation)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(BotBossSpawn), "Spawn"), prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(GateWave)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(GlobalEventDispatcher), "AnyEvent"), prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(EventStarted)));
        }
        private static int Faction(WildSpawnType role)
        {
            int value = (int)role;
            if (value == GhostBotVariant.ChaosValue) return 2;
            if (value >= 13701 && value <= 13716) return 1;
            if (value >= 848420 && value <= 848426) return 0;
            if (role == WildSpawnType.pmcBot) return 3;
            if (role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR) return 4;
            return 5;
        }
        private static bool NamedSpecial(WildSpawnType role)
        {
            int value = (int)role;
            return value >= 13702 && value <= 13706 || value == 13715 || value == 13716 || value == 848424 || value == 848426;
        }
        private static int[] WaveCounts(BossLocationSpawn wave)
        {
            var counts = new int[6];
            counts[Faction(wave.BossType)]++;
            var escorts = wave.GetEscors();
            if (escorts != null)
                foreach (var escort in escorts) counts[Faction(escort.BossEscortType)] += escort.BossEscortAmount;
            else counts[Faction(wave.EscortType)] += wave.EscortCount;
            return counts;
        }
        private static bool SpecialWave(BossLocationSpawn wave) => NamedSpecial(wave.BossType) || GhostBotSpawnBridge.IsSpecialWave(wave) ||
            (wave.GetEscors()?.Any(e => NamedSpecial(e.BossEscortType)) ?? NamedSpecial(wave.EscortType));
        private static bool HasWedge(Job job) => job.Wave != null
            ? ContainmentWedgeLimit.IsWedge(job.Wave.BossName) || ContainmentWedgeLimit.IsWedge(job.Wave.BossEscortType)
                || job.Wave.GetEscors()?.Any(e => ContainmentWedgeLimit.IsWedge(e.BossEscortType.ToString())) == true
            : job.Data?.Profiles.Any(p => (int)p.Info.Settings.Role == 848424) == true;
        private bool Context()
        {
            if (!ISBFikaAuthority.IsAiAuthority) return false;
            var world = Singleton<GameWorld>.Instance;
            if (!ReferenceEquals(world, _world)) Reset(world);
            return world != null && string.Equals(world.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase);
        }
        private static bool GateWave(BotBossSpawn __instance, BossLocationSpawn wave, BotSpawnParams spawnParams, BotCreationData data, ref Task __result)
        {
            var q = _instance;
            if (q == null || q._waveBypass || wave == null || !q.Context()) return true;
            if (q.ChaosOnly && !EventPopulationPolicy.IsChaosOnly(WaveCounts(wave)))
            {
                data?.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            q._bossSpawner = __instance;
            if (data != null && spawnParams != null && q._reservations.TryGetValue(spawnParams, out var reservation) && ReferenceEquals(reservation.Wave, wave))
            {
                if (!reservation.Expired) return true;
                data?.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            if (q._known && !q.Active) return true;
            var existing = q._jobs.FirstOrDefault(job => ReferenceEquals(job.Wave, wave));
            if (existing != null)
            {
                if (data != null && !ReferenceEquals(data, existing.Data)) data.StopSpawn();
                __result = existing.Done.Task;
                return false;
            }
            var parameters = new BotSpawnParams { Id_spawn = spawnParams?.Id_spawn, TriggerType = spawnParams?.TriggerType ?? default, ShallBeGroup = spawnParams?.ShallBeGroup };
            if (data != null)
            {
                if (ProfileData?.GetValue(data) is GetProfileDataParams nativeData) nativeData.SpawnParams = parameters;
                else parameters = data.SpawnParams ?? parameters;
            }
            var job = new Job { BossSpawner = __instance, Wave = wave, Parameters = parameters, Data = data,
                Spawner = __instance._spawner, Token = __instance._spawner.GetCancelToken(), Counts = WaveCounts(wave), Special = SpecialWave(wave) };
            q._jobs.AddLast(job);
            __result = job.Done.Task;
            return false;
        }
        private static bool GateActivation(BotSpawner __instance, List<ISpawnPoint> spawnPoints, BotZone botZone, BotCreationData data, Action<BotOwner> callback, CancellationToken cancellationToken, ref Task __result)
        {
            var q = _instance;
            if (q == null || q._activationBypass || data == null || !q.Context()) return true;
            if (q.ChaosOnly && data.Profiles.Any(p => Faction(p.Info.Settings.Role) != 2))
            {
                data.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            if (data.SpawnParams != null && q._reservations.TryGetValue(data.SpawnParams, out var reservation))
            {
                if (reservation.Expired) { data.StopSpawn(); __result = Task.CompletedTask; return false; }
                q.Register(reservation, data);
                __result = q.ActivateReserved(__instance, spawnPoints, botZone, data, callback, cancellationToken, reservation);
                return false;
            }
            if (q._known && !q.Active) return true;
            var counts = new int[6];
            foreach (var profile in data.Profiles) counts[Faction(profile.Info.Settings.Role)]++;
            var job = new Job { Spawner = __instance, Zone = botZone, Data = data, Parameters = data.SpawnParams ?? new BotSpawnParams(), Callback = callback,
                Token = cancellationToken, Points = new List<ISpawnPoint>(spawnPoints), Counts = counts, Special = data.Profiles.Any(p => NamedSpecial(p.Info.Settings.Role)) };
            q._jobs.AddLast(job);
            spawnPoints.Clear(); __result = job.Done.Task;
            return false;
        }
        private Task ActivateReserved(BotSpawner spawner, List<ISpawnPoint> points, BotZone zone, BotCreationData data, Action<BotOwner> callback, CancellationToken token, Reservation reservation)
        {
            try
            {
                _activationBypass = true;
                return ObserveActivation((Task)Activation.Invoke(spawner, new object[] { points, zone, data, callback, token }), data, reservation, _generation);
            }
            catch (Exception error)
            {
                Expire(reservation);
                Retire(reservation, data);
                data.StopSpawn();
                return Task.FromException(error.GetBaseException());
            }
            finally { _activationBypass = false; }
        }
        private async Task ObserveActivation(Task task, BotCreationData data, Reservation reservation, int generation)
        {
            try { await task; }
            catch
            {
                if (generation == _generation) { Expire(reservation); Retire(reservation, data); }
                throw;
            }
            finally
            {
                if (generation == _generation)
                {
                    reservation.Creations.Remove(data);
                    if (reservation.Expired) Retire(reservation, data);
                }
            }
        }
        private static void Retire(Reservation reservation, BotCreationData data)
        {
            foreach (var profile in data.Profiles) reservation.Profiles.Remove(profile.Id);
        }
        private static void Expire(Reservation reservation)
        {
            if (reservation.Expired) return;
            reservation.Expired = true;
            Array.Clear(reservation.Remaining, 0, reservation.Remaining.Length);
            foreach (var creation in reservation.Creations.ToArray()) creation.StopSpawn();
        }
        private void Register(Reservation reservation, BotCreationData data)
        {
            reservation.Creations.Add(data);
            foreach (var profile in data.Profiles)
            {
                RegisterProfile(reservation, profile.Id, Faction(profile.Info.Settings.Role));
            }
        }
        private void RegisterProfile(Reservation reservation, string id, int faction)
        {
            if (!reservation.Registered.Add(id)) return;
            reservation.Remaining[faction] = Math.Max(0, reservation.Remaining[faction] - 1);
            reservation.Profiles[id] = faction;
            if (reservation.Special) _specialProfiles.Add(id);
        }
        private static void EventStarted(string __0)
        {
            var q = _instance;
            if (q == null || string.IsNullOrEmpty(__0) || !__0.StartsWith("isb_labs_blackout_", StringComparison.Ordinal) || !q.Context()) return;
            if (!q._started) { q._started = true; q._startedAt = Time.unscaledTime; q._nextGeneric = q._startedAt + (q.Config?.genericRefillDelaySeconds ?? 15); }
        }
        private void Reset(GameWorld world)
        {
            _generation++;
            foreach (var job in _jobs) { job.Data?.StopSpawn(); job.Done.TrySetCanceled(); }
            _jobs.Clear(); _reservations.Clear(); _specialProfiles.Clear(); _templates.Clear(); _aliveIds.Clear();
            _world = world; _bossSpawner = null; _state = null;
            _known = _fetching = _reported = _started = _wedgeDispatched = false;
            _nextFetch = _nextPump = _nextGeneric = _startedAt = 0;
        }
        private void Update()
        {
            if (!Context()) return;
            if (!_known && !_fetching && Time.unscaledTime >= _nextFetch) { _fetching = true; _ = Fetch(_generation); }
            if (!_known || Time.unscaledTime < _nextPump) return;
            _nextPump = Time.unscaledTime + .5f;
            var game = Singleton<IBotGame>.Instance;
            if (game == null || game.Status != GameStatus.Started && game.Status != GameStatus.Running) return;
            if (_state.active && !_started) { _started = true; _startedAt = Time.unscaledTime; _nextGeneric = _startedAt + Config.genericRefillDelaySeconds; }
            if (_bossSpawner == null && Active) _bossSpawner = game.BotsController?.BotSpawner?.BossSpawner;
            if (_world.AllAlivePlayersList == null) return;
            if (ChaosOnly)
                for (var node = _jobs.First; node != null;)
                {
                    var next = node.Next;
                    if (!EventPopulationPolicy.IsChaosOnly(node.Value.Counts))
                    {
                        _jobs.Remove(node); node.Value.Data?.StopSpawn(); node.Value.Done.TrySetResult(false);
                    }
                    node = next;
                }
            Count();
            bool specialWaiting = Active && _jobs.Any(j => j.Special && j.Counts.Sum() <= Config.Ceiling && !j.Token.IsCancellationRequested);
            var targets = Config?.Targets(Time.unscaledTime - _startedAt) ?? new int[6];
            if (_state.populationFactions != null)
                for (int i = 0; i < Math.Min(5, _state.populationFactions.Length); i++) if (!_state.populationFactions[i]) targets[i] = 0;
            if (Active && !specialWaiting && Time.unscaledTime >= _nextGeneric && _bossSpawner != null)
                Refill(targets);
            var ordered = _jobs.ToArray().OrderByDescending(j => j.Special).ThenByDescending(j => Score(j.Counts, targets));
            foreach (var job in ordered)
            {
                if (job.Token.IsCancellationRequested || job.Data?.SpawnStopped == true)
                { _jobs.Remove(job); job.Data?.StopSpawn(); job.Done.TrySetCanceled(); continue; }
                if (Active && job.Counts.Sum() > Config.Ceiling)
                {
                    _jobs.Remove(job); Failed(job, null, new InvalidOperationException("A formation exceeds the complete raid spawn limit.")); continue;
                }
                if (Active && !EventPopulationPolicy.CanAdmit(_global, _current, job.Counts, targets, Config, job.Special, specialWaiting)) continue;
                _jobs.Remove(job); Dispatch(job); break;
            }
        }
        private int Score(int[] counts, int[] targets)
        {
            int value = 0;
            for (int i = 0; i < 5; i++) if (counts[i] > 0) value = Math.Max(value, targets[i] - _current[i] + (i >= 3 && targets[i] > 0 ? 100 : 0));
            return value;
        }
        private void Count()
        {
            Array.Clear(_current, 0, _current.Length); _global = 0; _aliveIds.Clear();
            foreach (var player in _world.AllAlivePlayersList)
                if (player != null && player.IsAI)
                {
                    var parameters = player.AIData?.BotOwner?.SpawnProfileData?.SpawnParams;
                    if (parameters != null && _reservations.TryGetValue(parameters, out var reservation))
                        RegisterProfile(reservation, player.ProfileId, Faction(player.Profile.Info.Settings.Role));
                    _aliveIds.Add(player.ProfileId); _global++;
                    if (!_specialProfiles.Contains(player.ProfileId) && !NamedSpecial(player.Profile.Info.Settings.Role)) _current[Faction(player.Profile.Info.Settings.Role)]++;
                }
            foreach (var reservation in _reservations.Values)
            {
                if (!reservation.Expired && Time.unscaledTime >= reservation.Deadline) Expire(reservation);
                if (reservation.Expired && reservation.Profiles.Count == 0) continue;
                if (reservation.Profiles.Count > 0)
                    foreach (var id in reservation.Profiles.Keys.Where(_aliveIds.Contains).ToArray()) reservation.Profiles.Remove(id);
                for (int i = 0; i < 6; i++) { _global += reservation.Remaining[i]; if (!reservation.Special) _current[i] += reservation.Remaining[i]; }
                foreach (var profile in reservation.Profiles)
                    if (!_aliveIds.Contains(profile.Key)) { _global++; if (!reservation.Special) _current[profile.Value]++; }
            }
        }
        private void Dispatch(Job job)
        {
            if (_state?.active == true && HasWedge(job))
            {
                if (_wedgeDispatched)
                {
                    job.Data?.StopSpawn();
                    job.Done.TrySetResult(false);
                    return;
                }
                _wedgeDispatched = true;
            }
            int generation = _generation;
            Reservation reservation = null;
            if (Active)
            {
                reservation = new Reservation { Wave = job.Wave, Remaining = (int[])job.Counts.Clone(), Special = job.Special, Deadline = Time.unscaledTime + 180f };
                _reservations[job.Parameters] = reservation;
                _global += job.Counts.Sum();
                if (!job.Special) for (int i = 0; i < 6; i++) _current[i] += job.Counts[i];
                job.Spawner.SetMaxBots(Config.Ceiling);
            }
            try
            {
                Task task;
                _waveBypass = true;
                if (job.Wave != null)
                {
                    if (Active) job.Wave.IgnoreMaxBots = true;
                    if (!job.Special && !job.Generic && job.Wave.TriggerId?.IndexOf("checkpoint", StringComparison.OrdinalIgnoreCase) < 0 && job.Counts.Take(5).Count(c => c > 0) == 1 && job.Counts[5] == 0)
                        _templates[Array.FindIndex(job.Counts, c => c > 0)] = job.Wave;
                    task = job.BossSpawner.Spawn(job.Wave, job.Parameters, job.Data);
                }
                else
                {
                    if (reservation != null) Register(reservation, job.Data);
                    _activationBypass = true;
                    task = (Task)Activation.Invoke(job.Spawner, new object[] { job.Points, job.Zone, job.Data, job.Callback, job.Token });
                }
                _ = Observe(task, job, reservation, generation);
            }
            catch (Exception error) { Failed(job, reservation, error.GetBaseException()); }
            finally { _waveBypass = _activationBypass = false; }
        }
        private async Task Observe(Task task, Job job, Reservation reservation, int generation)
        {
            try
            {
                await task;
                if (generation != _generation) { job.Done.TrySetCanceled(); return; }
                job.Done.TrySetResult(true);
            }
            catch (Exception error)
            {
                if (generation == _generation) Failed(job, reservation, error);
                else job.Done.TrySetCanceled();
            }
        }
        private void Failed(Job job, Reservation reservation, Exception error)
        {
            job.Data?.StopSpawn();
            if (reservation != null) Expire(reservation);
            if (error is OperationCanceledException) job.Done.TrySetCanceled();
            else
            {
                job.Done.TrySetException(error);
                if (!_reported) { _reported = true; Plugin.LogSource.LogError("ISB population queue: " + error.Message); }
            }
        }
        private void Refill(int[] targets)
        {
            _nextGeneric = Time.unscaledTime + 5f;
            for (int n = 0; n < 5; n++)
            {
                int faction = Config.IsReinforcementWindow(Time.unscaledTime - _startedAt) ? new[] { 3, 4, 0, 1, 2 }[n] : n;
                if (_current[faction] >= targets[faction] || _jobs.Any(j => j.Counts[faction] > 0)) continue;
                BossLocationSpawn wave;
                if (_templates.TryGetValue(faction, out var template)) { wave = template.Copy(); wave.Init(); }
                else
                {
                    string role = new[] { "blackDivAssault", "ISBSpecialForces", "chaosInsurgency", "pmcBot", "pmcUSEC" }[faction];
                    wave = new BossLocationSpawn { BossName = role, BossDifficult = "hard", BossEscortType = role, BossEscortDifficult = "hard", BossEscortAmount = "1", BossChance = 100f, BossZone = "", IgnoreMaxBots = true };
                    wave.Init();
                }
                wave.BossChance = 100f; wave.Time = -1; wave.Delay = 0; wave.ForceSpawn = false; wave.TriggerId = "isb_population_refill"; wave.TriggerName = "";
                wave.IgnoreMaxBots = true;
                int[] counts = WaveCounts(wave);
                if (!EventPopulationPolicy.CanAdmit(_global, _current, counts, targets, Config, false, false)) continue;
                var job = new Job { BossSpawner = _bossSpawner, Spawner = _bossSpawner._spawner, Wave = wave,
                    Parameters = new BotSpawnParams { Id_spawn = "isb_population_" + Guid.NewGuid().ToString("N") }, Counts = counts, Generic = true, Token = _bossSpawner._spawner.GetCancelToken() };
                _jobs.AddLast(job);
                _ = job.Done.Task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        private async Task Fetch(int generation)
        {
            try
            {
                var bytes = await RequestHandler.HttpClient.GetAsync("/ISB/labsPreBlackoutEvent/status").ConfigureAwait(false);
                var state = JsonConvert.DeserializeObject<State>(Encoding.UTF8.GetString(bytes));
                if (state == null || state.containementBreachQueue == null || state.blackoutQueue == null) throw new InvalidOperationException("Update the ISB server population configuration.");
                state.containementBreachQueue.Validate();
                state.blackoutQueue.Validate();
                ISBAishiReputationWorker.Enqueue(() => { if (generation != _generation) return; _state = state; _known = true; _fetching = false; });
            }
            catch (Exception error)
            {
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation) return;
                    _fetching = false; _nextFetch = Time.unscaledTime + 5f;
                    if (!_reported) { _reported = true; Plugin.LogSource.LogError("ISB population state unavailable; waiting to spawn: " + error.Message); }
                });
            }
        }
        private void OnDestroy() { Reset(null); if (_instance == this) _instance = null; }
    }
}
