using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal static class LabsNativeAnnouncements
    {
        private static Harmony _harmony;
        private static GameWorld _world;
        private static bool _blackoutActive, _userMuted;
        private static readonly Dictionary<QueuePlayer, bool> PaQueues = new Dictionary<QueuePlayer, bool>();
        private static readonly List<AudioSource> Sources = new List<AudioSource>();
        internal static void Enable(ManualLogSource log)
        {
            if (_harmony != null)
                return;
            try
            {
                _harmony = new Harmony("com.samc137.isbsofextras.native-announcements");
                foreach (string name in new[]
                {
                    "Play",
                    "PlayInternal"
                }

                )
                {
                    var method = AccessTools.Method(typeof(QueuePlayer), name, new[] { typeof(AudioClip) });
                    if (method == null)
                        throw new MissingMethodException(typeof(QueuePlayer).FullName, name);
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(LabsNativeAnnouncements), name != "Play" ? nameof(BeforePlayInternal) : nameof(CanPlay)));
                }
            }
            catch (Exception ex)
            {
                Disable();
                log?.LogError("ISB SOF: Native announcement suppression unavailable: " + ex.GetBaseException().Message);
            }
        }

        internal static void SetActive(bool active)
        {
            _blackoutActive = active;
            RefreshState();
        }

        internal static void SetUserMuted(bool muted)
        {
            _userMuted = muted;
            RefreshState();
        }

        private static void RefreshState()
        {
            var world = (_blackoutActive || _userMuted) ? Singleton<GameWorld>.Instance : null;
            if (ReferenceEquals(_world, world))
                return;
            _world = world;
            PaQueues.Clear();
            if (_world == null)
                return;
            foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>(true))
                if (IsPaSource(source))
                    source.Stop();
        }

        private static bool IsPaSource(AudioSource source) => source != null && source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded && source.gameObject.scene.name.StartsWith("Laboratory_", StringComparison.Ordinal) && source.name.IndexOf("sound_disclamer", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool BeforePlayInternal(QueuePlayer __instance, AudioClip __0)
        {
            var clip = __0;
            if (!CanPlay(__instance))
                return false;
            if (__instance == null || clip == null)
                return true;
            Sources.Clear();
            __instance.GetComponentsInChildren(true, Sources);
            foreach (var source in Sources)
                if (IsPaSource(source))
                    AnnouncementFade.Begin(source, clip, AnnouncementFade.Volume(source), 0f);
            Sources.Clear();
            return true;
        }

        private static bool CanPlay(QueuePlayer __instance)
        {
            if (_world == null || !ReferenceEquals(_world, Singleton<GameWorld>.Instance) || __instance == null)
                return true;
            if (PaQueues.TryGetValue(__instance, out bool pa))
                return !pa;
            Sources.Clear();
            __instance.GetComponentsInChildren(true, Sources);
            foreach (var source in Sources)
                if (IsPaSource(source))
                {
                    PaQueues[__instance] = true;
                    Sources.Clear();
                    return false;
                }

            Sources.Clear();
            PaQueues[__instance] = false;
            return true;
        }

        internal static void Disable()
        {
            _userMuted = false;
            SetActive(false);
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
