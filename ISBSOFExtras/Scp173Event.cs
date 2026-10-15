using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
#if ISB_SPT41
using ScpDamageInfo = EFT.Ballistics.DamageInfo;
using ScpCamera = EFT.CameraControl.CameraManager;
#else
using ScpDamageInfo = DamageInfoStruct;
using ScpCamera = CameraClass;

#endif
namespace ISBSSOFExtra
{
    internal static class FreecamObservation
    {
        private static System.Reflection.FieldInfo _instance;
        private static System.Reflection.PropertyInfo _active;
        private static bool _resolved;
        internal static bool Active
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.terkoiz.freecam", out var plugin))
                    {
                        var type = plugin.Instance.GetType();
                        _instance = type.GetField("FreecamControllerInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        _active = type.Assembly.GetType("Terkoiz.Freecam.FreecamController")?.GetProperty("IsFreecamActive");
                    }
                }

                if (_instance == null || _active == null)
                    return false;
                var controller = _instance.GetValue(null) as UnityEngine.Object;
                if (controller == null)
                    return false;
                try
                {
                    return (bool)_active.GetValue(controller, null);
                }
                catch (System.Reflection.TargetInvocationException)
                {
                    return false;
                }
            }
        }
    }

    internal sealed partial class Scp173Event : IDisposable
    {
        private sealed class State
        {
            public string entity;
            public long sequence;
            public bool active, observed, selected, awakened, containment, moving;
            public NightmareFacilityState facility;
            public double sentAt;
            public float x, y, z, yaw;
            public Scp173Config config;
            public string victim, sound;
        }

        private sealed class Observation
        {
            public string entity, player;
            public long sequence;
            public bool visible, freecam;
            public float[][] views;
            public double sentAt;
        }

        private sealed class Observer
        {
            internal bool visible, freecam;
            internal float received;
            internal long sequence;
            internal float[][] views;
        }

        private sealed class DebugRequest
        {
            public string entity, player, action;
            public float x, y, z;
        }

        private sealed class Configuration
        {
            internal Scp173Config config;
            internal bool containment;
        }

        private sealed class AudioLoad
        {
            internal readonly Dictionary<string, PcmWavData> clips = new Dictionary<string, PcmWavData>();
            internal Exception error;
        }

        private readonly string _folder;
        private readonly ManualLogSource _log;
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<string, Observer> _observers = new Dictionary<string, Observer>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly Scp173Sight _sight = new Scp173Sight();
        private readonly HashSet<string> _attacks = new HashSet<string>();
        private GameWorld _world;
        private Scp173Config _config;
        private IExtrasNetworkBridge _network;
        private GameObject _body;
        private Mesh _mesh;
        private Material _material;
        private Texture2D _texture, _normal, _specular;
        private AudioSource _audio, _movementAudio;
        private AudioSource _audioOther, _fadingAudio;
        private float _audioFadeStarted, _audioFadeVolume;
        private bool _audioReady;
        private float _nextAudioLoad;
        private Task<AudioLoad> _audioRequest;
        private CancellationTokenSource _audioCancellation;
        private string _pendingSound;
        private Task<Configuration> _configRequest;
        private string _map;
        private Scp173Identity _identity;
        private bool _authority, _headless, _selected, _attempted, _active, _observed, _failed;
        private bool _awakened, _containmentRaid, _raidStarted;
        private int _teleports;
        private float _activatedAt, _nextTeleportTry;
        private string _lastMoveSound, _lastTeleportSound, _lastKillSound;
        private string _entity;
        private Vector3 _position, _lastPathTarget, _spawnPosition;
        private Vector3[] _corners = Array.Empty<Vector3>();
        private int _corner;
        private long _sequence, _receivedSequence, _reportSequence;
        private float _raidStart, _nextTick, _lastTick, _nextReport, _lastObserved, _safeUntil, _nextTeleport, _nextAttack, _nextPath, _nextMoveAudio, _nextSnapshot, _nextSpawnTry;
        private int _geometryMask;
        private double _cpuMilliseconds;
        private double _receivedServerTime;
        private float _receivedLocalTime;
        private long _ticks;
        private float _nextDebugRequest;
        internal bool DebugActive => _active && !_failed && _world != null;
        internal bool DebugObserved => _observed;
        internal Vector3 DebugPosition => _position;

        internal bool RequestDebug(string action, Vector3 position)
        {
            if (!DebugActive || !Finite(position))
                return false;
            if (_authority)
                return ApplyDebug(action, position);
            var player = _world.MainPlayer;
            if (!_config.allowClientDebug || !Alive(player) || !player.IsYourPlayer || _headless || !_network.Ready)
                return false;
            return _network.SendToServer("SC173|D|" + JsonConvert.SerializeObject(new DebugRequest { entity = _entity, player = player.ProfileId, action = action, x = position.x, y = position.y, z = position.z }));
        }

        private static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);
        private bool ApplyDebug(string action, Vector3 point)
        {
            if (!DebugActive || !_authority)
                return false;
            if (action == "kill")
            {
                _active = false;
                _attempted = true;
                StopAudio();
                _body?.SetActive(false);
                _identity?.Dispose();
                SendState();
                return true;
            }

            if (action != "teleport" || !DebugPoint(point, out var destination))
                return false;
            SetPosition(destination);
            _corners = Array.Empty<Vector3>();
            _nextPath = 0f;
            _lastObserved = Time.realtimeSinceStartup;
            _safeUntil = _lastObserved + Math.Max(2f, _config.afterTeleportGraceSeconds);
            _nextTeleport = _lastObserved + _config.teleportCooldownSeconds;
            SendState(sound: _config.teleportAudio);
            return true;
        }

        private bool DebugPoint(Vector3 point, out Vector3 result)
        {
            result = default;
            if (!Finite(point))
                return false;
            if (!NavMesh.SamplePosition(point, out var hit, 1.5f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - point.y) > 1.5f)
                return GroundPoint(point, true, out result);
            result = hit.position;
            return !Physics.CheckCapsule(result + Vector3.up * _config.bodyRadius, result + Vector3.up * (_config.height - _config.bodyRadius), _config.bodyRadius, _geometryMask, QueryTriggerInteraction.Ignore) && Physics.Raycast(result + Vector3.up * .5f, Vector3.down, 1f, _geometryMask, QueryTriggerInteraction.Ignore);
        }

        private bool DebugNearby(Vector3 origin, float distance, out Vector3 result)
        {
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI * 2f / 24f;
                var candidate = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * distance;
                if (!DebugPoint(candidate, out result))
                    continue;
                if (NavMesh.CalculatePath(origin, result, NavMesh.AllAreas, _path) && _path.status == NavMeshPathStatus.PathComplete || Mathf.Abs(origin.y - result.y) < .4f && ClearLine(origin + Vector3.up, result + Vector3.up))
                    return true;
            }

            result = default;
            return false;
        }

        private bool GroundPoint(Vector3 point, bool checkBody, out Vector3 result)
        {
            result = default;
            if (!Finite(point) || !Physics.Raycast(point + Vector3.up * .5f, Vector3.down, out var hit, 1f, _geometryMask, QueryTriggerInteraction.Ignore) || hit.normal.y < .7f || Mathf.Abs(hit.point.y - point.y) > .35f)
                return false;
            result = hit.point + Vector3.up * .02f;
            return !checkBody || !Physics.CheckCapsule(result + Vector3.up * (_config.bodyRadius + .05f), result + Vector3.up * (_config.height - _config.bodyRadius), _config.bodyRadius, _geometryMask, QueryTriggerInteraction.Ignore);
        }

        internal bool DebugBringTo(Player player, float distance)
        {
            if (!DebugActive || !Alive(player) || !DebugNearby(player.Position, Mathf.Clamp(distance, _config.killDistance + 2f, 30f), out var point))
                return false;
            return RequestDebug("teleport", point);
        }

        internal bool DebugGoTo(Player player)
        {
            if (!DebugActive || !Alive(player) || !player.IsYourPlayer || _headless || !DebugNearby(_position, Math.Max(5f, _config.killDistance + 2f), out var point))
                return false;
            player.Teleport(point);
            return true;
        }

        internal Scp173Event(string folder, ManualLogSource log)
        {
            _folder = folder;
            _log = log;
        }

        internal object Diagnostic => new
        {
            active = _active,
            selected = _selected,
            attempted = _attempted,
            failed = _failed,
            entity = _entity,
            authority = _authority,
            headless = _headless,
            observed = _observed,
            awakened = _awakened,
            containment = _containmentRaid,
            raidStarted = _raidStarted,
            activatedAt = _activatedAt,
            lookSeconds = _seenSince < 0f ? 0f : Time.realtimeSinceStartup - _seenSince,
            moving = _moving,
            chases = _chases,
            facility = Facility,
            teleportDueIn = _awakened ? Math.Max(0f, _nextTeleport - Time.realtimeSinceStartup) : -1f,
            teleports = _teleports,
            position = new[]
            {
                _position.x,
                _position.y,
                _position.z
            },
            sequence = _sequence,
            receivedSequence = _receivedSequence,
            observers = _observers.Count,
            ticks = _ticks,
            cpuTotalMs = _cpuMilliseconds,
            cpuMeanTickMs = _ticks == 0 ? 0 : _cpuMilliseconds / _ticks,
            profileId = _identity?.ProfileId,
            nickname = _identity?.Nickname,
            nativeBrain = false,
            map = _map,
            spawnPosition = new[]
            {
                _spawnPosition.x,
                _spawnPosition.y,
                _spawnPosition.z
            },
            configuration = "server",
            renderers = _body == null ? 0 : _body.GetComponentsInChildren<Renderer>().Length,
            audioSources = _body == null ? 0 : _body.GetComponentsInChildren<AudioSource>().Length,
            audioClips = _clips.Count,
            volume = _config?.audioVolume,
            speed = _config?.moveSpeed,
            teleportInterval = _config?.teleportCooldownSeconds
        };

        internal void Tick(IExtrasNetworkBridge network, string reportedLocation)
        {
            TickAudioFade();
            _network = network;
            _authority = !network.Active || network.IsSinglePlayer || network.IsServer;
            _headless = network.Active && network.IsHeadless;
            var game = Singleton<AbstractGame>.Instance;
            var world = Singleton<GameWorld>.Instance;
            if (game == null || world == null || game.Status != GameStatus.Started && game.Status != GameStatus.Running)
            {
                if (_world != null)
                    Reset();
                return;
            }

            if (!ReferenceEquals(_world, world))
            {
                string location = reportedLocation;
                if (string.IsNullOrWhiteSpace(location))
                    location = world.MainPlayer?.Location;
                if (string.IsNullOrWhiteSpace(location))
                    for (int i = 0; i < world.AllAlivePlayersList.Count; i++)
                        if (!string.IsNullOrWhiteSpace(world.AllAlivePlayersList[i]?.Location))
                        {
                            location = world.AllAlivePlayersList[i].Location;
                            break;
                        }

                if (string.IsNullOrWhiteSpace(location))
                    location = game.LocationObjectId;
                if (string.IsNullOrWhiteSpace(location))
                    return;
                if (location == "5b0fc42d86f7744a585f9105")
                    location = "laboratory";
                Begin(world, location.Trim());
            }

            if (_configRequest != null && _configRequest.IsCompleted)
                FinishConfiguration();
            if (!_raidStarted && game.Status == GameStatus.Started)
            {
                _raidStarted = true;
                _raidStart = Time.realtimeSinceStartup;
            }

            if (_failed || _config == null || _authority && !_config.enabled)
                return;
            if (_authority && !_selected || !_authority && _receivedSequence != 0 && !_selected && !_active)
                return;
            float now = Time.realtimeSinceStartup;
            if (!_authority && now >= _nextReport)
            {
                ScheduleNext(ref _nextReport, now, 1f / _config.observationReportHz);
                if (!string.IsNullOrEmpty(_entity))
                    ReportObservation();
                if (!_active && now >= _nextSnapshot)
                {
                    _nextSnapshot = now + 2f;
                    _network.SendToServer("SC173|Q");
                }
            }

            if (now < _nextTick)
                return;
            double started = Stopwatch.GetTimestamp();
            float delta = _lastTick == 0f ? 1f / _config.logicHz : Math.Min(.1f, Math.Max(0f, now - _lastTick));
            _lastTick = now;
            ScheduleNext(ref _nextTick, now, 1f / _config.logicHz);
            try
            {
                EnsureAudioReady(now);
                if (_authority)
                {
                    bool earlySpawn = _containmentRaid && _config.spawnOnContainmentBreach;
                    if (!_attempted && _selected && now >= _nextSpawnTry && (earlySpawn || _raidStarted && now - _raidStart >= _config.spawnDelaySeconds))
                        Spawn(now);
                    if (_active && _raidStarted)
                    {
                        bool previousMoving = _moving;
                        TickFacilityBehaviour(now);
                        if (_active && Facility.containmentAt < 0d && (Facility.awakeAt < 0d || now >= Facility.awakeAt + _config.activationBlackoutSeconds))
                            Advance(now, delta);
                        if (!_moving) SetMoving(false);
                        if (previousMoving != _moving) SendState();
                        if (now >= _nextSnapshot) { _nextSnapshot=now+.2f; SendState(); }
                    }
                    else if (now >= _nextSnapshot)
                    {
                        _nextSnapshot = now + .2f;
                        SendState();
                    }
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                _active = false;
                StopAudio();
                if (_body != null)
                    _body.SetActive(false);
                _log.LogError("ISB SCP-173 stopped safely: " + ex);
                if (_authority)
                    SendState();
            }

            _ticks++;
            _cpuMilliseconds += (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        }

        private static void ScheduleNext(ref float next, float now, float interval)
        {
            if (next == 0f)
                next = now;
            next += interval;
            if (next <= now)
                next += (Mathf.Floor((now - next) / interval) + 1f) * interval;
        }

        private void Begin(GameWorld world, string map)
        {
            Reset();
            _world = world;
            _map = map;
            _raidStart = Time.realtimeSinceStartup;
            _geometryMask = LayerMask.GetMask("LowPolyCollider", "HighPolyCollider", "Terrain", "Default");
            try
            {
                _config = new Scp173Config
                {
                    enabled = false
                };
                if (_authority)
                {
                    _entity = Guid.NewGuid().ToString("N");
                    _configRequest = Task.Run(() =>
                    {
                        string json = RequestHandler.GetJson("/ISB/scp173/config");
                        if (json == null || json.Length > 65536)
                            throw new InvalidDataException("SCP-173 server configuration exceeds 64 KiB.");
                        var config = JsonConvert.DeserializeObject<Scp173Config>(json);
                        if (config == null)
                            throw new InvalidDataException("SCP-173 server configuration is empty.");
                        config.Normalize();
                        bool containment = false;
                        if (config.enabled && map == "laboratory")
                        {
                            string statusJson = RequestHandler.GetJson("/ISB/labsPreBlackoutEvent/status");
                            if (statusJson == null || statusJson.Length > 65536)
                                throw new InvalidDataException("SCP-173 containment status unavailable.");
                            containment = JsonConvert.DeserializeObject<LabsPreBlackoutEventStatus>(statusJson)?.active == true;
                        }

                        return new Configuration
                        {
                            config = config,
                            containment = containment
                        };
                    });
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                _log.LogError("ISB SCP-173 configuration unavailable: " + ex.Message);
            }
        }

        private void FinishConfiguration()
        {
            var request = _configRequest;
            _configRequest = null;
            try
            {
                var result = request.GetAwaiter().GetResult();
                _config = result.config;
                _containmentRaid = result.containment;
                bool allowed = Array.Exists(_config.maps, x => string.Equals(x, _map, StringComparison.OrdinalIgnoreCase));
                _selected = allowed && _config.enabled && _random.NextDouble() * 100d < _config.chancePercent;
                SendState(full: true);
            }
            catch (Exception ex)
            {
                _failed = true;
                _log.LogError("ISB SCP-173 server configuration unavailable: " + ex.GetBaseException().Message);
            }
        }

        private void Spawn(float now)
        {
            if (IdentityTemplate() == null)
            {
                _nextSpawnTry = now + .5f;
                return;
            }
            Player target = FindTarget();
            if (target == null)
                return;
            _nextSpawnTry = now + 5f;
            Vector3 point;
            bool valid;
            bool fixedSpawn = _config.fixedSpawn || _containmentRaid && _config.spawnOnContainmentBreach;
            if (fixedSpawn)
            {
                if (!_config.fixedSpawnPoints.TryGetValue(_map, out var fixedPoint))
                    throw new InvalidDataException("SCP-173 has no fixed spawn configured for " + _map + ".");
                valid = GroundPoint(new Vector3(fixedPoint.x, fixedPoint.y, fixedPoint.z), true, out point);
            }
            else
                valid = FindSafePoint(target.Position, _config.initialMinDistance, _config.initialMaxDistance, out point);
            if (!valid)
            {
                _attempted = now - _raidStart > (_containmentRaid ? 0f : _config.spawnDelaySeconds) + 60f;
                if (_attempted && fixedSpawn)
                    throw new InvalidOperationException("SCP-173 fixed spawn is blocked or has no safe navigation point on " + _map + ".");
                return;
            }

            _attempted = true;
            _spawnPosition = point;
            CreateBody(point);
            _active = true;
            ISBSpecialForcesAudio.ReportScp173Spawn();
            _awakened = !_config.activateWhenSeen;
            _activatedAt = _awakened ? now : 0f;
            if (_awakened) StartAwakening(now);
            _lastObserved = now;
            _safeUntil = now + Math.Max(2f, _config.afterTeleportGraceSeconds);
            _nextTeleport = now + _config.teleportCooldownSeconds;
            SendState();
        }

        private Player FindTarget()
        {
            Player closest = null;
            float distance = float.MaxValue;
            var players = _world.AllAlivePlayersList;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!Alive(p) || p.IsAI && !_config.killBots)
                    continue;
                if (_headless && p.IsYourPlayer)
                    continue;
                float d = (_active ? p.Position - _position : Vector3.zero).sqrMagnitude;
                if (!_active && p.IsAI && closest != null && !closest.IsAI)
                    continue;
                if (!_active && !p.IsAI && closest != null && closest.IsAI || d < distance)
                {
                    distance = d;
                    closest = p;
                }
            }

            return closest;
        }

        private static bool Alive(Player p) => p != null && p.HealthController?.IsAlive == true;
        private Player TeleportTarget()
        {
            Player target = null;
            int count = 0;
            var players = _world.AllAlivePlayersList;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!Alive(p) || p.IsAI && !_config.killBots || _headless && p.IsYourPlayer)
                    continue;
                if (_random.Next(++count) == 0)
                    target = p;
            }

            return target;
        }

        private string HumanObserver(float now)
        {
            var players = _world.AllAlivePlayersList;
            for (int i=0;i<players.Count;i++)
                if(players[i]?.ProfileId==_triggerObserver && HumanObserves(players[i],now))return _triggerObserver;
            for (int i=0;i<players.Count;i++)
                if(HumanObserves(players[i],now))return players[i].ProfileId;
            return null;
        }

        private bool HumanObserves(Player player,float now)
        {
            if(!Alive(player) || player.IsAI || _headless && player.IsYourPlayer)return false;
            if(player.IsYourPlayer)return LocalSees(_position,false);
            return _network.Active && !_network.IsSinglePlayer && _observers.TryGetValue(player.ProfileId,out var report) && now-report.received<=_config.observerTimeoutSeconds && !report.freecam && RemoteSees(report.views,_position,false);
        }

        private bool ObservedAt(Vector3 position, float now)
        {
            var players = _world.AllAlivePlayersList;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!Alive(p) || _headless && p.IsYourPlayer)
                    continue;
                if (p.IsAI)
                {
                    if (_config.botsObserve && BotSees(p, position))
                        return true;
                }
                else if (p.IsYourPlayer)
                {
                    if (LocalSees(position))
                        return true;
                }
                else if ((_network.Active && !_network.IsSinglePlayer))
                {
                    if (!_observers.TryGetValue(p.ProfileId, out var observer) || now - observer.received > _config.observerTimeoutSeconds)
                        return true;
                    if (!observer.freecam && RemoteSees(observer.views, position))
                        return true;
                }
            }

            return false;
        }

        private bool LocalSees(Vector3 position, bool missingMeansVisible = true)
        {
            var player = _world.MainPlayer;
            if (!Alive(player) || _headless || !player.IsYourPlayer || FreecamObservation.Active)
                return false;
            Camera camera = ScpCamera.Instance?.Camera;
            if (camera == null || !camera.isActiveAndEnabled)
                return missingMeansVisible;
            if (Sees(camera, position))
                return true;
            var optic = ScpCamera.Instance?.OpticCameraManager;
            if (optic?.CurrentOpticSight != null && player.HandsController is Player.FirearmController firearm && firearm.IsAiming)
            {
                var opticCamera = optic.Camera;
                if (opticCamera != null && opticCamera.isActiveAndEnabled && Sees(opticCamera, position))
                    return true;
            }

            return false;
        }

        private bool Sees(Camera camera, Vector3 position)
        {
            if ((camera.transform.position - position).sqrMagnitude > _config.observationDistance * _config.observationDistance)
                return false;
            for (int i = 0; i < 9; i++)
            {
                var point = ObservationPoint(position, camera.transform.position, i);
                var view = camera.WorldToViewportPoint(point);
                if (view.z > 0f && view.x >= -.015f && view.x <= 1.015f && view.y >= -.015f && view.y <= 1.015f && ClearObservationLine(camera.transform.position, point))
                    return true;
            }

            return false;
        }

        private Vector3 ObservationPoint(Vector3 position, Vector3 origin, int index)
        {
            var point = position + Vector3.up * _config.height * (.15f + index % 3 * .35f);
            if (index < 3)
                return point;
            var side = Vector3.Cross(Vector3.up, position - origin).normalized;
            return point + side * Math.Max(_config.bodyRadius, _config.height * .25f) * (index < 6 ? -1f : 1f);
        }

        private static float[] View(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled)
                return null;
            var values = new float[19];
            var matrix = camera.projectionMatrix * camera.worldToCameraMatrix;
            for (int i = 0; i < 16; i++)
                values[i] = matrix[i];
            var p = camera.transform.position;
            values[16] = p.x;
            values[17] = p.y;
            values[18] = p.z;
            return values;
        }

        private float[][] LocalViews()
        {
            if (FreecamObservation.Active)
                return Array.Empty<float[]>();
            var main = View(ScpCamera.Instance?.Camera);
            var p = _world.MainPlayer;
            var optic = ScpCamera.Instance?.OpticCameraManager;
            var scope = optic?.CurrentOpticSight != null && p?.HandsController is Player.FirearmController firearm && firearm.IsAiming ? View(optic.Camera) : null;
            return scope == null ? new[]
            {
                main
            }

            : new[]
            {
                main,
                scope
            };
        }

        private bool RemoteSees(float[][] views, Vector3 position, bool missingMeansVisible = true)
        {
            if (views == null || views.Length == 0 || views[0] == null)
                return missingMeansVisible;
            foreach (var view in views)
            {
                if (view == null || view.Length != 19)
                    continue;
                var origin = new Vector3(view[16], view[17], view[18]);
                if ((origin - position).sqrMagnitude > _config.observationDistance * _config.observationDistance)
                    continue;
                var matrix = new Matrix4x4();
                for (int i = 0; i < 16; i++)
                    matrix[i] = view[i];
                for (int i = 0; i < 9; i++)
                {
                    var point = ObservationPoint(position, origin, i);
                    var clip = matrix * new Vector4(point.x, point.y, point.z, 1f);
                    if (clip.w > 0f && Math.Abs(clip.x) <= clip.w * 1.03f && Math.Abs(clip.y) <= clip.w * 1.03f && ClearObservationLine(origin, point))
                        return true;
                }
            }

            return false;
        }

        private bool BotSees(Player p, Vector3 position, float angle = -1f)
        {
            if ((p.Position - position).sqrMagnitude > _config.observationDistance * _config.observationDistance)
                return false;
            var origin = p.PlayerBones?.Head != null ? p.PlayerBones.Head.position : p.Position + Vector3.up * 1.6f;
            var forward = Quaternion.Euler(p.Rotation.y, p.Rotation.x, 0f) * Vector3.forward;
            for (int i = 0; i < 3; i++)
            {
                var point = position + Vector3.up * _config.height * (.15f + i * .35f);
                if (Vector3.Angle(forward, point - origin) <= (angle < 0f ? _config.botViewAngle : angle) * .5f && ClearObservationLine(origin, point))
                    return true;
            }

            return false;
        }

        private bool ClearObservationLine(Vector3 origin, Vector3 point) => _sight.ClearLine(origin, point, _geometryMask, _body?.transform);

        private bool ClearLine(Vector3 origin, Vector3 point)
        {
            var delta = point - origin;
            if (delta.sqrMagnitude < .001f)
                return true;
            if (!Physics.Raycast(origin, delta.normalized, out var hit, delta.magnitude, _geometryMask, QueryTriggerInteraction.Ignore))
                return true;
            return _body != null && hit.transform.IsChildOf(_body.transform);
        }

        private bool FindSafePoint(Vector3 origin, float min, float max, out Vector3 result)
        {
            for (int i = 0; i < _config.spawnAttempts; i++)
            {
                float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Lerp(min, max, (float)_random.NextDouble());
                var candidate = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, NavMesh.AllAreas))
                    continue;
                var point = hit.position;
                if (Mathf.Abs(point.y - origin.y) > 1.5f || (point - origin).sqrMagnitude < min * min || (point - origin).sqrMagnitude > max * max)
                    continue;
                if (Physics.CheckCapsule(point + Vector3.up * _config.bodyRadius, point + Vector3.up * (_config.height - _config.bodyRadius), _config.bodyRadius, _geometryMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (!Physics.Raycast(point + Vector3.up * .5f, Vector3.down, 1f, _geometryMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (!NavMesh.CalculatePath(origin, point, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
                    continue;
                bool unsafePoint = ObservedAt(point, Time.realtimeSinceStartup);
                var players = _world.AllAlivePlayersList;
                for (int k = 0; !unsafePoint && k < players.Count; k++)
                    if (Alive(players[k]) && (players[k].Position - point).sqrMagnitude < Mathf.Pow(_config.killDistance + 1f, 2f))
                        unsafePoint = true;
                if (unsafePoint)
                    continue;
                result = point;
                return true;
            }

            result = default;
            return false;
        }

        private void Advance(float now, float elapsed)
        {
            bool wasMoving = _moving;
            _moving = false;
            if (AwaitAwakening(now)) return;
            var tracked = ChaseTarget(now);
            if (tracked != null && ClearObservationLine(_position + Vector3.up, tracked.Position + Vector3.up))
            {
                _lastKnownTarget = tracked.Position;
            }
            bool previous = _observed;
            _observed = ObservedAt(_position, now);
            if (_observed)
                _lastObserved = now;
            if (wasMoving && _observed) { _chaseBegan = -1f; _nextChase = now + .2f; _chases++; }
            if (_observed || now - _lastObserved < _config.unobservedGraceSeconds || now < _safeUntil)
            {
                if (wasMoving || previous != _observed || now >= _nextSnapshot)
                {
                    _nextSnapshot = now + .2f;
                    SendState();
                }

                return;
            }

            var target = ChaseTarget(now);
            if (target == null)
                return;
            float distance = Vector3.Distance(target.Position, _position);
            if ((_config.periodicTeleport || distance > _config.teleportTriggerDistance) && now >= _nextTeleport && now >= _nextTeleportTry)
            {
                _nextTeleportTry = now + 5f;
                var teleportTarget = _config.periodicTeleport ? TeleportTarget() : target;
                if (TryTeleport(now, teleportTarget)) return;
            }

            if (distance <= _config.killDistance)
            {
                if ((target.IsAI ? _config.killBots : _config.killPlayers) && now >= _nextAttack && ClearLine(_position + Vector3.up, target.Position + Vector3.up))
                {
                    _nextAttack = now + _config.killCooldownSeconds;
                    SendState(target.ProfileId, ChooseSound(_config.killAudio, _config.killAudioVariants, ref _lastKillSound));
                    ApplyAttack(target.ProfileId, _sequence);
                }

                return;
            }

            if (!ChaseDestination(target, now, out var destination)) return;
            if (now >= _nextPath || (_lastPathTarget - destination).sqrMagnitude > 4f)
            {
                _nextPath = now + _config.pathRefreshSeconds;
                _lastPathTarget = destination;
                if (NavMesh.CalculatePath(_position, destination, NavMesh.AllAreas, _path) && _path.status != NavMeshPathStatus.PathInvalid)
                {
                    _corners = _path.corners;
                    _corner = 1;
                }
                else _corners = Array.Empty<Vector3>();
            }
            bool direct = _corner >= _corners.Length;
            if (direct && (Math.Abs(destination.y-_position.y) > .4f || !ClearLine(_position+Vector3.up, destination+Vector3.up))) return;
            var proposed = Vector3.MoveTowards(_position, direct ? destination : _corners[_corner], _config.moveSpeed * elapsed);
            if (!CanStep(proposed, out proposed)) return;
            if (ObservedAt(proposed, now))
            {
                _lastObserved = now;
                _observed = true;
                SendState();
                return;
            }

            SetMoving(true);
            SetPosition(proposed);
            if (!direct && (_corners[_corner] - _position).sqrMagnitude < .04f)
                _corner++;
            string sound = null;
            if (now >= _nextMoveAudio)
            {
                _nextMoveAudio = now + _config.moveAudioCooldownSeconds;
                sound = ChooseSound(_config.moveAudio, _config.moveAudioVariants, ref _lastMoveSound);
            }

            SendState(sound: sound);
        }

        private void SetPosition(Vector3 position)
        {
            var delta = position - _position;
            _position = position;
            if (_body == null)
                return;
            _body.transform.position = position;
            delta.y = 0f;
            if (delta.sqrMagnitude > .001f)
                _body.transform.rotation = Quaternion.LookRotation(delta);
        }

        private void ReportObservation()
        {
            var p = _world.MainPlayer;
            if (!Alive(p) || !p.IsYourPlayer || _headless)
                return;
            var observation = new Observation
            {
                entity = _entity,
                player = p.ProfileId,
                sequence = ++_reportSequence,
                visible = LocalSees(_position),
                freecam = FreecamObservation.Active,
                views = LocalViews(),
                sentAt = _receivedServerTime + Time.realtimeSinceStartup - _receivedLocalTime
            };
            _network.SendToServer("SC173|O|" + JsonConvert.SerializeObject(observation));
        }

        internal bool Receive(string payload)
        {
            if (payload == null || !payload.StartsWith("SC173|", StringComparison.Ordinal))
                return false;
            if (_world == null || _failed || payload.Length > 12000)
                return true;
            try
            {
                if (payload == "SC173|Q")
                {
                    if (_authority)
                        SendState(full: true);
                    return true;
                }

                if (payload.StartsWith("SC173|C|", StringComparison.Ordinal))
                {
                    if (!_authority || Time.realtimeSinceStartup < _nextDebugRequest) return true;
                    var request = JsonConvert.DeserializeObject<DebugRequest>(payload.Substring(8));
                    if (request == null || request.entity != _entity || string.IsNullOrEmpty(request.player) || request.player.Length > 64) return true;
                    foreach (var player in _world.AllAlivePlayersList)
                        if (player.ProfileId == request.player) { _nextDebugRequest = Time.realtimeSinceStartup + .2f; TryContain(player); break; }
                    return true;
                }
                if (payload.StartsWith("SC173|D|", StringComparison.Ordinal))
                {
                    if (!_authority || _config?.allowClientDebug != true || !DebugActive || Time.realtimeSinceStartup < _nextDebugRequest)
                        return true;
                    var request = JsonConvert.DeserializeObject<DebugRequest>(payload.Substring(8));
                    if (request == null || request.entity != _entity || string.IsNullOrEmpty(request.player) || request.player.Length > 64 || request.action != "kill" && request.action != "teleport")
                        return true;
                    var point = new Vector3(request.x, request.y, request.z);
                    if (!Finite(point))
                        return true;
                    foreach (var player in _world.AllAlivePlayersList)
                        if (Alive(player) && !player.IsAI && !(_headless && player.IsYourPlayer) && player.ProfileId == request.player)
                        {
                            _nextDebugRequest = Time.realtimeSinceStartup + .1f;
                            ApplyDebug(request.action, point);
                            break;
                        }

                    return true;
                }

                if (payload.StartsWith("SC173|O|", StringComparison.Ordinal) && _authority)
                {
                    var report = JsonConvert.DeserializeObject<Observation>(payload.Substring(8));
                    if (report == null || report.entity != _entity || string.IsNullOrEmpty(report.player) || report.player.Length > 64)
                        return true;
                    double age = Time.realtimeSinceStartup - report.sentAt;
                    if (double.IsNaN(age) || double.IsInfinity(age) || age > _config.observerTimeoutSeconds || age < -1d)
                        return true;
                    if (report.views == null || report.views.Length > 2)
                        return true;
                    foreach (var view in report.views)
                    {
                        if (view == null)
                            continue;
                        if (view.Length != 19)
                            return true;
                        foreach (float value in view)
                            if (float.IsNaN(value) || float.IsInfinity(value))
                                return true;
                    }

                    var players = _world.AllAlivePlayersList;
                    bool valid = false;
                    for (int i = 0; i < players.Count; i++)
                        if (Alive(players[i]) && !players[i].IsAI && !players[i].IsYourPlayer && players[i].ProfileId == report.player)
                        {
                            valid = true;
                            break;
                        }

                    if (!valid)
                        return true;
                    if (!_observers.TryGetValue(report.player, out var observer))
                        _observers[report.player] = observer = new Observer();
                    if (report.sequence <= observer.sequence)
                        return true;
                    observer.visible = report.visible;
                    observer.freecam = report.freecam;
                    observer.received = Time.realtimeSinceStartup;
                    observer.sequence = report.sequence;
                    observer.views = report.views;
                }
                else if (payload.StartsWith("SC173|S|", StringComparison.Ordinal) && !_authority)
                {
                    var state = JsonConvert.DeserializeObject<State>(payload.Substring(8));
                    if (state == null || string.IsNullOrEmpty(state.entity) || state.entity.Length > 64 || state.sequence <= _receivedSequence || _receivedSequence == 0 && state.config == null)
                        return true;
                    if (float.IsNaN(state.x) || float.IsNaN(state.y) || float.IsNaN(state.z) || float.IsNaN(state.yaw) || float.IsInfinity(state.x) || float.IsInfinity(state.y) || float.IsInfinity(state.z) || float.IsInfinity(state.yaw) || double.IsNaN(state.sentAt) || double.IsInfinity(state.sentAt))
                        return true;
                    if (state.active && _body == null && IdentityTemplate() == null)
                        return true;
                    if (state.config != null)
                    {
                        state.config.Normalize();
                        _config = state.config;
                    }

                    _entity = state.entity;
                    _receivedSequence = state.sequence;
                    _receivedServerTime = state.sentAt;
                    _receivedLocalTime = Time.realtimeSinceStartup;
                    _selected = state.selected;
                    _active = state.active;
                    _observed = state.observed;
                    _awakened = state.awakened;
                    _containmentRaid = state.containment;
                    if (state.facility != null) Facility = state.facility;
                    SetMoving(state.moving);
                    var position = new Vector3(state.x, state.y, state.z);
                    if (_active && _body == null)
                        CreateBody(position);
                    if (_body != null)
                    {
                        _body.SetActive(_active);
                        _body.transform.position = position;
                        _body.transform.rotation = Quaternion.Euler(0f, state.yaw, 0f);
                    }

                    if (!_active)
                        StopAudio();
                    _position = position;
                    if (_active && !string.IsNullOrEmpty(state.victim))
                        ApplyAttack(state.victim, state.sequence);
                    if (_active && !string.IsNullOrEmpty(state.sound))
                        PlaySound(state.sound);
                }
            }
            catch (JsonException)
            {
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                _active = false;
                StopAudio();
                if (_body != null)
                    _body.SetActive(false);
                _log.LogError("ISB SCP-173 packet rejected: " + ex.Message);
            }

            return true;
        }

        private void SendState(string victim = null, string sound = null, bool full = false)
        {
            var state = new State
            {
                entity = _entity,
                sequence = ++_sequence,
                active = _active,
                observed = _observed,
                selected = _selected,
                awakened = _awakened,
                containment = _containmentRaid,
                moving = _moving,
                facility = Facility,
                sentAt = Time.realtimeSinceStartup,
                x = _position.x,
                y = _position.y,
                z = _position.z,
                yaw = _body != null ? _body.transform.eulerAngles.y : 0f,
                config = full || _sequence == 1 ? _config : null,
                victim = victim,
                sound = sound
            };
            if (_network.Active && !_network.IsSinglePlayer && _network.Ready)
                _network.Broadcast("SC173|S|" + JsonConvert.SerializeObject(state));
            if (!string.IsNullOrEmpty(sound))
                PlaySound(sound);
        }

        private void ApplyAttack(string victim, long sequence)
        {
            if (!_attacks.Add(_entity + ":" + sequence.ToString(CultureInfo.InvariantCulture)))
                return;
            var players = _world.AllAlivePlayersList;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!Alive(p) || p.ProfileId != victim || p.ActiveHealthController == null)
                    continue;
                if (!(p.IsAI && _authority || !p.IsAI && p.IsYourPlayer && !_headless))
                    return;
                if (!p.IsAI && (LocalSees(_position) || (p.Position - _position).sqrMagnitude > _config.killDistance * _config.killDistance || !ClearLine(_position + Vector3.up, p.Position + Vector3.up)))
                    return;
                if (_identity == null)
                    throw new InvalidOperationException("SCP-173 attack identity unavailable.");
                var info = new ScpDamageInfo
                {
                    DamageType = EDamageType.Poison,
                    Damage = 10000f,
                    Player = _identity,
                    BodyPartColliderType = EBodyPartColliderType.HeadCommon,
                    MasterOrigin = _position,
                    HitPoint = p.Position + Vector3.up
                };
                p.ManageAggressor(info, EBodyPart.Head, EBodyPartColliderType.HeadCommon);
                p.ActiveHealthController.ApplyDamage(EBodyPart.Head, 10000f, info);
                return;
            }
        }

        private Profile IdentityTemplate()
        {
            var template = _world.MainPlayer?.Profile;
            if (template?.Info != null && template.Inventory?.Equipment != null)
                return template;
            foreach (var player in _world.AllAlivePlayersList)
            {
                template = player?.Profile;
                if (Alive(player) && template?.Info != null && template.Inventory?.Equipment != null)
                    return template;
            }
            return null;
        }

        private void CreateBody(Vector3 position)
        {
            var template = IdentityTemplate();
            if (template == null)
                throw new InvalidOperationException("SCP-173 identity template unavailable.");
            _position = position;
            _body = new GameObject("ISB_SCP173");
            _body.transform.position = position;
            var physics = new GameObject("ISB_SCP173Physics");
            physics.transform.SetParent(_body.transform, false);
            physics.layer = LayerMask.NameToLayer("LowPolyCollider");
            if (physics.layer < 0)
                physics.layer = 0;
            var collider = physics.AddComponent<CapsuleCollider>();
            collider.height = _config.height;
            collider.radius = _config.bodyRadius;
            collider.center = Vector3.up * _config.height * .5f;
            _identity = new Scp173Identity(this, _body.transform, _entity, template);
            Scp173Registry.Current = _identity;
            if (_headless)
                return;
            var geometry = WavefrontMesh.Read(Path.Combine(_folder, "scp173.obj"), 18000, 18000, 1);
            _mesh = new Mesh
            {
                name = "ISB SCP-173"
            };
            _mesh.vertices = Array.ConvertAll(geometry.vertices, a => new Vector3(a[0], a[1], a[2]) * (_config.height / 2f));
            _mesh.normals = Array.ConvertAll(geometry.normals, a => new Vector3(a[0], a[1], a[2]));
            _mesh.uv = Array.ConvertAll(geometry.uv, a => new Vector2(a[0], a[1]));
            _mesh.triangles = geometry.triangles[0];
            _mesh.RecalculateBounds();
            _mesh.RecalculateTangents();
            _body.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var shader = Shader.Find("Standard");
            if (shader == null || !shader.isSupported)
                shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("SCP-173 material shader unavailable.");
            _material = new Material(shader)
            {
                name = "ISB SCP-173 concrete"
            };
            _texture = LoadTexture("173texture.jpg");
            _normal = LoadTexture("173_Norm.jpg", true);
            _specular = LoadTexture("173_Spec.jpg");
            _material.mainTexture = _texture;
            if (_material.HasProperty("_BumpMap"))
            {
                _material.SetTexture("_BumpMap", _normal);
                _material.EnableKeyword("_NORMALMAP");
            }

            if (_material.HasProperty("_SpecGlossMap"))
                _material.SetTexture("_SpecGlossMap", _specular);
            if (_material.HasProperty("_SpecMap"))
                _material.SetTexture("_SpecMap", _specular);
            if (_material.HasProperty("_Glossiness"))
                _material.SetFloat("_Glossiness", .2f);
            var renderer = _body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            _audio = _body.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 1f;
            _audio.maxDistance = _config.audioMaxDistance;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.volume = _config.audioVolume;
            _audioOther = _body.AddComponent<AudioSource>();
            _audioOther.playOnAwake = false;
            _audioOther.spatialBlend = 1f;
            _audioOther.minDistance = _audio.minDistance;
            _audioOther.maxDistance = _audio.maxDistance;
            _audioOther.rolloffMode = _audio.rolloffMode;
            _audioOther.volume = _config.audioVolume;
            _movementAudio = _body.AddComponent<AudioSource>();
            _movementAudio.playOnAwake=false;
            _movementAudio.spatialBlend=1f;
            _movementAudio.minDistance=_audio.minDistance;
            _movementAudio.maxDistance=_audio.maxDistance;
            _movementAudio.rolloffMode=_audio.rolloffMode;
            _movementAudio.volume=_config.audioVolume;
            StartAudioLoad();
            EnsureAudioReady(Time.realtimeSinceStartup);
        }

        private Texture2D LoadTexture(string name, bool normal = false)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, normal);
            if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(_folder, name)), !normal))
            {
                Object.Destroy(texture);
                throw new InvalidDataException("SCP-173 texture unavailable: " + name);
            }

            if (normal)
            {
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i].a = pixels[i].r;
                    pixels[i].r = 255;
                }

                texture.SetPixels32(pixels);
                texture.Apply(true, true);
            }

            return texture;
        }

        private void EnsureAudioReady(float now)
        {
            if (_headless || !_raidStarted || _audio == null || _audioReady || _audioRequest == null || !_audioRequest.IsCompleted || now < _nextAudioLoad || AudioSettings.dspTime <= 0d)
                return;
            _nextAudioLoad = now + 1f;
            var result = _audioRequest.GetAwaiter().GetResult();
            if (result.error != null)
                throw new InvalidDataException("SCP-173 audio unavailable.", result.error);
            bool ready = true;
            foreach (var entry in result.clips)
                ready &= CreateClip(entry.Key, entry.Value);
            _audioReady = ready;
            if (!ready)
                return;
            _audioRequest = null;
            _audioCancellation.Dispose();
            _audioCancellation = null;
            var pending = _pendingSound;
            _pendingSound = null;
            if (!string.IsNullOrEmpty(pending))
                PlaySound(pending);
        }

        private void StartAudioLoad()
        {
            var names = new HashSet<string>
            {
                _config.moveAudio,
                _config.teleportAudio,
                _config.killAudio,
                _config.activationAudio
            };
            foreach (var name in _config.moveAudioVariants)
                names.Add(name);
            foreach (var name in _config.teleportAudioVariants)
                names.Add(name);
            foreach (var name in _config.killAudioVariants)
                names.Add(name);
            float fadeIn = _config.audioFadeInSeconds, fadeOut = _config.audioFadeOutSeconds;
            _audioCancellation = new CancellationTokenSource();
            var cancellation = _audioCancellation.Token;
            _audioRequest = Task.Run(() =>
            {
                var result = new AudioLoad();
                try
                {
                    foreach (var name in names)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (string.IsNullOrEmpty(name))
                            continue;
                        string path = Path.Combine(_folder, Scp173Config.AudioFile(name));
                        if (!File.Exists(path))
                            continue;
                        if (new FileInfo(path).Length > 8 * 1024 * 1024)
                            throw new InvalidDataException("SCP-173 WAV exceeds 8 MiB.");
                        var data = PcmWavDecoder.Decode(path, cancellation);
                        FadeClip(data, fadeIn, fadeOut);
                        result.clips[name] = data;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.error = ex;
                }

                return result;
            }, cancellation);
        }

        private static void FadeClip(PcmWavData data, float fadeInSeconds, float fadeOutSeconds)
        {
            int frames = data.Samples.Length / data.Channels;
            int fadeIn = Math.Min(frames / 2, (int)(fadeInSeconds * data.Frequency));
            int fadeOut = Math.Min(frames / 2, (int)(fadeOutSeconds * data.Frequency));
            for (int frame = 0; frame < fadeIn; frame++)
            {
                for (int channel = 0; channel < data.Channels; channel++)
                    data.Samples[frame * data.Channels + channel] *= frame / (float)fadeIn;
            }

            for (int frame = 0; frame < fadeOut; frame++)
            {
                for (int channel = 0; channel < data.Channels; channel++)
                    data.Samples[(frames - 1 - frame) * data.Channels + channel] *= frame / (float)fadeOut;
            }
        }

        private bool CreateClip(string name, PcmWavData data)
        {
            if (_clips.ContainsKey(name))
                return true;
            int frames = data.Samples.Length / data.Channels;
            var clip = AudioClip.Create(name, frames, data.Channels, data.Frequency, false);
            if (clip.samples != frames || clip.channels != data.Channels || !clip.SetData(data.Samples, 0))
            {
                Object.Destroy(clip);
                return false;
            }

            _clips[name] = clip;
            return true;
        }

        private void PlaySound(string name)
        {
            if (_headless || _audio == null)
                return;
            if (!_audioReady)
            {
                _pendingSound = name;
                return;
            }

            if (_clips.TryGetValue(Scp173Config.AudioFile(name), out var clip))
            {
                if (IsMoveSound(name))
                {
                    if (!_moving || _movementAudio == null || _movementAudio.isPlaying) return;
                    _movementAudio.clip=clip;
                    _movementAudio.volume=_config.audioVolume;
                    _movementAudio.Play();
                    return;
                }
                if (_audio.isPlaying)
                {
                    _audioOther.Stop();
                    _fadingAudio = _audio;
                    _audioFadeStarted = Time.realtimeSinceStartup;
                    _audioFadeVolume = _audio.volume;
                    var previous = _audio;
                    _audio = _audioOther;
                    _audioOther = previous;
                }

                _audio.volume = _config.audioVolume;
                _audio.clip = clip;
                _audio.Play();
            }
        }

        private string ChooseSound(string primary, string[] variants, ref string previous)
        {
            string chosen = null;
            int count = 0;
            if (!string.IsNullOrEmpty(primary) && primary != previous)
            {
                chosen = primary;
                count++;
            }

            foreach (var name in variants)
                if (!string.IsNullOrEmpty(name) && name != primary && name != previous && _random.Next(++count) == 0)
                    chosen = name;
            if (chosen == null)
                chosen = !string.IsNullOrEmpty(primary) ? primary : previous;
            previous = chosen;
            return chosen;
        }

        private void StopAudio()
        {
            _movementAudio?.Stop();
            _audio?.Stop();
            _audioOther?.Stop();
            _fadingAudio = null;
            _pendingSound = null;
        }

        private void TickAudioFade()
        {
            if (_fadingAudio == null)
                return;
            float duration = _config?.audioFadeOutSeconds ?? 0f;
            float progress = duration <= 0f ? 1f : Math.Min(1f, (Time.realtimeSinceStartup - _audioFadeStarted) / duration);
            _fadingAudio.volume = _audioFadeVolume * (1f - progress);
            if (progress >= 1f)
            {
                _fadingAudio.Stop();
                _fadingAudio = null;
            }
        }

        private void Reset()
        {
            _audioCancellation?.Cancel();
            _audioCancellation?.Dispose();
            _audioCancellation = null;
            _audioRequest = null;
            _pendingSound = null;
            if (ReferenceEquals(Scp173Registry.Current, _identity))
                Scp173Registry.Current = null;
            _identity?.Dispose();
            _identity = null;
            Object.Destroy(_body);
            Object.Destroy(_mesh);
            Object.Destroy(_material);
            Object.Destroy(_texture);
            Object.Destroy(_normal);
            Object.Destroy(_specular);
            foreach (var clip in _clips.Values)
                Object.Destroy(clip);
            _clips.Clear();
            _observers.Clear();
            _attacks.Clear();
            _body = null;
            _audio = _audioOther = _fadingAudio = _movementAudio = null;
            ResetBehaviour();
            _mesh = null;
            _material = null;
            _texture = null;
            _normal = null;
            _specular = null;
            _audioReady = false;
            _nextAudioLoad = 0f;
            _world = null;
            _config = null;
            _entity = _map = null;
            _configRequest = null;
            _selected = _attempted = _active = _observed = _failed = false;
            _awakened = _containmentRaid = _raidStarted = false;
            _teleports = 0;
            _activatedAt = _nextTeleportTry = 0f;
            _lastMoveSound = _lastTeleportSound = _lastKillSound = null;
            _sequence = _receivedSequence = _reportSequence = _ticks = 0;
            _cpuMilliseconds = 0d;
            _receivedServerTime = _receivedLocalTime = 0f;
            _spawnPosition = Vector3.zero;
            _sight.Reset();
            _nextDebugRequest = 0f;
            _nextTick = _lastTick = _nextReport = _nextPath = _nextSnapshot = _lastObserved = _safeUntil = _nextTeleport = _nextAttack = _nextMoveAudio = _nextSpawnTry = 0f;
            _corners = Array.Empty<Vector3>();
        }

        public void Dispose() => Reset();
    }

    public partial class ISBSpecialForcesAudio
    {
        private Scp173Event _scp173;
        private void TickScp173()
        {
            if (_scp173 == null)
                _scp173 = new Scp173Event(Path.Combine(Path.GetDirectoryName(Info.Location), "Nightmare"), Log);
            _scp173.Tick(NetworkBridge, string.IsNullOrWhiteSpace(_currentRaidLocation) ? _fikaReportedRaidLocation : _currentRaidLocation);
            TickNightmareFacility();
        }
    }
}
