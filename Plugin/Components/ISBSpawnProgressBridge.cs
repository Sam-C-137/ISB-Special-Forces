using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBSpawnProgressBridge : MonoBehaviour
    {
        private sealed class State
        {
            public string token { get; set; }
            public bool blackoutSelected { get; set; }
            public bool blackoutReady { get; set; }
            public string[] revengeTargets { get; set; }
        }

        private static ISBSpawnProgressBridge _instance;
        private static readonly HashSet<string> Targets = new HashSet<string>(StringComparer.Ordinal);
        private GameWorld _world;
        private string _token;
        private int _generation;
        private int _revision, _sentRevision = -1;
        private bool _started, _seen, _containment, _blackout, _scp173, _busy, _reported;
        private double _blackoutChance = -1;
        private float _next;
        public static bool BlackoutDecisionReady { get; private set; }
        public static bool BlackoutSelected { get; private set; }

        private void Awake()
        {
            _instance = this;
        }

        public static bool IsTarget(IPlayer player) => player != null && !player.IsAI && Targets.Contains(player.ProfileId);
        internal static void Init(GameWorld world, bool started)
        {
            var c = _instance;
            if (c == null)
                return;
            if (!ReferenceEquals(c._world, world))
            {
                c._generation++;
                c._world = world;
                c._token = null;
                c._busy = false;
                c._next = 0;
                c._started = c._seen = c._containment = c._blackout = c._scp173 = c._reported = false;
                c._blackoutChance = -1;
                c._revision = 0;
                c._sentRevision = -1;
                BlackoutDecisionReady = BlackoutSelected = false;
                Targets.Clear();
            }

            if (started && !c._started)
            {
                c._started = true;
                c._revision++;
                c._next = 0;
            }
        }

        internal static void MarkSpawn(BotOwner bot)
        {
            if (_instance == null || !ISBFikaAuthority.IsAiAuthority || bot?.Profile?.Info?.Settings == null || !WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role))
                return;
            if (!_instance._seen)
            {
                _instance._seen = true;
                _instance._revision++;
                _instance._next = 0;
            }
        }

        public static void ReportScp173Spawn()
        {
            var c = _instance;
            if (c == null || !ISBFikaAuthority.IsAiAuthority)
                return;
            Init(Singleton<GameWorld>.Instance, false);
            if (c._scp173)
                return;
            c._scp173 = true;
            c._revision++;
            c._next = 0;
        }

        public static void ReportLabs(bool containment, bool blackout, double baseChance)
        {
            var c = _instance;
            if (c == null || !ISBFikaAuthority.IsAiAuthority)
                return;
            bool changed = containment && !c._containment || blackout && !c._blackout || baseChance >= 0 && baseChance != c._blackoutChance;
            c._containment |= containment;
            c._blackout |= blackout;
            if (baseChance >= 0)
                c._blackoutChance = baseChance;
            if (changed)
            {
                c._revision++;
                c._next = 0;
            }
        }

        private void Update()
        {
            if (_world == null || !ReferenceEquals(_world, Singleton<GameWorld>.Instance))
            {
                if (!ReferenceEquals(_world, null))
                    Init(null, false);
                return;
            }

            if (_busy || Time.unscaledTime < _next || !ISBFikaAuthority.IsAiAuthority)
                return;
            if (!_seen && _world.AllAlivePlayersList != null)
                foreach (var player in _world.AllAlivePlayersList)
                    MarkSpawn(player?.AIData?.BotOwner);
            _busy = true;
            _next = Time.unscaledTime + 15;
            var generation = _generation;
            var revision = _revision;
            var report = !string.IsNullOrEmpty(_token) && revision != _sentRevision ? JsonConvert.SerializeObject(new { token = _token, started = _started, isbSpawned = _seen, containmentOccurred = _containment, blackoutOccurred = _blackout, scp173Spawned = _scp173, blackoutBaseChance = _blackoutChance }) : null;
            _ = Send(generation, revision, _token, report);
        }

        private async Task Send(int generation, int revision, string token, string report)
        {
            try
            {
                var bytes = report == null ? await RequestHandler.HttpClient.GetAsync("/ISB/spawn/progress").ConfigureAwait(false) : await RequestHandler.HttpClient.PostAsync("/ISB/spawn/report", Encoding.UTF8.GetBytes(report)).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(bytes);
                var state = JsonConvert.DeserializeObject<State>(json);
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    _busy = false;
                    if (state == null || string.IsNullOrEmpty(state.token))
                    {
                        _next = Time.unscaledTime + 2;
                        return;
                    }

                    if (_token != state.token)
                    {
                        _sentRevision = -1;
                        _next = 0;
                    }
                    else if (report != null)
                        _sentRevision = revision;
                    if (_revision != _sentRevision)
                        _next = 0;
                    _token = state.token;
                    BlackoutDecisionReady = state.blackoutReady;
                    BlackoutSelected = state.blackoutSelected;
                    Targets.Clear();
                    if (state.revengeTargets != null)
                        foreach (var id in state.revengeTargets)
                            Targets.Add(id);
                });
            }
            catch (Exception ex)
            {
                ISBAishiReputationWorker.Enqueue(() =>
                {
                    if (generation != _generation)
                        return;
                    _busy = false;
                    _next = Time.unscaledTime + 5;
                    if (!_reported)
                    {
                        _reported = true;
                        Plugin.LogSource.LogError("ISB spawn progression request failed: " + ex.Message);
                    }
                });
            }
        }

        private void OnDestroy()
        {
            Init(null, false);
            if (_instance == this)
                _instance = null;
        }
    }
}
