using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if ISB_SPT41
using Creation = BotCreationData;
using ProfileData = GetProfileDataParams;
#else
using Creation = BotCreationDataClass;
using ProfileData = BotProfileDataClass;
#endif

namespace ISBSpecialForces.Components
{
    public sealed class ISBEventPopulationQueue : MonoBehaviour
    {
        private const WildSpawnType ReserveRole = (WildSpawnType)13701;
        private const string RefillPrefix = "isb_performance_";
        private sealed class State
        {
            public bool active;
            public bool performanceMode;
            public int performanceIsbReserve;
        }

        private sealed class Job
        {
            public BotSpawner Spawner;
            public BotZone Zone;
            public Creation Data;
            public Action<BotOwner> Callback;
            public CancellationToken Token;
            public List<ISpawnPoint> Points;
            public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Offset;
            public int Pending;
            public bool Dispatched;
        }

        private static ISBEventPopulationQueue _instance;
        private static readonly MethodInfo Target = AccessTools.Method(typeof(BotSpawner), "method_7", new[] { typeof(List<ISpawnPoint>), typeof(BotZone), typeof(Creation), typeof(Action<BotOwner>), typeof(CancellationToken) });
        private readonly LinkedList<Job> _jobs = new LinkedList<Job>();
        private readonly HashSet<BotSpawner> _spawners = new HashSet<BotSpawner>();
        private readonly Dictionary<string, float> _loadingIsb = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<string> _finishedIsb = new List<string>();
        private GameWorld _world;
        private bool _known, _limited, _busy, _bypass, _reported;
        private int _generation, _reserve, _refillPending, _queuedIsb;
        private float _next, _pump, _refillAt;
        private bool _refillGenerating;
        private Creation _refillData;
        private string _refillId;

        public static int Capacity(int loaded, int alive) => ISBEventPopulationPolicy.Capacity(loaded, alive);

        private void Awake()
        {
            _instance = this;
            if (Target == null)
                throw new MissingMethodException("ISB: native bot spawn queue boundary is missing.");
            new Harmony("com.samc137.isb.eventpopulation").Patch(Target, prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(Gate)) { priority = Priority.Last });
        }

        private static bool IsIsb(Profile profile) => profile?.Info?.Settings != null && WildSpawnTypeExtensions.IsISB(profile.Info.Settings.Role);

        private static bool Gate(BotSpawner __instance, List<ISpawnPoint> spawnPoints, BotZone botZone, Creation data, Action<BotOwner> callback, CancellationToken cancellationToken, ref Task __result)
        {
            var queue = _instance;
            if (queue == null || queue._bypass || !ISBFikaAuthority.IsAiAuthority)
                return true;
            var world = Singleton<GameWorld>.Instance;
            if (world == null || !string.Equals(world.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase))
                return true;
            queue.Ensure(world);
            if (queue._known && !queue._limited)
                return true;
            if (cancellationToken.IsCancellationRequested || data.SpawnStopped || data.Count == 0 || spawnPoints.Count == 0)
            {
                __result = Task.CompletedTask;
                return false;
            }
            var job = new Job
            {
                Spawner = __instance,
                Zone = botZone,
                Data = data,
                Callback = callback,
                Token = cancellationToken,
                Points = new List<ISpawnPoint>(spawnPoints)
            };
            if (queue._limited)
                __instance.SetMaxBots(ISBEventPopulationPolicy.MaximumBots);
            if (data.SpawnParams?.Id_spawn == queue._refillId && queue._refillPending > 0)
                queue._refillPending = Math.Max(0, queue._refillPending - data.Count);
            spawnPoints.Clear();
            queue._spawners.Add(__instance);
            queue._jobs.AddLast(job);
            queue._queuedIsb += data.Profiles.Count(IsIsb);
            __result = job.Done.Task;
            return false;
        }

        private void Ensure(GameWorld world)
        {
            if (ReferenceEquals(_world, world))
                return;
            Clear();
            _world = world;
            _known = _limited = _busy = _reported = false;
            _reserve = _refillPending = 0;
            _refillId = null;
            _next = _pump = _refillAt = 0;
        }

        private void Clear()
        {
            _generation++;
            if (_refillData != null)
                Stop(_refillData);
            _refillData = null;
            _refillGenerating = false;
            _refillPending = 0;
            foreach (var job in _jobs)
                Cancel(job);
            _jobs.Clear();
            _queuedIsb = 0;
            _spawners.Clear();
            _loadingIsb.Clear();
        }

        private static void Stop(Creation data)
        {
            try
            {
                data?.StopSpawn();
            }
            catch (NullReferenceException)
            {
            }
        }

        private static void Cancel(Job job)
        {
            Stop(job.Data);
            job.Done.TrySetCanceled();
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            if (!ReferenceEquals(_world, world))
                Ensure(world);
            if (world == null || !ISBFikaAuthority.IsAiAuthority || !string.Equals(world.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase))
                return;
            if (!_known && !_busy && Time.unscaledTime >= _next)
            {
                _busy = true;
                _ = Fetch(_generation);
            }
            if (!_known || Time.unscaledTime < _pump)
                return;
            _pump = Time.unscaledTime + .1f;
            int alive = 0, isbAlive = 0;
            if (_limited && world.AllAlivePlayersList != null)
                foreach (var player in world.AllAlivePlayersList)
                    if (player != null && player.IsAI)
                    {
                        alive++;
                        if (IsIsb(player.Profile))
                        {
                            isbAlive++;
                            _loadingIsb.Remove(player.ProfileId);
                        }
                    }
            _finishedIsb.Clear();
            foreach (var pending in _loadingIsb)
                if (Time.unscaledTime >= pending.Value)
                    _finishedIsb.Add(pending.Key);
            foreach (var id in _finishedIsb)
                _loadingIsb.Remove(id);
            int dispatched = 0;
            long started = Stopwatch.GetTimestamp();
            for (var node = _jobs.First; node != null;)
            {
                var next = node.Next;
                var job = node.Value;
                if (job.Token.IsCancellationRequested || job.Data.SpawnStopped || job.Done.Task.IsCompleted)
                {
                    _jobs.Remove(node);
                    _queuedIsb = Math.Max(0, _queuedIsb - job.Data.Profiles.Count(IsIsb));
                    Cancel(job);
                    node = next;
                    continue;
                }
                bool isb = job.Data.Profiles.Count > 0 && job.Data.Profiles.All(IsIsb);
                int loaded = _limited ? _spawners.Sum(spawner => Math.Max(0, spawner.AllBotsWithLoaded)) : 0;
                int available = _limited ? ISBEventPopulationPolicy.Capacity(loaded, alive, isbAlive, isb, _reserve) : job.Data.Count;
                int amount = Math.Min(job.Data.Count, available);
                if (amount > 0)
                {
                    Dispatch(job, amount);
                    if (job.Dispatched || job.Done.Task.IsCompleted)
                        _jobs.Remove(node);
                    dispatched++;
                }
                node = next;
                if (dispatched >= 4 || Stopwatch.GetTimestamp() - started >= Stopwatch.Frequency / 500)
                    break;
            }
            if (_limited && _reserve > 0)
                Refill(isbAlive);
        }

        private void Dispatch(Job job, int amount)
        {
            bool last = amount >= job.Data.Count;
            var data = last ? job.Data : job.Data.Separate(amount);
            var points = _limited ? new List<ISpawnPoint>(amount) : new List<ISpawnPoint>(job.Points);
            if (_limited)
                for (int i = 0; i < amount; i++)
                    points.Add(job.Points[(job.Offset + i) % job.Points.Count]);
            job.Offset += amount;
            var pendingIsb = data.Profiles.Where(IsIsb).Select(profile => profile.Id).ToArray();
            _queuedIsb = Math.Max(0, _queuedIsb - pendingIsb.Length);
            foreach (var id in pendingIsb)
                _loadingIsb[id] = Time.unscaledTime + 30f;
            int generation = _generation;
            Action<BotOwner> callback = bot =>
            {
                if (generation != _generation)
                    return;
                if (bot?.Profile != null)
                    _loadingIsb.Remove(bot.Profile.Id);
                job.Callback?.Invoke(bot);
            };
            job.Pending++;
            job.Dispatched = last;
            try
            {
                _bypass = true;
                var task = (Task)Target.Invoke(job.Spawner, new object[] { points, job.Zone, data, callback, job.Token });
                _ = Observe(task, job, data, pendingIsb, generation);
            }
            catch (Exception error)
            {
                Stop(data);
                Stop(job.Data);
                foreach (var id in pendingIsb)
                    _loadingIsb.Remove(id);
                job.Done.TrySetException(error.GetBaseException());
                Plugin.LogSource.LogError("ISB event population dispatch failed: " + error.GetBaseException().Message);
            }
            finally
            {
                _bypass = false;
            }
        }

        private async Task Observe(Task task, Job job, Creation data, string[] pendingIsb, int generation)
        {
            try
            {
                await task;
                job.Pending--;
                if (job.Dispatched && job.Pending == 0)
                    job.Done.TrySetResult(true);
            }
            catch (Exception error)
            {
                if (generation == _generation)
                    foreach (var id in pendingIsb)
                        _loadingIsb.Remove(id);
                Stop(data);
                Stop(job.Data);
                if (error is OperationCanceledException)
                    job.Done.TrySetCanceled();
                else
                    job.Done.TrySetException(error);
            }
        }

        private void Refill(int isbAlive)
        {
            if (isbAlive >= _reserve || Time.unscaledTime < _refillAt)
                return;
            if (_refillGenerating || _refillData != null && !_refillData.SpawnStopped && _refillPending > 0)
                return;
            _refillPending = 0;
            int queued = _loadingIsb.Count + _queuedIsb;
            int missing = ISBEventPopulationPolicy.MissingIsb(isbAlive, queued, _reserve);
            if (missing == 0)
                return;
            var game = Singleton<IBotGame>.Instance;
            var controller = game?.BotsController;
            if (controller == null || game.Status != GameStatus.Started && game.Status != GameStatus.Running)
                return;
            var spawner = controller.BotSpawner;
            if (spawner == null)
                return;
            spawner.SetMaxBots(ISBEventPopulationPolicy.MaximumBots);
            _spawners.Add(spawner);
            var zone = spawner.SpawnZones(false).FirstOrDefault();
            if (zone == null)
                return;
            _refillId = RefillPrefix + Guid.NewGuid().ToString("N");
            _refillAt = Time.unscaledTime + 1f;
            _refillPending = missing;
            _refillGenerating = true;
            _ = CreateReserve(spawner, zone, missing, _refillId, _generation);
        }

        private async Task CreateReserve(BotSpawner spawner, BotZone zone, int amount, string id, int generation)
        {
            Creation data = null;
            try
            {
                var parameters = new BotSpawnParams { Id_spawn = id };
                var profile = new ProfileData(EPlayerSide.Savage, ReserveRole, BotDifficulty.hard, 0f, parameters);
#if ISB_SPT41
                var creator = spawner._botCreator;
#else
                var creator = spawner.BotCreator;
#endif
                data = await Creation.Create(profile, creator, amount, spawner);
                if (generation != _generation || !ReferenceEquals(_world, Singleton<GameWorld>.Instance))
                {
                    Stop(data);
                    return;
                }
                if (data == null || data.Count == 0 || data.SpawnStopped)
                {
                    Stop(data);
                    _refillPending = 0;
                    return;
                }
                _refillData = data;
                _refillPending = data.Count;
                spawner.TryToSpawnInZoneAndDelay(zone, data, false, true, null, true);
            }
            catch (Exception error)
            {
                if (data != null)
                    Stop(data);
                if (generation == _generation)
                {
                    _refillData = null;
                    _refillPending = 0;
                    Plugin.LogSource.LogError("ISB event reserve spawn failed: " + error.GetBaseException().Message);
                }
            }
            finally
            {
                if (generation == _generation)
                    _refillGenerating = false;
            }
        }

        private async Task Fetch(int generation)
        {
            try
            {
                var bytes = await RequestHandler.HttpClient.GetAsync("/ISB/labsPreBlackoutEvent/status").ConfigureAwait(false);
                var state = JsonConvert.DeserializeObject<State>(Encoding.UTF8.GetString(bytes));
                if (state == null)
                    throw new InvalidOperationException("Missing Labs event population state.");
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    _busy = false;
                    _known = true;
                    _limited = state.active && state.performanceMode;
                    _reserve = _limited ? Math.Min(ISBEventPopulationPolicy.MinimumIsb, Math.Max(0, state.performanceIsbReserve)) : 0;
                    if (_limited)
                        foreach (var spawner in _spawners)
                            spawner.SetMaxBots(ISBEventPopulationPolicy.MaximumBots);
                });
            }
            catch (Exception error)
            {
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    _busy = false;
                    _next = Time.unscaledTime + 2;
                    if (!_reported)
                    {
                        _reported = true;
                        Plugin.LogSource.LogError("ISB event population state failed; queued spawns await retry: " + error.Message);
                    }
                });
            }
        }

        private void OnDestroy()
        {
            Clear();
            if (_instance == this)
                _instance = null;
        }
    }
}
