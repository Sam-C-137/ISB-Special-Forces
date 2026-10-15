using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ISBSSOFExtra
{
    internal static class LocalPlayerModCompatibility
    {
        private static readonly HashSet<MethodInfo> Patched = new HashSet<MethodInfo>();
        private static readonly string[] PlayerStartHandlers =
        {
            "EternalCycleClient.GameStartPatch",
            "EternalCycleClient.EternalCycleItem+InfinityStamina+GameStartPatch"
        };
        private static Harmony _harmony;

        internal static void Enable(ManualLogSource log)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var name in PlayerStartHandlers)
                {
                    var type = assembly.GetType(name, false);
                    var method = type?.GetMethod("Postfix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(GameWorld) }, null);
                    if (method == null || method.ReturnType != typeof(void) || Patched.Contains(method))
                        continue;
                    try
                    {
                        var prefix = new HarmonyMethod(typeof(LocalPlayerModCompatibility), nameof(HasLocalPlayer)) { priority = Priority.First };
                        if (_harmony == null)
                            _harmony = new Harmony("com.samc137.isbsofextras.local-player-mods");
                        _harmony.Patch(method, prefix: prefix);
                        Patched.Add(method);
                    }
                    catch (Exception e)
                    {
                        log?.LogError("ISB local-player compatibility failed for " + name + ": " + e.GetBaseException().Message);
                    }
                }
                var freecam = assembly.GetType("Terkoiz.Freecam.FreecamController", false);
                PatchFreecam(freecam?.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance), nameof(BeforeFreecamStart), log);
                PatchFreecam(freecam?.GetProperty("IsFreecamActive", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(), nameof(BeforeFreecamState), log);
            }
        }

        private static bool HasLocalPlayer(GameWorld __0) => __0 != null && __0.MainPlayer != null;

        internal static bool HasHeadlessWithoutPlayer(GameWorld world) =>
            (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) &&
            (world == null || world.MainPlayer == null);

        private static void PatchFreecam(MethodInfo method, string handler, ManualLogSource log)
        {
            if (method == null || Patched.Contains(method))
                return;
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.samc137.isbsofextras.local-player-mods");
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(LocalPlayerModCompatibility), handler) { priority = Priority.First });
                Patched.Add(method);
            }
            catch (Exception e)
            {
                log?.LogError("ISB freecam compatibility failed: " + e.GetBaseException().Message);
            }
        }

        private static bool BeforeFreecamStart(MonoBehaviour __instance)
        {
            if (!HasHeadlessWithoutPlayer(Singleton<GameWorld>.Instance))
                return true;
            __instance.enabled = false;
            return false;
        }

        private static bool BeforeFreecamState(ref bool __result)
        {
            if (!HasHeadlessWithoutPlayer(Singleton<GameWorld>.Instance))
                return true;
            __result = false;
            return false;
        }

        internal static void Dispose()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Patched.Clear();
        }
    }
}
