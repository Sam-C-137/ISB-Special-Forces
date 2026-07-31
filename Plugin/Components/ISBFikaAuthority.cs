using BepInEx.Bootstrap;
using System;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Components
{
    internal static class ISBFikaAuthority
    {
        private const string FikaPluginGuid = "com.fika.core";
        private const string BackendUtilsTypeName = "Fika.Core.Main.Utils.FikaBackendUtils";

        private static PropertyInfo _isServerProperty;
        private static FieldInfo _isServerField;
        private static bool _resolved;
        private static bool _warningLogged;

        internal static bool IsAiAuthority
        {
            get
            {
                if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid))
                    return true;

                Resolve();

                try
                {
                    if (_isServerProperty != null)
                        return Convert.ToBoolean(_isServerProperty.GetValue(null, null));

                    if (_isServerField != null)
                        return Convert.ToBoolean(_isServerField.GetValue(null));
                }
                catch (Exception ex)
                {
                    LogResolutionWarning(ex.Message);
                }

                LogResolutionWarning("FikaBackendUtils.IsServer was not found.");
                return false;
            }
        }

        private static void Resolve()
        {
            if (_resolved && (_isServerProperty != null || _isServerField != null))
                return;

            _resolved = true;

            try
            {
                var fikaAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly =>
                        string.Equals(
                            assembly.GetName().Name,
                            "Fika.Core",
                            StringComparison.OrdinalIgnoreCase));

                var backendUtilsType = fikaAssembly?.GetType(BackendUtilsTypeName, false);
                if (backendUtilsType == null)
                    return;

                const BindingFlags flags =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                _isServerProperty = backendUtilsType.GetProperty("IsServer", flags);
                _isServerField = backendUtilsType.GetField("IsServer", flags);
            }
            catch (Exception ex)
            {
                LogResolutionWarning(ex.Message);
            }
        }

        private static void LogResolutionWarning(string reason)
        {
            if (_warningLogged)
                return;

            _warningLogged = true;
            Plugin.LogSource?.LogWarning(
                "[ISB AISHI LOYALTY] Fika AI authority could not be resolved. " +
                "Client-side BotsGroup mutations skipped. " + reason);
        }
    }
}
