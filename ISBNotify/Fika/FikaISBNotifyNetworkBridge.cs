using BepInEx.Logging;
using Comfort.Common;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using ISBInfo.Networking;
using System;
using System.Collections.Generic;

namespace ISBInfo.Fika
{
    internal sealed class FikaISBNotifyNetworkBridge : IISBNotifyNetworkBridge
    {
        private const int MaximumQueuedPayloads = 128;
        private const int MaximumPayloadLength = 4096;

        private readonly ManualLogSource _log;
        private readonly object _queueLock = new object();
        private readonly Queue<string> _receivedPayloads = new Queue<string>();

        private IFikaNetworkManager _networkManager;
        private IFikaNetworkManager _registeredNetworkManager;
        private bool _initialized;
        private bool _isServer;

        internal FikaISBNotifyNetworkBridge(ManualLogSource log)
        {
            _log = log;
        }

        public bool Active { get; private set; }

        public bool Ready
        {
            get
            {
                EnsureNetworkManager();
                return Active && _networkManager != null;
            }
        }

        public bool IsServer
        {
            get
            {
                RefreshRole();
                return _isServer;
            }
        }

        public bool IsHeadless
        {
            get
            {
                try
                {
                    return FikaBackendUtils.IsHeadless;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Active = true;

            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(
                OnNetworkManagerCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(
                OnNetworkManagerDestroyed);
            FikaEventDispatcher.SubscribeEvent<FikaRaidStartedEvent>(
                OnRaidStarted);

            EnsureNetworkManager();
            _log.LogInfo(
                "ISB SOF Notifier Fika: direct network bridge initialized without reflection or runtime IL generation.");
        }

        public bool Broadcast(string payload)
        {
            if (!IsValidPayload(payload) || !Ready || !IsServer)
                return false;

            try
            {
                ISBNotifyPacket packet = new ISBNotifyPacket
                {
                    Payload = payload
                };

                _networkManager.SendData(
                    ref packet,
                    DeliveryMethod.ReliableOrdered,
                    true);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    "ISB SOF Notifier Fika: failed to broadcast packet: " + ex.Message);
                return false;
            }
        }

        public bool SendToServer(string payload)
        {
            if (!IsValidPayload(payload) || !Ready || IsServer)
                return false;

            try
            {
                ISBNotifyPacket packet = new ISBNotifyPacket
                {
                    Payload = payload
                };

                _networkManager.SendData(
                    ref packet,
                    DeliveryMethod.ReliableOrdered,
                    false);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    "ISB SOF Notifier Fika: failed to send packet to authority: " + ex.Message);
                return false;
            }
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
                    _log.LogWarning(
                        "ISB SOF Notifier Fika: payload handler failed: " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (_initialized)
            {
                FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(
                    OnNetworkManagerCreated);
                FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerDestroyedEvent>(
                    OnNetworkManagerDestroyed);
                FikaEventDispatcher.UnsubscribeEvent<FikaRaidStartedEvent>(
                    OnRaidStarted);
            }

            _initialized = false;
            Active = false;
            _networkManager = null;
            _registeredNetworkManager = null;

            lock (_queueLock)
                _receivedPayloads.Clear();
        }

        private void OnNetworkManagerCreated(FikaNetworkManagerCreatedEvent fikaEvent)
        {
            if (!Active || fikaEvent == null)
                return;

            AttachNetworkManager(
                fikaEvent.Manager,
                "FikaNetworkManagerCreatedEvent");
        }

        private void OnNetworkManagerDestroyed(FikaNetworkManagerDestroyedEvent fikaEvent)
        {
            if (!Active || fikaEvent == null)
                return;

            if (_networkManager == null ||
                ReferenceEquals(_networkManager, fikaEvent.Manager))
            {
                _networkManager = null;
                _registeredNetworkManager = null;
            }

            lock (_queueLock)
                _receivedPayloads.Clear();
        }

        private void OnRaidStarted(FikaRaidStartedEvent fikaEvent)
        {
            if (!Active || fikaEvent == null)
                return;

            _isServer = fikaEvent.IsServer;
            EnsureNetworkManager();
        }

        private void EnsureNetworkManager()
        {
            if (!Active || _networkManager != null)
                return;

            try
            {
                if (!Singleton<IFikaNetworkManager>.Instantiated)
                    return;

                AttachNetworkManager(
                    Singleton<IFikaNetworkManager>.Instance,
                    "Singleton<IFikaNetworkManager> fallback");
            }
            catch (Exception ex)
            {
                _log.LogDebug(
                    "ISB SOF Notifier Fika: network manager is not ready yet: " + ex.Message);
            }
        }

        private void AttachNetworkManager(
            IFikaNetworkManager networkManager,
            string source)
        {
            if (!Active || networkManager == null)
                return;

            if (ReferenceEquals(_registeredNetworkManager, networkManager))
            {
                _networkManager = networkManager;
                return;
            }

            try
            {
                networkManager.RegisterPacket<ISBNotifyPacket>(OnPacketReceived);
                _networkManager = networkManager;
                _registeredNetworkManager = networkManager;
                RefreshRole();

                _log.LogInfo(
                    "ISB SOF Notifier Fika: packet registered using " + source +
                    ". manager=" + networkManager.GetType().FullName +
                    ", server=" + _isServer +
                    ", headless=" + IsHeadless + ".");
            }
            catch (Exception ex)
            {
                _networkManager = null;
                _registeredNetworkManager = null;
                _log.LogError(
                    "ISB SOF Notifier Fika: packet registration failed: " + ex);
            }
        }

        private void OnPacketReceived(ISBNotifyPacket packet)
        {
            if (!Active || !IsValidPayload(packet.Payload))
                return;

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

        private void RefreshRole()
        {
            try
            {
                _isServer = FikaBackendUtils.IsServer;
            }
            catch
            {
            }

            if (_networkManager == null)
                return;

            string managerName = _networkManager.GetType().Name;
            if (managerName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
                _isServer = true;
            else if (managerName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0)
                _isServer = false;
        }

        private static bool IsValidPayload(string payload)
        {
            return !string.IsNullOrEmpty(payload) &&
                   payload.Length <= MaximumPayloadLength;
        }
    }
}
