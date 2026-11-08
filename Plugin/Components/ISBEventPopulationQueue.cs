using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using ISB.Compatibility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBEventPopulationQueue : MonoBehaviour
    {
        private const int BlackDivision = 0, Isb = 1, Chaos = 2, Raider = 3, Usec = 4, Bear = 5, Other = 6;
        private const string RefillTrigger = "isb_population_refill";
        private static readonly string[] Leaders = { "blackDivAssault", "ISBTeamLeader", "chaosInsurgency", "pmcBot", "pmcUSEC", "pmcBEAR" };
        private static readonly WildSpawnType[] IsbFollowers =
        {
            (WildSpawnType)13701, (WildSpawnType)13710, (WildSpawnType)13711,
            (WildSpawnType)13712, (WildSpawnType)13713, (WildSpawnType)13714
        };

        private sealed class State
        {
            public bool active;
            public bool chaosInsurgencyOnly;
            public bool? populationReady;
            public EventPopulationConfig containementBreachQueue;
            public EventPopulationConfig blackoutQueue;
            public bool[] populationFactions;
        }
        private sealed class SquadSlot
        {
            public int Faction;
            public int Size;
            public bool Normal, Reinforcement;
            public float RetryAt;
            public Reservation Deployed;
            public PreparedSquad Prepared;
            public float PrepareRetryAt;
        }
        private sealed class PreparedRole
        {
            public WildSpawnType Role;
            public BotDifficulty Difficulty;
            public int Count;
            public readonly Queue<Profile> Profiles = new Queue<Profile>();
        }
        private sealed class PreparedSquad
        {
            public SquadSlot Slot;
            public BossLocationSpawn Wave;
            public BotSpawnParams Parameters;
            public readonly Dictionary<string, PreparedRole> Roles = new Dictionary<string, PreparedRole>(StringComparer.Ordinal);
            public bool Claimed;
            public bool Failed;
        }
        private sealed class Reservation
        {
            public BossLocationSpawn Wave;
            public int[] Remaining;
            public SquadSlot Slot;
            public int Alive;
            public float LastActivationAt;
            public bool Activated;
            public bool Ready;
            public CancellationToken Token;
            public bool Special;
            public bool Expired;
            public float Deadline;
            public CancellationTokenSource Cancellation;
            public bool Cached;
            public readonly HashSet<BotCreationData> Creations = new HashSet<BotCreationData>();
            public readonly HashSet<string> Registered = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> ActivatedProfiles = new HashSet<string>(StringComparer.Ordinal);
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
            public SquadSlot Slot;
            public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private static ISBEventPopulationQueue _instance;
        private static readonly MethodInfo Activation = AccessTools.Method(typeof(BotSpawner), "method_7", new[] { typeof(List<ISpawnPoint>), typeof(BotZone), typeof(BotCreationData), typeof(Action<BotOwner>), typeof(CancellationToken) });
        private static readonly FieldInfo ProfileData = AccessTools.Field(typeof(BotCreationData), "_profileData");
        private readonly LinkedList<Job> _jobs = new LinkedList<Job>();
        private readonly Dictionary<BotSpawnParams, Reservation> _reservations = new Dictionary<BotSpawnParams, Reservation>();
        private static readonly object RetiredSpawn = new object();
        private ConditionalWeakTable<BotSpawnParams, object> _expired = new ConditionalWeakTable<BotSpawnParams, object>();
        private readonly Dictionary<string, Reservation> _profileOwners = new Dictionary<string, Reservation>(StringComparer.Ordinal);
        private readonly Dictionary<BotSpawnParams, PreparedSquad> _prepared = new Dictionary<BotSpawnParams, PreparedSquad>();
        private readonly HashSet<string> _preparedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _freshGate = new SemaphoreSlim(1, 1);
        private readonly HashSet<Player> _watched = new HashSet<Player>();
        private readonly List<BotSpawnParams> _retired = new List<BotSpawnParams>();
        private readonly int[] _current = new int[Other + 1];
        private List<SquadSlot> _slots;
        private int[] _normalTargets, _reinforcementTargets;
        private SquadSlot _blackDivisionHunter;
        private GameWorld _world;
        private BotBossSpawn _bossSpawner;
        private State _state;
        private bool _known, _fetching, _waveBypass, _activationBypass, _reported, _started, _wedgeDispatched;
        private int _generation;
        private float _nextFetch, _nextPump, _nextDispatchAt, _genericStartsAt, _startedAt;
        private int _global;
        private int _actualAlive;
        private bool _preparing, _cacheReported;
        private EventPopulationConfig Config => _state == null ? null : _state.active ? _state.containementBreachQueue : _state.blackoutQueue;
        private bool Active => _known && Config?.enabled == true && (_state.active || _started);
        private bool ChaosOnly => _known && _state?.active == true && _state.chaosInsurgencyOnly;
        internal static bool IsContainmentHunt => _instance?._state?.active == true;

        internal static bool TryGetHuntSquad(BotOwner bot, out object squad, out bool huntsPlayer)
        {
            squad = null;
            huntsPlayer = false;
            var queue = _instance;
            if (queue == null || !queue.Active || bot == null) return false;
            var parameters = bot.SpawnProfileData?.SpawnParams;
            if ((parameters == null || !queue._reservations.TryGetValue(parameters, out var reservation)) &&
                (string.IsNullOrEmpty(bot.Profile?.Id) || !queue._profileOwners.TryGetValue(bot.Profile.Id, out reservation)))
                return false;
            if (reservation.Slot == null) return false;
            squad = reservation;
            huntsPlayer = reservation.Slot.Faction == Chaos || ReferenceEquals(reservation.Slot, queue._blackDivisionHunter);
            return true;
        }
        internal static bool IsHuntSquadAssembled(object squad) => squad is Reservation reservation && reservation.Ready;
        internal static int GetManagedSquadSize(object squad) => squad is Reservation reservation ? reservation.Slot.Size : 0;
        internal static bool HasPreparedProfiles(BotSpawnParams parameters) => parameters != null &&
            _instance != null && _instance._prepared.ContainsKey(parameters);

        private void Awake()
        {
            _instance = this;
            if (Activation == null) throw new MissingMethodException("ISB: native bot activation boundary is missing.");
            var harmony = new Harmony("com.samc137.isb.eventpopulation");
            harmony.Patch(Activation, prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(GateActivation)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(BotBossSpawn), "Spawn"), prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(GateWave)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(GlobalEventDispatcher), "AnyEvent"), prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(EventStarted)));
            harmony.Patch(AccessTools.Method(typeof(BotCreationData), nameof(BotCreationData.Create),
                new[] { typeof(IGetProfileData), typeof(IBotCreator), typeof(int), typeof(ITokenGetter) }),
                prefix: new HarmonyMethod(typeof(ISBEventPopulationQueue), nameof(CreatePrepared)) { priority = Priority.First });
        }

        private static string RoleKey(WildSpawnType role, BotDifficulty difficulty) => ((int)role).ToString(CultureInfo.InvariantCulture) + ":" + ((int)difficulty).ToString(CultureInfo.InvariantCulture);
        private static void NormalizeFreshSide(Profile profile)
        {
            if (profile.Info.Settings.Role == WildSpawnType.pmcUSEC) profile.Info.Side = EPlayerSide.Usec;
            else if (profile.Info.Settings.Role == WildSpawnType.pmcBEAR) profile.Info.Side = EPlayerSide.Bear;
        }
        private static bool CreatePrepared(IGetProfileData profileData, IBotCreator botCreator, int count, ITokenGetter token, ref Task<BotCreationData> __result)
        {
            var q = _instance;
            var parameters = (profileData as GetProfileDataParams)?.SpawnParams;
            if (q == null || parameters == null) return true;
            if (q._expired.TryGetValue(parameters, out _))
            {
                __result = Task.FromCanceled<BotCreationData>(new CancellationToken(true));
                return false;
            }
            if (!q._prepared.TryGetValue(parameters, out var prepared))
            {
                if (!q._reservations.TryGetValue(parameters, out var owned)) return true;
                if (owned.Expired)
                {
                    __result = Task.FromCanceled<BotCreationData>(new CancellationToken(true));
                    return false;
                }
                __result = q.CreateFreshNative(profileData, botCreator, count, token, owned);
                return false;
            }
            try
            {
                if (!prepared.Claimed || prepared.Failed || !q._reservations.TryGetValue(parameters, out var reservation) || reservation.Expired)
                    throw new OperationCanceledException("The prepared squad reservation is no longer active.");
                token.GetCancelToken().ThrowIfCancellationRequested();
                var native = (GetProfileDataParams)profileData;
                if (!prepared.Roles.TryGetValue(RoleKey(native.Role, native.BotDifficulty), out var role) || count <= 0 || role.Profiles.Count < count)
                    throw new InvalidOperationException("Native squad creation requested an unavailable prepared role.");
                var profiles = new List<Profile>(count);
                for (int i = 0; i < count; i++) profiles.Add(role.Profiles.Dequeue());
                var data = BotCreationData.CreateWithoutProfile(profileData);
                data._botCreator = botCreator;
                data._tokenGetter = token;
                foreach (var profile in profiles) data._profilesIdsHash.Add(profile.Id);
                data.AddProfiles(profiles);
                q.Register(reservation, data);
                __result = Task.FromResult(data);
            }
            catch (Exception error)
            {
                prepared.Failed = true;
                if (q._reservations.TryGetValue(parameters, out var failed)) Expire(failed);
                __result = Task.FromException<BotCreationData>(error);
            }
            return false;
        }
        private static int Faction(WildSpawnType role)
        {
            int value = (int)role;
            if (value == GhostBotVariant.ChaosValue) return Chaos;
            if (value >= 13701 && value <= 13716) return Isb;
            if (value >= 848420 && value <= 848426) return BlackDivision;
            if (role == WildSpawnType.pmcBot) return Raider;
            if (role == WildSpawnType.pmcUSEC) return Usec;
            if (role == WildSpawnType.pmcBEAR) return Bear;
            return Other;
        }
        private static bool NamedSpecial(WildSpawnType role)
        {
            int value = (int)role;
            return value >= 13702 && value <= 13706 || value == 13715 || value == 13716 || value == 848424 || value == 848426;
        }
        private static int[] WaveCounts(BossLocationSpawn wave)
        {
            var counts = new int[Other + 1];
            counts[Faction(wave.BossType)]++;
            var escorts = wave.GetEscors();
            if (escorts != null)
                foreach (var escort in escorts) counts[Faction(escort.BossEscortType)] += escort.BossEscortAmount;
            else counts[Faction(wave.EscortType)] += wave.EscortCount;
            return counts;
        }
        private static bool SpecialWave(BossLocationSpawn wave) => NamedSpecial(wave.BossType) || GhostBotSpawnBridge.IsSpecialWave(wave) ||
            (wave.GetEscors()?.Any(e => NamedSpecial(e.BossEscortType)) ?? NamedSpecial(wave.EscortType));
        private static bool ManagedWave(int[] counts) => counts[Other] != counts.Sum();
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
            var counts = WaveCounts(wave);
            if (q.ChaosOnly && !EventPopulationPolicy.IsChaosOnly(counts))
            {
                data?.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            q._bossSpawner = __instance;
            if (data != null && spawnParams != null && q._reservations.TryGetValue(spawnParams, out var reservation) && ReferenceEquals(reservation.Wave, wave))
            {
                if (data.SpawnStopped) Expire(reservation);
                if (!reservation.Expired) return true;
                data?.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            if (spawnParams != null && q._expired.TryGetValue(spawnParams, out _))
            {
                data?.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            if (q._known && !q.Active) return true;
            bool special = SpecialWave(wave);
            if (q.Active && !special && ManagedWave(counts))
            {
                data?.StopSpawn(); __result = Task.CompletedTask; return false;
            }
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
                Spawner = __instance._spawner, Token = __instance._spawner.GetCancelToken(), Counts = counts, Special = special };
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
            if (data.SpawnParams != null && q._expired.TryGetValue(data.SpawnParams, out _))
            {
                data.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            if (q._known && !q.Active) return true;
            var counts = new int[Other + 1];
            foreach (var profile in data.Profiles) counts[Faction(profile.Info.Settings.Role)]++;
            bool special = data.Profiles.Any(p => NamedSpecial(p.Info.Settings.Role));
            if (q.Active && !special && ManagedWave(counts))
            {
                data.StopSpawn(); __result = Task.CompletedTask; return false;
            }
            var job = new Job { Spawner = __instance, Zone = botZone, Data = data, Parameters = data.SpawnParams ?? new BotSpawnParams(), Callback = callback,
                Token = cancellationToken, Points = new List<ISpawnPoint>(spawnPoints), Counts = counts, Special = special };
            q._jobs.AddLast(job);
            spawnPoints.Clear(); __result = job.Done.Task;
            return false;
        }
        private Task ActivateReserved(BotSpawner spawner, List<ISpawnPoint> points, BotZone zone, BotCreationData data, Action<BotOwner> callback, CancellationToken token, Reservation reservation)
        {
            try
            {
                _activationBypass = true;
                int generation = _generation;
                Action<BotOwner> activated = bot =>
                {
                    if (generation == _generation && bot?.Profile?.Id is string id)
                    {
                        if (reservation.Profiles.ContainsKey(id)) reservation.ActivatedProfiles.Add(id);
                        if (!reservation.Expired) MarkActivated(reservation);
                        Watch(bot.GetPlayer);
                        _nextPump = 0f;
                    }
                    callback?.Invoke(bot);
                };
                return ObserveActivation((Task)Activation.Invoke(spawner, new object[] { points, zone, data, activated, reservation.Cancellation?.Token ?? token }), data, reservation, generation);
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
                    if (data.SpawnStopped) Expire(reservation);
                    if (reservation.Expired)
                    {
                        reservation.Creations.Remove(data);
                        Retire(reservation, data);
                    }
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
            reservation.Cancellation?.Cancel();
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
            _profileOwners[id] = reservation;
            if (reservation.Expired) return;
            reservation.Remaining[faction] = Math.Max(0, reservation.Remaining[faction] - 1);
            reservation.Profiles[id] = faction;
            if (reservation.Cached) reservation.Deadline = Math.Max(reservation.Deadline, Time.unscaledTime + 15f);
        }
        private static void MarkActivated(Reservation reservation)
        {
            reservation.LastActivationAt = Time.unscaledTime;
            reservation.Activated = true;
            if (reservation.Cached) reservation.Deadline = Math.Max(reservation.Deadline, Time.unscaledTime + 15f);
        }
        private void Watch(Player player)
        {
            if (player != null && _watched.Add(player)) player.OnPlayerDeadOrUnspawn += PlayerLeft;
        }
        private void PlayerLeft(Player player)
        {
            if (player != null && _watched.Remove(player)) player.OnPlayerDeadOrUnspawn -= PlayerLeft;
            _nextPump = 0f;
        }
        private static void EventStarted(string __0)
        {
            var q = _instance;
            if (q == null || string.IsNullOrEmpty(__0) || !__0.StartsWith("isb_labs_blackout_", StringComparison.Ordinal) || !q.Context()) return;
            if (!q._started) q.StartPopulation();
        }
        private void StartPopulation()
        {
            _started = true;
            _startedAt = Time.unscaledTime;
            _genericStartsAt = _startedAt + (Config?.genericRefillDelaySeconds ?? 15);
        }
        private void Reset(GameWorld world)
        {
            _generation++;
            foreach (var job in _jobs) { job.Data?.StopSpawn(); job.Done.TrySetCanceled(); }
            foreach (var reservation in _reservations.Values) Expire(reservation);
            foreach (var player in _watched)
                if (player != null) player.OnPlayerDeadOrUnspawn -= PlayerLeft;
            _watched.Clear();
            _jobs.Clear();
            _reservations.Clear();
            _expired = new ConditionalWeakTable<BotSpawnParams, object>();
            _profileOwners.Clear();
            _prepared.Clear();
            _preparedIds.Clear();
            _preparing = _cacheReported = false;
            _retired.Clear();
            _slots = null;
            _normalTargets = _reinforcementTargets = null;
            _blackDivisionHunter = null;
            _world = world; _bossSpawner = null; _state = null;
            _known = _fetching = _reported = _started = _wedgeDispatched = false;
            _nextFetch = _nextPump = _nextDispatchAt = _genericStartsAt = _startedAt = 0;
        }
        private void Update()
        {
            if (!Context()) return;
            if (!_known && !_fetching && Time.unscaledTime >= _nextFetch) { _fetching = true; _ = Fetch(_generation); }
            if (!_known || Time.unscaledTime < _nextPump) return;
            _nextPump = Time.unscaledTime + .5f;
            var game = Singleton<IBotGame>.Instance;
            if (game == null || game.Status != GameStatus.Started && game.Status != GameStatus.Running) return;
            if (_state.active && !_started) StartPopulation();
            bool active = Active;
            if (_bossSpawner == null && active) _bossSpawner = game.BotsController?.BotSpawner?.BossSpawner;
            if (_world.AllAlivePlayersList == null) return;
            if (active) Count();
            LinkedListNode<Job> special = null, ambient = null;
            for (var node = _jobs.First; node != null;)
            {
                var next = node.Next;
                var job = node.Value;
                if (job.Token.IsCancellationRequested || job.Data?.SpawnStopped == true)
                {
                    _jobs.Remove(node); job.Data?.StopSpawn(); job.Done.TrySetCanceled();
                }
                else if (ChaosOnly && !EventPopulationPolicy.IsChaosOnly(job.Counts) ||
                    active && !job.Special && ManagedWave(job.Counts))
                {
                    _jobs.Remove(node); job.Data?.StopSpawn(); job.Done.TrySetResult(false);
                }
                else if (active && job.Counts.Sum() > Config.Ceiling)
                {
                    _jobs.Remove(node); Failed(job, null, new InvalidOperationException("A formation exceeds the complete raid spawn limit."));
                }
                else if (job.Special && (!active || _global + job.Counts.Sum() <= Config.Ceiling))
                {
                    if (special == null) special = node;
                }
                else if (!job.Special && ambient == null && (!active || _global + job.Counts.Sum() <= Config.maximumBots))
                {
                    ambient = node;
                }
                node = next;
            }
            if (active)
            {
                EnsureSlots();
                bool reinforcement = Config.IsReinforcementWindow(Time.unscaledTime - _startedAt);
                SelectBlackDivisionHunter(reinforcement);
                if (HasLoadingSquad()) return;
                if (_actualAlive > 0 && Time.unscaledTime < _nextDispatchAt)
                {
                    PrepareNext(reinforcement);
                    return;
                }
                if (special != null || _jobs.Any(job => job.Special))
                {
                    if (special != null) DispatchQueued(special);
                    return;
                }
                if ((Time.unscaledTime >= _genericStartsAt || _actualAlive == 0) && _bossSpawner != null && TryRefill(reinforcement)) return;
                PrepareNext(reinforcement);
            }
            var ready = special ?? ambient;
            if (ready != null) DispatchQueued(ready);
        }
        private void DispatchQueued(LinkedListNode<Job> node)
        {
            _jobs.Remove(node);
            Dispatch(node.Value);
        }
        private bool HasLoadingSquad()
        {
            foreach (var reservation in _reservations.Values)
                if (!reservation.Expired && !reservation.Ready)
                    return true;
            return false;
        }
        private void Count()
        {
            Array.Clear(_current, 0, _current.Length);
            _global = 0;
            _actualAlive = 0;
            foreach (var reservation in _reservations.Values) reservation.Alive = 0;
            foreach (var player in _world.AllAlivePlayersList)
                if (player != null && player.IsAI && player.HealthController?.IsAlive == true && player.AIData?.BotOwner?.IsDead != true)
                {
                    string id = player.ProfileId;
                    var role = player.Profile.Info.Settings.Role;
                    int faction = Faction(role);
                    Watch(player.AIData?.BotOwner?.GetPlayer);
                    if (!_profileOwners.TryGetValue(id, out var reservation))
                    {
                        var parameters = player.AIData?.BotOwner?.SpawnProfileData?.SpawnParams;
                        if (parameters != null && _reservations.TryGetValue(parameters, out reservation))
                            RegisterProfile(reservation, id, faction);
                    }
                    if (reservation != null)
                    {
                        reservation.Alive++;
                        reservation.ActivatedProfiles.Remove(id);
                        if (reservation.Profiles.Remove(id) || !reservation.Activated) MarkActivated(reservation);
                    }
                    _global++;
                    _actualAlive++;
                    if (reservation?.Special != true && !NamedSpecial(role)) _current[faction]++;
                }
            foreach (var pair in _reservations)
            {
                var reservation = pair.Value;
                if (!reservation.Expired && (reservation.Token.IsCancellationRequested ||
                    Time.unscaledTime >= reservation.Deadline &&
                    (reservation.Remaining.Sum() > 0 || reservation.Profiles.Count > 0 || reservation.Creations.Count > 0)))
                    Expire(reservation);
                if (reservation.Expired)
                {
                    reservation.Creations.Clear();
                    reservation.Profiles.Clear();
                }
                float sinceLastActivation = Time.unscaledTime - reservation.LastActivationAt;
                if (sinceLastActivation >= 1f && reservation.ActivatedProfiles.Count > 0)
                {
                    foreach (string id in reservation.ActivatedProfiles) reservation.Profiles.Remove(id);
                    reservation.ActivatedProfiles.Clear();
                }
                int pending = reservation.Profiles.Count;
                foreach (int count in reservation.Remaining) pending += count;
                reservation.Ready = pending == 0 && reservation.Activated;
                if (reservation.Ready) reservation.Creations.Clear();
                if (ManagedSquadPopulation.CanRetire(reservation.Alive, pending, reservation.Creations.Count,
                    reservation.Activated, sinceLastActivation))
                {
                    _retired.Add(pair.Key);
                    continue;
                }
                _global += pending;
                if (reservation.Special) continue;
                for (int i = 0; i < _current.Length; i++) _current[i] += reservation.Remaining[i];
                foreach (int faction in reservation.Profiles.Values) _current[faction]++;
            }
            foreach (var parameters in _retired)
            {
                var reservation = _reservations[parameters];
                _reservations.Remove(parameters);
                _expired.Add(parameters, RetiredSpawn);
                _prepared.Remove(parameters);
                reservation.Cancellation?.Dispose();
                foreach (string id in reservation.Registered)
                    if (_profileOwners.TryGetValue(id, out var owner) && ReferenceEquals(owner, reservation)) _profileOwners.Remove(id);
                if (reservation.Slot == null) continue;
                reservation.Slot.Deployed = null;
                _nextPump = 0f;
                if (!reservation.Activated) reservation.Slot.RetryAt = Time.unscaledTime + 5f;
            }
            _retired.Clear();
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
                _nextDispatchAt = Time.unscaledTime + 1f;
                reservation = new Reservation { Wave = job.Wave, Slot = job.Slot, Remaining = (int[])job.Counts.Clone(), Special = job.Special,
                    Deadline = Time.unscaledTime + (job.Slot != null ? 30f : 180f), Token = job.Token,
                    Cached = job.Slot != null, Cancellation = CancellationTokenSource.CreateLinkedTokenSource(job.Token) };
                _reservations[job.Parameters] = reservation;
                if (job.Slot != null) job.Slot.Deployed = reservation;
                _global += job.Counts.Sum();
                if (!job.Special) for (int i = 0; i < _current.Length; i++) _current[i] += job.Counts[i];
                job.Spawner.SetMaxBots(Config.Ceiling);
            }
            try
            {
                Task task;
                _waveBypass = true;
                if (job.Wave != null)
                {
                    if (Active) job.Wave.IgnoreMaxBots = true;
                    task = job.BossSpawner.Spawn(job.Wave, job.Parameters, job.Data);
                }
                else if (reservation != null)
                {
                    Register(reservation, job.Data);
                    task = ActivateReserved(job.Spawner, job.Points, job.Zone, job.Data, job.Callback, job.Token, reservation);
                }
                else
                {
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
        private void EnsureSlots()
        {
            if (_slots != null) return;
            _slots = new List<SquadSlot>();
            _normalTargets = ExpandTargets(Config.normal);
            _reinforcementTargets = ExpandTargets(Config.reinforcement);
            foreach (int faction in new[] { Raider, Usec, Bear, BlackDivision, Isb, Chaos })
            {
                var allocations = faction == Isb && _state?.active == false
                    ? ManagedSquadPopulation.AllocatePhaseSquads(_normalTargets[faction], _reinforcementTargets[faction], 4)
                    : ManagedSquadPopulation.Allocate(_normalTargets[faction], _reinforcementTargets[faction], faction == Chaos ? 3 : 4);
                foreach (var allocation in allocations)
                {
                    _slots.Add(new SquadSlot
                    {
                        Faction = faction, Size = allocation.Size, Normal = allocation.Normal, Reinforcement = allocation.Reinforcement
                    });
                }
            }
        }
        private void PrepareNext(bool reinforcement)
        {
            if (_preparing || _bossSpawner == null || HasLoadingSquad() || _slots == null) return;
            SquadSlot selected = null;
            for (int priority = 0; priority < 3 && selected == null; priority++)
                foreach (var slot in _slots)
                {
                    if (slot.Prepared != null || Time.unscaledTime < slot.PrepareRetryAt || ChaosOnly && slot.Faction != Chaos) continue;
                    bool current = reinforcement ? slot.Reinforcement : slot.Normal;
                    int rank = current && slot.Deployed == null ? 0 : slot.Deployed?.Alive > 0 ? 1 : 2;
                    if (rank != priority) continue;
                    selected = slot;
                    break;
                }
            if (selected == null) return;
            _preparing = true;
            _ = PrepareSquad(selected, _generation, _bossSpawner._spawner.GetCancelToken());
        }

        private static void AddPreparedRole(PreparedSquad prepared, WildSpawnType role, BotDifficulty difficulty, int count)
        {
            if (count <= 0) return;
            string key = RoleKey(role, difficulty);
            if (!prepared.Roles.TryGetValue(key, out var entry))
            {
                entry = new PreparedRole { Role = role, Difficulty = difficulty };
                prepared.Roles.Add(key, entry);
            }
            entry.Count += count;
        }

        private async Task PrepareSquad(SquadSlot slot, int generation, CancellationToken token)
        {
            try
            {
                var wave = CreateSquadWave(slot);
                var prepared = new PreparedSquad
                {
                    Slot = slot, Wave = wave,
                    Parameters = new BotSpawnParams { Id_spawn = "isb_population_" + Guid.NewGuid().ToString("N") }
                };
                AddPreparedRole(prepared, wave.BossType, wave.BossDif, 1);
                var escorts = wave.GetEscors();
                if (escorts != null)
                    foreach (var escort in escorts) AddPreparedRole(prepared, escort.BossEscortType, escort.EscortDifficulty, escort.BossEscortAmount);
                else AddPreparedRole(prepared, wave.EscortType, wave.EscortDif, wave.EscortCount);
                int requested = prepared.Roles.Values.Sum(role => role.Count);
                if (requested != slot.Size) throw new InvalidOperationException("Prepared squad size differs from its native wave.");
                await LoadFreshProfiles(prepared, _bossSpawner._botCreator, generation, token);
                token.ThrowIfCancellationRequested();
                if (generation != _generation) return;
                slot.Prepared = prepared;
                _prepared.Add(prepared.Parameters, prepared);
                _nextPump = 0f;
            }
            catch (Exception error)
            {
                if (generation == _generation && !(error is OperationCanceledException))
                {
                    slot.PrepareRetryAt = Time.unscaledTime + 5f;
                    if (!_cacheReported)
                    {
                        _cacheReported = true;
                        Plugin.LogSource.LogWarning("ISB squad preparation will retry: " + error.GetBaseException().Message);
                    }
                }
            }
            finally { if (generation == _generation) _preparing = false; }
        }

        private async Task<List<Profile>> LoadFreshProfiles(PreparedSquad prepared, IBotCreator creator, int generation, CancellationToken token)
        {
            await _freshGate.WaitAsync(token);
            try
            {
                int requested = prepared.Roles.Values.Sum(role => role.Count);
                var payload = JsonConvert.SerializeObject(new
                {
                    conditions = prepared.Roles.Values.Select(role => new
                    {
                        Role = role.Role.ToString(), Difficulty = role.Difficulty.ToString().ToLowerInvariant(), Limit = role.Count
                    }).ToArray()
                });
                var json = await RequestHandler.PostJsonAsync("/ISB/population/generate", payload);
                token.ThrowIfCancellationRequested();
                if (generation != _generation) throw new OperationCanceledException();
                var response = JObject.Parse(json);
                if ((int?)response["err"] != 0 || !(response["data"] is JArray array))
                    throw new InvalidOperationException("Could not prepare fresh native bot profiles.");
                var descriptors = JsonExtensions.ParseJsonTo<ProfileDescriptor[]>(array.ToString(Formatting.None), Array.Empty<JsonConverter>());
                if (descriptors == null || descriptors.Length != requested)
                    throw new InvalidOperationException("Fresh bot response returned an incomplete squad.");
                var profiles = descriptors.Select(descriptor => new Profile(descriptor)).ToList();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var profile in profiles)
                {
                    if (string.IsNullOrEmpty(profile?.Id) || profile.Info?.Settings == null || !ids.Add(profile.Id) ||
                        _preparedIds.Contains(profile.Id) || BotCreationData._generatedHash.Contains(profile.Id))
                        throw new InvalidOperationException("Fresh bot response reused a profile identity.");
                    NormalizeFreshSide(profile);
                    var matches = prepared.Roles.Values.Where(role => role.Role == profile.Info.Settings.Role).ToArray();
                    if (matches.Length != 1 || matches[0].Profiles.Count >= matches[0].Count)
                        throw new InvalidOperationException("Fresh bot response returned an unexpected native role.");
                    matches[0].Profiles.Enqueue(profile);
                }
                if (prepared.Roles.Values.Any(role => role.Profiles.Count != role.Count))
                    throw new InvalidOperationException("Fresh bot response omitted a squad member role.");
                foreach (string id in ids)
                {
                    _preparedIds.Add(id);
                    BotCreationData._generatedHash.Add(id);
                }
                if (profiles.Any(profile => (int)profile.Info.Settings.Role == GhostBotVariant.ChaosValue))
                {
                    ISBEventHostility.RememberVariants(profiles, GhostBotVariant.ChaosName);
                    ISBChaosVoice.Remember(profiles, GhostBotVariant.ChaosName);
                }
                var resources = profiles.SelectMany(profile => profile.GetAllPrefabPaths(allCustomization: false)).Distinct().ToArray();
                var nativeCreator = creator as BotProfileClient;
                if (nativeCreator != null) nativeCreator._bundlesLoading++;
                try
                {
                    await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid,
                        ObjectsFactory.AssemblyType.Local, resources, Diz.Jobs.JobYieldPriority.General, null, token);
                }
                finally { if (nativeCreator != null) nativeCreator._bundlesLoading--; }
                return profiles;
            }
            finally { _freshGate.Release(); }
        }

        private async Task<BotCreationData> CreateFreshNative(IGetProfileData profileData, IBotCreator creator, int count, ITokenGetter token, Reservation reservation)
        {
            var native = (GetProfileDataParams)profileData;
            var prepared = new PreparedSquad { Parameters = native.SpawnParams };
            AddPreparedRole(prepared, native.Role, native.BotDifficulty, count);
            var profiles = await LoadFreshProfiles(prepared, creator, _generation, reservation.Cancellation.Token);
            if (reservation.Expired) throw new OperationCanceledException();
            var data = BotCreationData.CreateWithoutProfile(profileData);
            data._botCreator = creator;
            data._tokenGetter = token;
            foreach (var profile in profiles) data._profilesIdsHash.Add(profile.Id);
            data.AddProfiles(profiles);
            Register(reservation, data);
            return data;
        }

        private int[] ExpandTargets(PopulationTargets targets)
        {
            var result = new[] { targets.blackDivision, targets.isb, targets.chaosInsurgency, targets.raiders, (targets.pmcs + 1) / 2, targets.pmcs / 2, 0 };
            var flags = _state.populationFactions;
            if (flags != null)
                for (int faction = 0; faction < Other; faction++)
                {
                    int flag = faction == Bear ? Usec : faction;
                    if (flag < flags.Length && !flags[flag]) result[faction] = 0;
                }
            return result;
        }
        private void SelectBlackDivisionHunter(bool reinforcement)
        {
            if (reinforcement)
            {
                var anchor = _slots.Find(slot => slot.Faction == BlackDivision && slot.Normal && slot.Reinforcement && slot.Deployed?.Alive > 0);
                if (anchor != null)
                {
                    _blackDivisionHunter = anchor;
                    return;
                }
            }
            if (!(_blackDivisionHunter?.Deployed?.Alive > 0))
                _blackDivisionHunter = _slots.Find(slot => slot.Faction == BlackDivision && slot.Deployed?.Alive > 0);
        }
        private bool TryRefill(bool reinforcement)
        {
            var targets = reinforcement ? _reinforcementTargets : _normalTargets;
            var spawner = _bossSpawner._spawner;
            var token = spawner.GetCancelToken();
            if (token.IsCancellationRequested) return false;
            foreach (var slot in _slots)
            {
                int faction = slot.Faction;
                if (!(reinforcement ? slot.Reinforcement : slot.Normal) || Time.unscaledTime < slot.RetryAt ||
                    slot.Prepared == null || slot.Prepared.Failed || slot.Prepared.Claimed ||
                    !ManagedSquadPopulation.CanRefill(slot.Deployed != null, _global, Config.maximumBots, _current[faction], targets[faction], slot.Size))
                    continue;
                var prepared = slot.Prepared;
                var wave = prepared.Wave;
                var counts = WaveCounts(wave);
                if (counts[faction] != slot.Size || counts.Sum() != slot.Size)
                {
                    slot.RetryAt = Time.unscaledTime + 5f;
                    if (!_reported)
                    {
                        _reported = true;
                        Plugin.LogSource.LogError("ISB population queue: native roles did not match the complete squad.");
                    }
                    continue;
                }
                var job = new Job
                {
                    BossSpawner = _bossSpawner, Spawner = spawner, Wave = wave, Slot = slot, Counts = counts, Token = token,
                    Parameters = prepared.Parameters
                };
                prepared.Claimed = true;
                slot.Prepared = null;
                _ = job.Done.Task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                Dispatch(job);
                return true;
            }
            return false;
        }
        private static BossLocationSpawn CreateSquadWave(SquadSlot slot)
        {
            string leader = Leaders[slot.Faction];
            var wave = new BossLocationSpawn
            {
                BossName = leader, BossDifficult = "hard", BossEscortDifficult = "hard", BossChance = 100f,
                BossEscortType = slot.Faction == BlackDivision ? "blackDivAssault" : leader,
                BossEscortAmount = (slot.Size - 1).ToString(CultureInfo.InvariantCulture), BossZone = "",
                IgnoreMaxBots = true, Time = -1f, TriggerId = RefillTrigger, TriggerName = ""
            };
            if (slot.Faction == Isb && slot.Size > 1)
            {
                var available = (WildSpawnType[])IsbFollowers.Clone();
                var supports = new WildSpawnSupports[slot.Size - 1];
                for (int i = 0; i < supports.Length; i++)
                {
                    int choice = UnityEngine.Random.Range(i, available.Length);
                    var role = available[choice];
                    available[choice] = available[i];
                    supports[i] = new WildSpawnSupports { BossEscortType = role, BossEscortAmount = 1, BossEscortDifficult = new[] { "hard" } };
                }
                wave.BossEscortAmount = "0";
                wave.Supports = supports;
            }
            wave.Init();
            return wave;
        }
        private async Task Fetch(int generation)
        {
            try
            {
                var bytes = await RequestHandler.HttpClient.GetAsync("/ISB/labsPreBlackoutEvent/status").ConfigureAwait(false);
                var state = JsonConvert.DeserializeObject<State>(Encoding.UTF8.GetString(bytes));
                if (state?.populationReady == false)
                {
                    ISBAishiReputationWorker.Enqueue(() =>
                    {
                        if (generation != _generation) return;
                        _fetching = false;
                        _nextFetch = Time.unscaledTime + 1f;
                    });
                    return;
                }
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
