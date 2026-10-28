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
                    bridge.Initialize();
                    ExtrasNetworkBridgeRegistry.Register(bridge);
                    _bridge = bridge;
                    EnableCompatibility(FikaInventoryViewCompatibility.Enable, "inventory views", log);
                    EnableCompatibility(FikaStartupUiCompatibility.Enable, "startup UI", log);
                    EnableCompatibility(FikaAggressorCompatibility.Enable, "aggressor lookup", log);
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

        private static void EnableCompatibility(Action enable, string feature, ManualLogSource log)
        {
            try
            {
                enable();
            }
            catch (Exception error)
            {
                log?.LogWarning("ISB Fika compatibility for " + feature + " is unavailable: " + error.GetType().Name + ". Event synchronization remains active.");
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
