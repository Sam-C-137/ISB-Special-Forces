using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    internal static class ISBFikaAuthority
    {
        private const string FikaPluginGuid = "com.fika.core";
        private const string BackendUtilsTypeName = "Fika.Core.Main.Utils.FikaBackendUtils";
        private static Func<bool> _isServer;
        private static Func<bool> _isHeadless;
        private static float _resolveAt;
        private static bool _warningLogged;
        internal static bool IsAiAuthority
        {
            get
            {
                if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid) || Singleton<AbstractGame>.Instance is LocalGame)
                    return true;
                Resolve();
                try
                {
                    if (_isServer != null)
                        return _isServer();
                }
                catch (Exception ex)
                {
                    LogResolutionWarning(ex.Message);
                }

                LogResolutionWarning("FikaBackendUtils.IsServer was not found.");
                return false;
            }
        }

        internal static bool CanPresentLocally
        {
            get
            {
                if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid) || Singleton<AbstractGame>.Instance is LocalGame)
                    return true;
                Resolve();
                try
                {
                    return _isHeadless != null && !_isHeadless();
                }
                catch (Exception ex)
                {
                    LogResolutionWarning(ex.Message);
                    return false;
                }
            }
        }

        private static void Resolve()
        {
            if ((_isServer != null && _isHeadless != null) || Time.realtimeSinceStartup < _resolveAt)
                return;
            _resolveAt = Time.realtimeSinceStartup + 1f;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(assembly.GetName().Name, "Fika.Core", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var type = assembly.GetType(BackendUtilsTypeName, false);
                    if (type == null)
                        return;
                    _isServer = Bind(type, "IsServer");
                    _isHeadless = Bind(type, "IsHeadless");
                    return;
                }
            }
            catch (Exception ex)
            {
                LogResolutionWarning(ex.Message);
            }
        }

        private static Func<bool> Bind(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var property = type.GetProperty(name, flags);
            var getter = property?.GetGetMethod(true);
            if (getter != null && property.PropertyType == typeof(bool))
                return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
            var field = type.GetField(name, flags);
            return field?.FieldType == typeof(bool) ? Expression.Lambda<Func<bool>>(Expression.Field(null, field)).Compile() : null;
        }

        private static void LogResolutionWarning(string reason)
        {
            if (_warningLogged)
                return;
            _warningLogged = true;
            Plugin.LogSource?.LogError("[ISB AISHI LOYALTY] Fika AI authority could not be resolved. Client-side BotsGroup mutations skipped. " + reason);
        }
    }
}
