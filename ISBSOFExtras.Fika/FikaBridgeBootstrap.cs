using BepInEx.Logging;
using System;

namespace ISBSSOFExtra.Fika
{
    public static class FikaBridgeBootstrap
    {
        private static readonly object SyncRoot = new object ();
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
                    FikaInventoryViewCompatibility.Enable();
                    FikaStartupUiCompatibility.Enable();
                    FikaAggressorCompatibility.Enable();
                    bridge.Initialize();
                    ExtrasNetworkBridgeRegistry.Register(bridge);
                    _bridge = bridge;
                }
                catch
                {
                    ExtrasNetworkBridgeRegistry.Unregister(bridge);
                    bridge.Dispose();
                    FikaInventoryViewCompatibility.Disable();
                    FikaStartupUiCompatibility.Disable();
                    FikaAggressorCompatibility.Disable();
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
                FikaInventoryViewCompatibility.Disable();
                FikaStartupUiCompatibility.Disable();
                FikaAggressorCompatibility.Disable();
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
