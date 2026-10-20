using BepInEx.Logging;
using Comfort.Common;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace ISBSSOFExtra.Fika
{
    internal sealed class FikaNetworkBridge : IExtrasNetworkBridge, IExtrasPeerNetworkBridge, IDisposable
    {
        private const int MaximumPayloadLength = 65535;
        private readonly ManualLogSource _log;
        private readonly FikaBotHandsCompatibility _botHands = new FikaBotHandsCompatibility();
        private readonly FikaAuthorityClock _clock = new FikaAuthorityClock();
        private readonly object _queueLock = new object ();
        private sealed class ReceivedPacket
        {
            internal string Payload, Player;
        }
        private sealed class PeerRate
        {
            internal long Window;
            internal int Count;
        }
        private readonly FikaEventQueue<ReceivedPacket> _receivedPayloads = new FikaEventQueue<ReceivedPacket>();
        private readonly Dictionary<string, NetPeer> _playerPeers = new Dictionary<string, NetPeer>();
        private readonly Dictionary<int, string> _players = new Dictionary<int, string>();
        private readonly Dictionary<int, PeerRate> _rates = new Dictionary<int, PeerRate>();
        private static FikaNetworkBridge _activeBridge;
        private Harmony _identityPatch;
        public string ReceivedPlayerId { get; private set; }

        private static void CaptureIdentity(object[] __args)
        {
            var bridge = _activeBridge;
            if (bridge == null || __args.Length != 2 || !(__args[1] is NetPeer peer)) return;
            var packet = __args[0];
            var type = packet.GetType();
            string profile = (type.GetField("ProfileId")?.GetValue(packet) ?? type.GetProperty("ProfileId")?.GetValue(packet))?.ToString();
            if (string.IsNullOrEmpty(profile) || profile.Length > 64) return;
            lock (bridge._queueLock)
            {
                if (!bridge._players.ContainsKey(peer.Id))
                    bridge._players.Add(peer.Id, profile);
                bridge._playerPeers[bridge._players[peer.Id]] = peer;
            }
        }
        private IFikaNetworkManager _networkManager;
        private IFikaNetworkManager _registeredNetworkManager;
        private bool _peerConnectedPending;
        private bool _raidStarted;
        private bool _subscribed;
        private long _nextSendFailureLog;
        internal FikaNetworkBridge(ManualLogSource log)
        {
            _log = log;
        }

        public bool Active { get; private set; }

        public bool Ready
        {
            get
            {
                RefreshNetworkManager();
                return Active && _networkManager != null && (IsServer || _clock.Ready);
            }
        }

        public bool IsServer => Active && FikaBackendUtils.IsServer;
        public bool IsHeadless => Active && FikaBackendUtils.IsHeadless;
        public bool IsSinglePlayer => !Active || Singleton<EFT.AbstractGame>.Instance is EFT.LocalGame || FikaBackendUtils.IsSinglePlayer;
        public bool RaidStarted => _raidStarted;

        public double NetworkTime => IsServer ? LocalTime : _clock.Time(LocalTime);

        private static double LocalTime => FikaAuthorityClock.LocalTime;

        internal void Initialize()
        {
            if (Active)
                return;
            var identity = AccessTools.Method(typeof(FikaServer), "OnNetworkSettingsPacketReceived");
            if (identity == null) throw new MissingMethodException("Fika player identity callback is unavailable.");
            _activeBridge = this;
            _identityPatch = new Harmony("com.samc137.isb.fika-event-identity");
            _identityPatch.Patch(identity, postfix: new HarmonyMethod(typeof(FikaNetworkBridge), nameof(CaptureIdentity)));
            _botHands.Initialize();
            Active = true;
            SubscribeEvents();
            RefreshNetworkManager();
        }

        public void DrainReceivedPayloads(Action<string> handler)
        {
            if (handler == null)
                return;
            RefreshNetworkManager();
            _botHands.Tick(_networkManager, IsServer);
            int processed = 0;
            long started = Stopwatch.GetTimestamp();
            while (processed < 64 && (processed < 4 || Stopwatch.GetTimestamp() - started < Stopwatch.Frequency / 500) && TryDequeuePayload(out ReceivedPacket packet))
            {
                processed++;
                try
                {
                    ReceivedPlayerId = packet.Player;
                    handler(packet.Payload);
                }
                catch (Exception ex)
                {
                    _log?.LogError("Fika event payload handler failed: " + ex.GetType().Name);
                }
                finally
                {
                    ReceivedPlayerId = null;
                }
            }
        }

        public bool ConsumePeerConnected()
        {
            lock (_queueLock)
            {
                bool pending = _peerConnectedPending;
                _peerConnectedPending = false;
                return pending;
            }
        }

        public bool Broadcast(string payload)
        {
            return TrySend(payload, true);
        }

        public bool SendToPlayer(string profileId, string payload)
        {
            if (!IsServer || !Ready || string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(payload) || payload.Length > MaximumPayloadLength)
                return false;
            NetPeer peer;
            lock (_queueLock)
                if (!_playerPeers.TryGetValue(profileId, out peer))
                    return false;
            try
            {
                var packet = new ISBExtrasPacket { Payload = FikaAuthorityClock.Stamp(payload, LocalTime) };
                if (packet.Payload.Length > MaximumPayloadLength || !(_networkManager is FikaServer host))
                    return false;
                host.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
                return true;
            }
            catch (Exception ex)
            {
                ReportSendFailure("send Fika state to player", ex);
                return false;
            }
        }

        public bool SendToServer(string payload)
        {
            return TrySend(payload, false);
        }

        public void Dispose()
        {
            Active = false;
            _botHands.Dispose();
            _identityPatch?.UnpatchSelf();
            _identityPatch = null;
            if (ReferenceEquals(_activeBridge, this)) _activeBridge = null;
            UnsubscribeEvents();
            ResetNetworkState();
        }

        private bool TrySend(string payload, bool broadcast)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > MaximumPayloadLength || !Ready || IsServer != broadcast)
            {
                return false;
            }

            try
            {
                ISBExtrasPacket packet = new ISBExtrasPacket
                {
                    Payload = broadcast ? FikaAuthorityClock.Stamp(payload, LocalTime) : payload
                };
                if (packet.Payload.Length > MaximumPayloadLength)
                    return false;
                _networkManager.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast);
                return true;
            }
            catch (Exception ex)
            {
                ReportSendFailure(broadcast ? "broadcast Fika packet" : "send Fika packet to server", ex);
                return false;
            }
        }

        private void ReportSendFailure(string action, Exception error)
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _nextSendFailureLog)
                return;
            _nextSendFailureLog = now + 30L * Stopwatch.Frequency;
            _log?.LogError("Failed to " + action + " (" + error.GetType().Name + "). Check the Fika connection and matching ISB client files.");
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
                return;
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnNetworkManagerDestroyed);
            FikaEventDispatcher.SubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
            FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
            _subscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed)
                return;
            FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
            FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnNetworkManagerDestroyed);
            FikaEventDispatcher.UnsubscribeEvent<FikaRaidStartedEvent>(OnRaidStarted);
            FikaEventDispatcher.UnsubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
            _subscribed = false;
        }

        private void OnNetworkManagerCreated(FikaNetworkManagerCreatedEvent fikaEvent)
        {
            AttachNetworkManager(fikaEvent.Manager, "FikaNetworkManagerCreatedEvent");
        }

        private void OnNetworkManagerDestroyed(FikaNetworkManagerDestroyedEvent fikaEvent)
        {
            if (_registeredNetworkManager == null || ReferenceEquals(_registeredNetworkManager, fikaEvent.Manager))
            {
                ResetNetworkState();
            }
        }

        private void OnRaidStarted(FikaRaidStartedEvent fikaEvent)
        {
            _raidStarted = true;
        }

        private void OnPeerConnected(PeerConnectedEvent fikaEvent)
        {
            if (!IsServer || fikaEvent.Peer == null)
                return;
            lock (_queueLock)
            {
                if (_players.TryGetValue(fikaEvent.Peer.Id, out var previousPlayer))
                    _playerPeers.Remove(previousPlayer);
                _players.Remove(fikaEvent.Peer.Id);
                _rates.Remove(fikaEvent.Peer.Id);
                _peerConnectedPending = true;
            }
        }

        private void RefreshNetworkManager()
        {
            if (!Active)
                return;
            if (!Singleton<IFikaNetworkManager>.Instantiated)
            {
                if (_networkManager != null)
                    ResetNetworkState();
                return;
            }

            IFikaNetworkManager manager = Singleton<IFikaNetworkManager>.Instance;
            if (manager != null && !ReferenceEquals(_registeredNetworkManager, manager))
                AttachNetworkManager(manager, "Singleton<IFikaNetworkManager>");
        }

        private void AttachNetworkManager(IFikaNetworkManager manager, string source)
        {
            if (!Active || manager == null)
                return;
            _networkManager = manager;
            if (ReferenceEquals(_registeredNetworkManager, manager))
                return;
            _clock.Reset();
            manager.RegisterPacket<ISBExtrasPacket, NetPeer>(OnPacketReceived);
            manager.RegisterPacket<ISBBotHandsPacket, NetPeer>(OnBotHandsReceived);
            _registeredNetworkManager = manager;
        }

        private void OnBotHandsReceived(ISBBotHandsPacket packet, NetPeer peer)
        {
            if (!Active) return;
            if (IsServer)
            {
                lock (_queueLock)
                {
                    if (peer == null || !_players.ContainsKey(peer.Id)) return;
                    long now = Stopwatch.GetTimestamp();
                    if (!_rates.TryGetValue(peer.Id, out var rate)) _rates[peer.Id] = rate = new PeerRate { Window = now };
                    if (now - rate.Window >= Stopwatch.Frequency) { rate.Window = now; rate.Count = 0; }
                    if (++rate.Count > 80) return;
                }
            }
            _botHands.Receive(packet, peer, _networkManager, IsServer);
        }

        private void OnPacketReceived(ISBExtrasPacket packet, NetPeer peer)
        {
            if (!Active || string.IsNullOrEmpty(packet.Payload) || packet.Payload.Length > MaximumPayloadLength)
            {
                return;
            }

            lock (_queueLock)
            {
                if (!IsServer)
                {
                    if (!_clock.Receive(packet.Payload, LocalTime, out string payload))
                        return;
                    packet.Payload = payload;
                }
                string profile = null;
                if (IsServer)
                {
                    if (peer == null || !_players.TryGetValue(peer.Id, out profile)) return;
                    long now = Stopwatch.GetTimestamp();
                    if (!_rates.TryGetValue(peer.Id, out var rate)) _rates[peer.Id] = rate = new PeerRate { Window = now };
                    if (now - rate.Window >= Stopwatch.Frequency) { rate.Window = now; rate.Count = 0; }
                    if (++rate.Count > 80) return;
                    string command = packet.Payload.Split('|')[0];
                    if (command != "Q" && command != "T" && command != "W" && command != "OQ" && command != "OC" && command != "OT" && command != "OP" && command != "SC173" && command != "AR") return;
                    string[] fields = packet.Payload.Split('|');
                    int playerIndex = command == "OC" || command == "OP" ? 2 : command == "OT" || command == "OQ" ? 1 : -1;
                    if (playerIndex >= 0 && (fields.Length <= playerIndex || fields[playerIndex] != profile)) return;
                }
                _receivedPayloads.Enqueue(new ReceivedPacket { Payload = packet.Payload, Player = profile }, FikaEventQueue<ReceivedPacket>.StateKey(packet.Payload, profile));
            }
        }

        private bool TryDequeuePayload(out ReceivedPacket payload)
        {
            lock (_queueLock)
                return _receivedPayloads.TryDequeue(out payload);
        }

        private void ResetNetworkState()
        {
            _botHands.Reset();
            _clock.Reset();
            _networkManager = null;
            _registeredNetworkManager = null;
            _raidStarted = false;
            _nextSendFailureLog = 0;
            lock (_queueLock)
            {
                _receivedPayloads.Clear();
                _players.Clear();
                _playerPeers.Clear();
                _rates.Clear();
                ReceivedPlayerId = null;
                _peerConnectedPending = false;
            }
        }
    }
}
