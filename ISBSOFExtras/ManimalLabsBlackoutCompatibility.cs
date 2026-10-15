using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal static class ManimalLabsBlackoutCompatibility
    {
        internal const string PluginGuid = "com.manimal.labsboiler";
        internal const string SceneName = "Laboratory_Office_Above_Boiler_Room_floor_1_MX";
        private const string HarmonyId = "com.samc137.isbsofextras.manimal-blackout";
        private sealed class LampState
        {
            internal Light Light;
            internal float Intensity;
            internal bool Enabled;
        }

        private sealed class AreaState
        {
            internal BaseLight Light;
            internal float Intensity;
            internal bool Enabled;
        }

        private static readonly Dictionary<int, LampState> Lamps = new Dictionary<int, LampState>();
        private static readonly Dictionary<int, AreaState> Areas = new Dictionary<int, AreaState>();
        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static MethodInfo _reapply;
        private static bool _available, _blackout;
        internal static void Enable(ManualLogSource log)
        {
            if (_available)
                return;
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin) || plugin.Instance == null)
                return;
            _log = log;
            try
            {
                var type = plugin.Instance.GetType().Assembly.GetType("Manimal.LabsBoiler.LabsBoilerLights");
                _reapply = type?.GetMethod("ReapplyIntensity", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (_reapply == null || _reapply.ReturnType != typeof(void))
                    throw new MissingMethodException("Manimal LabsBoilerLights.ReapplyIntensity() was not found.");
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(_reapply, postfix: new HarmonyMethod(typeof(ManimalLabsBlackoutCompatibility), nameof(AfterReapplyIntensity)));
                _harmony.Patch(AccessTools.Method(typeof(CullingObject), "Start"), prefix: new HarmonyMethod(typeof(ManimalLabsBlackoutCompatibility), nameof(BeforeCullingObjectStart)));
                _available = true;
            }
            catch (Exception)
            {
                UnpatchSafely();
                _harmony = null;
                _reapply = null;
            }
        }

        private static void BeforeCullingObjectStart(CullingObject __instance)
        {
            if (__instance == null || __instance.gameObject.scene.name != SceneName || __instance.GetTransform() != null)
                return;
            __instance.OnPreProcess();
        }

        internal static void SetBlackout(bool active)
        {
            if (!_available)
                return;
            _blackout = active;
            try
            {
                if (active)
                    CaptureAndMute();
                else
                    Restore();
            }
            catch (Exception)
            {
            }
        }

        private static void AfterReapplyIntensity()
        {
            if (!_available || !_blackout)
                return;
            try
            {
                CaptureAndMute();
            }
            catch (Exception)
            {
            }
        }

        private static void CaptureAndMute()
        {
            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                {
                    if (!BlackoutTargets.IsFacilityComponent(light))
                        continue;
                    int key = light.gameObject.GetInstanceID();
                    if (!Lamps.TryGetValue(key, out var state))
                        Lamps.Add(key, state = new LampState { Intensity = light.intensity, Enabled = light.enabled });
                    state.Light = light;
                    light.intensity = 0f;
                    light.enabled = false;
                }

                foreach (var light in root.GetComponentsInChildren<BaseLight>(true))
                {
                    if (!BlackoutTargets.IsFacilityComponent(light))
                        continue;
                    int key = light.gameObject.GetInstanceID();
                    if (!Areas.TryGetValue(key, out var state))
                        Areas.Add(key, state = new AreaState { Intensity = light.m_Intensity, Enabled = light.enabled });
                    state.Light = light;
                    light.m_Intensity = 0f;
                    light.enabled = false;
                }
            }
        }

        private static void Restore()
        {
            int count = Lamps.Count + Areas.Count;
            if (count == 0)
                return;
            bool liveLights = false;
            try
            {
                foreach (var state in Lamps.Values)
                    if (state.Light != null)
                    {
                        state.Light.intensity = state.Intensity;
                        state.Light.enabled = state.Enabled;
                        liveLights = true;
                    }

                foreach (var state in Areas.Values)
                    if (state.Light != null)
                    {
                        state.Light.m_Intensity = state.Intensity;
                        state.Light.enabled = state.Enabled;
                        liveLights = true;
                    }

                if (liveLights)
                    _reapply?.Invoke(null, null);
            }
            finally
            {
                Lamps.Clear();
                Areas.Clear();
            }
        }

        internal static void Reset() => SetBlackout(false);
        internal static void Dispose()
        {
            Reset();
            UnpatchSafely();
            _harmony = null;
            _reapply = null;
            _available = false;
            _blackout = false;
            Lamps.Clear();
            Areas.Clear();
        }

        private static void UnpatchSafely()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception)
            {
            }
        }
    }
}
