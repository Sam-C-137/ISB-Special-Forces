using BepInEx.Bootstrap;
using BepInEx.Logging;
using System;
using System.Reflection;

namespace ISB.Compatibility
{
    internal static class ISBEffectNotifications
    {
        private static Action<string> _report;
        private static bool _resolved, _failed;
        private static Action<string, float> _begin;
        private static bool _beginResolved, _beginFailed;
        internal static void BeginAnnouncement(string audioName, float delay, ManualLogSource log)
        {
            if (_beginFailed)
                return;
            try
            {
                if (!_beginResolved)
                {
                    _beginResolved = true;
                    if (!Chainloader.PluginInfos.TryGetValue("com.samc137.ISBinfo", out var plugin) || plugin?.Instance == null)
                        return;
                    var method = plugin.Instance.GetType().GetMethod("BeginAnnouncement", BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                        throw new MissingMethodException("Update ISBNotify.dll to configure announcement notification timing.");
                    _begin = (Action<string, float>)Delegate.CreateDelegate(typeof(Action<string, float>), method);
                }

                _begin?.Invoke(audioName, delay);
            }
            catch (Exception ex)
            {
                _beginFailed = true;
                log?.LogError("[ISB] Notification timing unavailable: " + ex.GetBaseException().Message);
            }
        }

        internal static void Publish(string effectId, ManualLogSource log)
        {
            if (_failed)
                return;
            try
            {
                if (!_resolved)
                {
                    _resolved = true;
                    if (!Chainloader.PluginInfos.TryGetValue("com.samc137.ISBinfo", out var plugin) || plugin?.Instance == null)
                        return;
                    var method = plugin.Instance.GetType().GetMethod("ReportAnnouncementEffect", BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                        throw new MissingMethodException("Update ISBNotify.dll to enable announcement effect notifications.");
                    _report = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), method);
                }

                _report?.Invoke(effectId);
            }
            catch (Exception ex)
            {
                _failed = true;
                log?.LogError("[ISB] Effect notification unavailable: " + ex.GetBaseException().Message);
            }
        }
    }
}
