using BepInEx.Logging;
using System;

namespace ISBSSOFExtra.Fika
{
    public static class FikaBridgeBootstrap
    {
        private static readonly object SyncRoot = new object();
        private static FikaNetworkBridge _bridge;

        public static void Initialize(ManualLogSource log)
        {
            lock (SyncRoot)
            {
                if (_bridge != null && _bridge.Active)
                    return;

                FikaNetworkBridge bridge = new FikaNetworkBridge(log);

                try
                {
                    bridge.Initialize();
                    ExtrasNetworkBridgeRegistry.Register(bridge);
                    _bridge = bridge;

                    log?.LogInfo(
                        "ISB SOF Extras: optional Fika bridge attached. " +
                        "server=" + bridge.IsServer +
                        ", headless=" + bridge.IsHeadless + ".");
                }
                catch
                {
                    ExtrasNetworkBridgeRegistry.Unregister(bridge);
                    bridge.Dispose();
                    throw;
                }
            }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                FikaNetworkBridge bridge = _bridge;
                _bridge = null;

                if (bridge == null)
                    return;

                ExtrasNetworkBridgeRegistry.Unregister(bridge);

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
