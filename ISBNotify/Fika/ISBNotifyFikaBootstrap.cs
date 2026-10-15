using BepInEx.Logging;
using System;

namespace ISBInfo.Fika
{
    public static class ISBNotifyFikaBootstrap
    {
        private static readonly object SyncRoot = new object ();
        private static FikaISBNotifyNetworkBridge _bridge;
        public static void Initialize(ManualLogSource log)
        {
            lock (SyncRoot)
            {
                if (_bridge != null && _bridge.Active)
                    return;
                FikaISBNotifyNetworkBridge bridge = new FikaISBNotifyNetworkBridge(log);
                try
                {
                    bridge.Initialize();
                    if (!ISBInfoPlugin.InstallNetworkBridge(bridge))
                    {
                        throw new InvalidOperationException("The main Notifier plugin rejected the Fika network bridge.");
                    }

                    _bridge = bridge;
                }
                catch
                {
                    ISBInfoPlugin.RemoveNetworkBridge(bridge);
                    bridge.Dispose();
                    throw;
                }
            }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                FikaISBNotifyNetworkBridge bridge = _bridge;
                _bridge = null;
                if (bridge == null)
                    return;
                ISBInfoPlugin.RemoveNetworkBridge(bridge);
                try
                {
                    bridge.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
