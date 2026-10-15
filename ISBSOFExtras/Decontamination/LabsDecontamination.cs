using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;
using GasDamageInfo = DamageInfoStruct;
using GasNotifications = NotificationManagerClass;

namespace ISBSSOFExtra
{
    internal sealed class GasNetworkState
    {
        public bool aborted { get; set; }
        public int revision { get; set; }
        public double startedAt { get; set; } = -1d;
        public double fireExtinctionStartedAt { get; set; } = -1d;
        public DecontaminationConfig config { get; set; }
    }

    internal sealed class LabsDecontamination : IDisposable
    {
        private sealed class Exposure
        {
            internal float Seconds;
            internal float MaskedSeconds;
            internal float LastSeen;
            internal bool Failed;
        }

        private static readonly EBodyPart[] BodyParts =
        {
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.Stomach,
            EBodyPart.Chest,
            EBodyPart.Head
        };
        private readonly Dictionary<Player, Exposure> _exposures = new Dictionary<Player, Exposure>();
        private readonly List<Player> _targets = new List<Player>(64);
        private readonly List<Player> _expired = new List<Player>();
        private readonly DecontaminationVisuals _visuals = new DecontaminationVisuals();
        private readonly ManualLogSource _log;
        private readonly GameWorld _world;
        private readonly string _path;
        private readonly string _folder;
        private DecontaminationConfig _local = new DecontaminationConfig();
        private GasNetworkState _state = new GasNetworkState();
        private HashSet<string> _masks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _fileStamp = DateTime.MinValue;
        private float _nextReload;
        private float _nextTick;
        private double _lastClock = -1d;
        private bool _configRead;
        private bool _triggered;
        internal bool Announced { get; private set; }

        private bool _notified;
        private bool _visualsPrepared;
        private bool _visualsFailed;
        private bool _disposed;
        private int _acceptedRevision = -1;
        private string _serializedState;
        internal bool VideoTimelineEnabled => !_state.aborted && _local.videoTimeline;
        internal bool ScreenVideoEnabled => !_state.aborted && _local.screenVideo;
        internal int Revision => _state.revision;

        internal bool HasStarted(double now) => !_disposed && !_state.aborted && _state.config?.enabled == true && _state.startedAt >= 0d && now >= _state.startedAt;
        internal double StartedAt => _state.startedAt;
        internal double FireExtinctionStartedAt => _state.fireExtinctionStartedAt;
        internal bool Configured => !_state.aborted && _local.enabled && _local.areas.Length > 0;

        internal bool Due(float now) => !_disposed && now >= _nextTick;
        internal LabsDecontamination(GameWorld world, string path, ManualLogSource log)
        {
            _world = world;
            _path = path;
            _folder = Path.GetDirectoryName(path);
            _log = log;
        }

        internal void Trigger(string clip, double startedAt, bool masterEnabled)
        {
            if (_disposed || _state.aborted || _triggered || !masterEnabled || !_local.enabled || _local.areas.Length == 0 || !GasRules.Finite(startedAt) || startedAt < 0d)
                return;
            if (GasRules.MatchesClip(clip, GasRules.WarningClip))
            {
                Announced = true;
                return;
            }

            if (!Announced || !GasRules.MatchesClip(clip, GasRules.TriggerClip))
                return;
            _triggered = true;
            _state.startedAt = startedAt + GasRules.ActivationDelay(clip);
            _lastClock = _state.startedAt;
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                _log?.LogInfo("ISB SOF gas: Activation scheduled by " + clip + "; start=" + _state.startedAt + "; protective mask IDs=" + _masks.Count + ".");
            }

            Changed();
        }

        internal void Tick(double now, float realNow, bool authority, bool present, bool masterEnabled, bool visualEnabled)
        {
            _nextTick = realNow + GasRules.TickInterval;
            if (_disposed)
                return;
            if (!ReferenceEquals(_world, Singleton<GameWorld>.Instance))
            {
                Dispose();
                return;
            }

            if (realNow >= _nextReload)
            {
                _nextReload = realNow + 2f;
                Reload(authority);
            }

            if (authority && !ReferenceEquals(_state.config, _local))
            {
                _state.config = _local;
                _masks = new HashSet<string>(_local.protectiveMasks, StringComparer.OrdinalIgnoreCase);
                Changed();
            }

            if (authority && !_state.aborted && (!masterEnabled || !_local.enabled || _local.areas.Length == 0) && _state.startedAt >= 0d)
                Stop();
            var rules = _state.config;
            bool active = !_state.aborted && rules != null && rules.enabled && _state.startedAt >= 0d && now >= _state.startedAt;
            if (!active)
            {
                ClearVisuals();
                if (_notified && present)
                    Notify("Stopped", "Decontamination System aborted. The toxin has been contained and the facility is safe to remain in.");
                _notified = false;
                _lastClock = now;
                return;
            }

            if (authority && _state.fireExtinctionStartedAt < 0d)
            {
                _state.fireExtinctionStartedAt = _state.startedAt;
                Changed();
            }

            float elapsed = _lastClock < 0d ? 0f : GasRules.Step(Math.Max(_lastClock, _state.startedAt), now);
            _lastClock = now;
            float density = Mathf.Clamp01((float)(now - _state.startedAt) / rules.fillSeconds);
            var localPlayer = present ? _world.MainPlayer : null;
            bool localAlive = localPlayer != null && localPlayer.HealthController?.IsAlive == true;
            Vector3 position = localAlive ? localPlayer.Transform.position : Vector3.zero;
            bool inArea = localAlive && rules.Contains(position.x, position.y, position.z);
            if (inArea && !_notified)
            {
                _notified = true;
                Notify("Started", "Decontamination System initiated. The toxin has been released and will begin to fill the facility. Equip a Gas Mask and evacuate the facility immediately.");
            }

            if (present && visualEnabled && _local.visualsEnabled && inArea && !_visualsFailed)
            {
                try
                {
                    if (!_visualsPrepared)
                    {
                        _visuals.Prepare(_folder, _local);
                        _visualsPrepared = true;
                    }

                    _visuals.Tick(position, true, density);
                }
                catch (Exception ex)
                {
                    _visualsFailed = true;
                    ClearVisuals();
                    _log?.LogError("ISB SOF: Gas visuals disabled after failure: " + ex.Message);
                }
            }
            else if (_visualsPrepared)
                _visuals.Suspend();
            ApplyExposure(realNow, Time.timeScale > 0f ? elapsed : 0f, density, authority, present, rules);
        }

        private void ApplyExposure(float realNow, float elapsed, float density, bool authority, bool present, DecontaminationConfig rules)
        {
            _targets.Clear();
            var players = _world?.AllAlivePlayersList;
            if (players == null)
                return;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.HealthController?.IsAlive != true)
                    continue;
                bool local = present && (player.IsYourPlayer || ReferenceEquals(player, _world.MainPlayer));
                if (GasRules.OwnsDamage(local, player.IsAI, authority) && (player.IsAI ? rules.damageBots : rules.damagePlayers))
                    _targets.Add(player);
            }

            foreach (var player in _targets)
            {
                if (player == null || player.HealthController?.IsAlive != true)
                    continue;
                if (!_exposures.TryGetValue(player, out var exposure))
                {
                    exposure = new Exposure();
                    _exposures.Add(player, exposure);
                }

                exposure.LastSeen = realNow;
                if (exposure.Failed)
                    continue;
                try
                {
                    var position = player.Transform.position;
                    if (!rules.Contains(position.x, position.y, position.z))
                        continue;
                    bool protectedByMask = HasProtectiveMask(player);
                    if (player.IsAI && protectedByMask)
                        continue;
                    float rate = protectedByMask ? GasRules.MaskedDamageRate(exposure.MaskedSeconds, rules.damagePerSecond) : rules.damagePerSecond;
                    float damageTime = GasRules.DamageSeconds(exposure.Seconds, elapsed, rules.graceSeconds);
                    exposure.Seconds += elapsed;
                    if (protectedByMask)
                        exposure.MaskedSeconds += elapsed;
                    float damage = rate * damageTime * density / BodyParts.Length;
                    if (damage <= 0f || player.ActiveHealthController == null)
                        continue;
                    var info = new GasDamageInfo
                    {
                        DamageType = EDamageType.Poison,
                        Damage = damage
                    };
                    foreach (var part in BodyParts)
                    {
                        if (!player.HealthController.IsAlive)
                            break;
                        player.ActiveHealthController.ApplyDamage(part, damage, info);
                    }
                }
                catch (Exception ex)
                {
                    exposure.Failed = true;
                    _log?.LogError("ISB SOF: Gas damage unavailable for a raid actor: " + ex.Message);
                }
            }

            _expired.Clear();
            foreach (var item in _exposures)
                if (item.Key == null || realNow - item.Value.LastSeen > 3f)
                    _expired.Add(item.Key);
            foreach (var player in _expired)
                _exposures.Remove(player);
        }

        private bool HasProtectiveMask(Player player) => Protects(player.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem, 0) || Protects(player.Equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem, 0) || Protects(player.Equipment?.GetSlot(EquipmentSlot.Eyewear)?.ContainedItem, 0);
        private bool Protects(Item item, int depth)
        {
            if (item == null)
                return false;
            if (_masks.Contains(item.TemplateId.ToString()))
                return true;
            if (depth >= 4 || !(item is CompoundItem compound) || compound.Slots == null)
                return false;
            foreach (var slot in compound.Slots)
                if (Protects(slot?.ContainedItem, depth + 1))
                    return true;
            return false;
        }

        private void Reload(bool authority)
        {
            bool rebuildVisuals = false;
            try
            {
                DateTime stamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
                bool fileChanged = !_configRead || stamp != _fileStamp;
                if (!fileChanged)
                    return;
                _configRead = true;
                _fileStamp = stamp;
                if (fileChanged)
                    rebuildVisuals = true;
                _local = new DecontaminationConfig();
                if (stamp != DateTime.MinValue)
                {
                    if (new FileInfo(_path).Length > 32768)
                        throw new InvalidDataException("Gas configuration exceeds 32 KB.");
                    _local = (JsonConvert.DeserializeObject<DecontaminationConfig>(File.ReadAllText(_path)) ?? new DecontaminationConfig()).Normalize();
                }
            }
            catch (Exception ex)
            {
                rebuildVisuals = true;
                _local = new DecontaminationConfig();
                _log?.LogError("ISB SOF: Decontamination configuration disabled: " + ex.Message);
            }

            if (rebuildVisuals)
            {
                ClearVisuals();
                _visualsFailed = false;
            }

            if (authority)
            {
                _state.config = _local;
                _masks = new HashSet<string>(_local.protectiveMasks, StringComparer.OrdinalIgnoreCase);
                Changed();
            }
        }

        internal string SerializeState()
        {
            if (_serializedState == null)
                _serializedState = JsonConvert.SerializeObject(_state);
            return _serializedState;
        }

        internal void AcceptState(string json)
        {
            if (_disposed || string.IsNullOrEmpty(json) || json.Length > 32768)
                return;
            GasNetworkState received;
            try
            {
                received = JsonConvert.DeserializeObject<GasNetworkState>(json);
            }
            catch (JsonException)
            {
                return;
            }

            if (received == null || received.revision <= _acceptedRevision || received.revision < 0 || !GasRules.Finite(received.startedAt) || received.startedAt < -1d || received.config == null || !GasRules.Finite(received.fireExtinctionStartedAt) || received.fireExtinctionStartedAt < -1d)
                return;
            received.config.Normalize();
            _acceptedRevision = received.revision;
            bool newStart = received.startedAt != _state.startedAt;
            _state = received;
            _serializedState = null;
            _masks = new HashSet<string>(_state.config.protectiveMasks, StringComparer.OrdinalIgnoreCase);
            if (newStart)
            {
                _exposures.Clear();
                _lastClock = -1d;
            }
        }

        internal void SetPaused(bool paused)
        {
            if (_disposed || _state.aborted == paused)
                return;
            _state.aborted = paused;
            if (paused)
                ClearVisuals();
            Changed();
        }

        private void Stop()
        {
            _state.startedAt = -1d;
            _exposures.Clear();
            ClearVisuals();
            Changed();
        }

        private void Changed()
        {
            _state.revision++;
            _serializedState = null;
        }

        private void ClearVisuals()
        {
            if (!_visualsPrepared)
                return;
            _visuals.Dispose();
            _visualsPrepared = false;
        }

        private void Notify(string suffix, string fallback)
        {
            try
            {
                GasNotifications.DisplayMessageNotification(ISB.Compatibility.ISBLocalizedText.Get("ISB/Decontamination/" + suffix, fallback), ENotificationDurationType.Long);
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _visuals.Dispose();
            _visualsPrepared = false;
            _exposures.Clear();
            _targets.Clear();
            _expired.Clear();
        }
    }
}
