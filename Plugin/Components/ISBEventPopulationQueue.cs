using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
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
#if ISB_SPT41
using Creation = EFT.BotCreationData;
#else
using Creation = BotCreationDataClass;

#endif
namespace ISBSpecialForces.Components
{
    public sealed class ISBEventPopulationQueue : MonoBehaviour
    {
        private sealed class State
        {
            public bool active;
            public bool performanceMode;
        }

        private sealed class Job
        {
            public BotSpawner Spawner;
            public BotZone Zone;
            public Creation Data;
            public Action<BotOwner> Callback;
            public CancellationToken Token;
            public List<ISpawnPoint> Points;
            public TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Offset;
        }

        private static ISBEventPopulationQueue _instance;
        private static readonly MethodInfo Target = AccessTools.Method(typeof(BotSpawner), "method_7", new[] { typeof(List<ISpawnPoint>), typeof(BotZone), typeof(Creation), typeof(Action<BotOwner>), typeof(CancellationToken) });
        private readonly Queue<Job> _jobs = new Queue<Job>();
        private readonly HashSet<BotSpawner> _spawners = new HashSet<BotSpawner>();
        private GameWorld _world;
        private bool _known, _limited, _busy, _bypass, _reported;
        private int _generation;
        private float _next, _pump;
        public static int Capacity(int loaded, int alive) => Math.Max(0, 12 - Math.Max(loaded, alive));
        private void Awake()
        {
            _instance = this;
            if (Target == null)
                throw new MissingMethodException("ISB: native bot spawn queue boundary is missing.");
            new Harmony("com.samc137.isb.eventpopulation").Patch(Target, prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(Gate)) { priority = Priority.Last });
        }

        private static bool Gate(BotSpawner __instance, List<ISpawnPoint> spawnPoints, BotZone botZone, Creation data, Action<BotOwner> callback, CancellationToken cancellationToken, ref Task __result)
        {
            var c = _instance;
            if (c == null || c._bypass || !ISBFikaAuthority.IsAiAuthority)
                return true;
            var world = Singleton<GameWorld>.Instance;
            if (world == null || !string.Equals(world.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase))
                return true;
            c.Ensure(world);
            if (c._known && !c._limited)
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
            spawnPoints.Clear();
            c._spawners.Add(__instance);
            c._jobs.Enqueue(job);
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
            _next = _pump = 0;
        }

        private void Clear()
        {
            _generation++;
            while (_jobs.Count > 0)
            {
                var job = _jobs.Dequeue();
                Cancel(job);
            }

            _spawners.Clear();
        }

        private static void Cancel(Job job)
        {
            try
            {
                job.Data.StopSpawn();
            }
            catch (NullReferenceException)
            {
            }

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
            _pump = Time.unscaledTime + 0.1f;
            while (_jobs.Count > 0)
            {
                var job = _jobs.Peek();
                if (job.Token.IsCancellationRequested || job.Data.SpawnStopped)
                {
                    _jobs.Dequeue();
                    Cancel(job);
                    continue;
                }

                int alive = 0;
                if (_limited && world.AllAlivePlayersList != null)
                    foreach (var player in world.AllAlivePlayersList)
                        if (player != null && player.IsAI)
                            alive++;
                int loaded = 0;
                if (_limited)
                    foreach (var spawner in _spawners)
                        loaded += spawner.AllBotsWithLoaded;
                int amount = _limited ? Math.Min(job.Data.Count, Capacity(loaded, alive)) : job.Data.Count;
                if (amount == 0)
                    break;
                bool remaining = amount < job.Data.Count;
                var data = remaining ? job.Data.Separate(amount) : job.Data;
                var points = _limited ? new List<ISpawnPoint>(amount) : new List<ISpawnPoint>(job.Points);
                if (_limited)
                    for (int i = 0; i < amount; i++)
                        points.Add(job.Points[(job.Offset + i) % job.Points.Count]);
                job.Offset += amount;
                try
                {
                    _bypass = true;
                    var task = (Task)Target.Invoke(job.Spawner, new object[] { points, job.Zone, data, job.Callback, job.Token });
                    _ = Observe(task, job, !remaining);
                }
                catch (Exception ex)
                {
                    _jobs.Dequeue();
                    data.StopSpawn();
                    job.Data.StopSpawn();
                    job.Done.TrySetException(ex);
                    Plugin.LogSource.LogError("ISB event population dispatch failed: " + ex.GetBaseException().Message);
                    continue;
                }
                finally
                {
                    _bypass = false;
                }

                if (!remaining)
                    _jobs.Dequeue();
            }
        }

        private static async Task Observe(Task task, Job job, bool last)
        {
            try
            {
                await task;
                if (last)
                    job.Done.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                job.Done.TrySetCanceled();
            }
            catch (Exception ex)
            {
                job.Done.TrySetException(ex);
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
                });
            }
            catch (Exception ex)
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
                        Plugin.LogSource.LogError("ISB event population state failed; queued spawns await retry: " + ex.Message);
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
