using BepInEx.Logging;
using Comfort.Common;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.Snapshotting;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSSOFExtra.Fika
{
    internal sealed class FikaNetworkBridge : IExtrasNetworkBridge, IDisposable
    {
        private const int MaximumQueuedPayloads = 256;
        private const int MaximumPayloadLength = 65535;

        private readonly ManualLogSource _log;
        private readonly object _queueLock = new object();
        private readonly Queue<string> _receivedPayloads = new Queue<string>();

        private IFikaNetworkManager _networkManager;
        private IFikaNetworkManager _registeredNetworkManager;
        private bool _peerConnectedPending;
        private bool _raidStarted;
        private bool _subscribed;

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
                return Active && _networkManager != null;
            }
        }

        public bool IsServer => Active && FikaBackendUtils.IsServer;

        public bool IsHeadless => Active && FikaBackendUtils.IsHeadless;

        public bool IsSinglePlayer => !Active || FikaBackendUtils.IsSinglePlayer;

        public bool RaidStarted => _raidStarted;

        public double NetworkTime
        {
            get
            {
                try
                {
                    return NetworkTimeSync.NetworkTime;
                }
                catch
                {
                    return Time.realtimeSinceStartup;
                }
            }
        }

        internal void Initialize()
        {
            if (Active)
                return;

            Active = true;
            SubscribeEvents();
            RefreshNetworkManager();
            _log?.LogInfo("ISB SOF Extras Fika integration enabled.");
        }

        public void DrainReceivedPayloads(Action<string> handler)
        {
            if (handler == null)
                return;

            while (TryDequeuePayload(out string payload))
            {
                try
                {
                    handler(payload);
                }
                catch (Exception ex)
                {
                    _log?.LogWarning("Fika payload handler failed: " + ex.Message);
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

        public bool SendToServer(string payload)
        {
            return TrySend(payload, false);
        }

        public void Dispose()
        {
            Active = false;
            UnsubscribeEvents();
            ResetNetworkState();
        }

        private bool TrySend(string payload, bool broadcast)
        {
            if (string.IsNullOrEmpty(payload) ||
                payload.Length > MaximumPayloadLength ||
                !Ready ||
                IsServer != broadcast)
            {
                return false;
            }

            try
            {
                ISBExtrasPacket packet = new ISBExtrasPacket { Payload = payload };
                _networkManager.SendData(
                    ref packet,
                    DeliveryMethod.ReliableOrdered,
                    broadcast);
                return true;
            }
            catch (Exception ex)
            {
                string action = broadcast
                    ? "broadcast Fika packet"
                    : "send Fika packet to server";
                _log?.LogWarning("Failed to " + action + ": " + ex.Message);
                return false;
            }
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
            if (_registeredNetworkManager == null ||
                ReferenceEquals(_registeredNetworkManager, fikaEvent.Manager))
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
                _peerConnectedPending = true;
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

            manager.RegisterPacket<ISBExtrasPacket>(OnPacketReceived);
            _registeredNetworkManager = manager;

            _log?.LogInfo(
                "ISB SOF Extras Fika packet registered using " + source +
                ". server=" + IsServer +
                ", headless=" + IsHeadless +
                ", singlePlayer=" + IsSinglePlayer + ".");
        }

        private void OnPacketReceived(ISBExtrasPacket packet)
        {
            if (!Active ||
                string.IsNullOrEmpty(packet.Payload) ||
                packet.Payload.Length > MaximumPayloadLength)
            {
                return;
            }

            lock (_queueLock)
            {
                while (_receivedPayloads.Count >= MaximumQueuedPayloads)
                    _receivedPayloads.Dequeue();

                _receivedPayloads.Enqueue(packet.Payload);
            }
        }

        private bool TryDequeuePayload(out string payload)
        {
            lock (_queueLock)
            {
                if (_receivedPayloads.Count == 0)
                {
                    payload = null;
                    return false;
                }

                payload = _receivedPayloads.Dequeue();
                return true;
            }
        }

        private void ResetNetworkState()
        {
            _networkManager = null;
            _registeredNetworkManager = null;
            _raidStarted = false;

            lock (_queueLock)
            {
                _receivedPayloads.Clear();
                _peerConnectedPending = false;
            }
        }
    }
}
