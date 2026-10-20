using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Text;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private static bool ShouldRouteThroughLaboratoryPa(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
                return false;
            string normalized = Path.GetFileNameWithoutExtension(clipName);
            const string legacyEffectSuffix = "_effect";
            if (normalized.EndsWith(legacyEffectSuffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - legacyEffectSuffix.Length);
            }

            return LaboratoryPaAnnouncementClips.Contains(normalized);
        }

        private IEnumerator RouteBlackoutClipIntoLaboratoryPa(AudioClip clip, float startOffsetSeconds)
        {
            if (!IsAudioClipReady(clip))
                yield break;
            startOffsetSeconds = Mathf.Clamp(startOffsetSeconds, 0f, Mathf.Max(0f, clip.length - 0.05f));
            if (!CanPresentLocally)
            {
                yield return new WaitForSeconds(Mathf.Max(clip.length - startOffsetSeconds, 0.05f));
                yield break;
            }

            List<AudioSource> channels = CollectLaboratoryPaChannels();
            if (channels.Count == 0)
            {
                Log.LogWarning("ISB SOF Audio Plugin: No Laboratory PA channels associated with '" + LaboratoryPaObjectToken + "' were found. Using the existing announcement source as fallback for '" + clip.name + "'.");
                yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), AnnouncementMinDistance, AnnouncementMaxDistance, AnnouncementBaseVolume, _announcementsVolume, true, startOffsetSeconds, false, false, AnnouncementSpatialBlend, AnnouncementRolloffMode);
                yield break;
            }

            RestoreLaboratoryPaSources();
            int playbackVersion = _laboratoryPaPlaybackVersion;
            float sliderFactor = GetVolumeFactor(_announcementsVolume);
            AudioSource subtitleClock = null;
            int startedChannels = 0;
            HashSet<int> paObjects = new HashSet<int>();
            for (int i = 0; i < channels.Count; i++)
            {
                AudioSource channel = channels[i];
                if (channel == null)
                    continue;
                LaboratoryPaSourceSnapshot snapshot = LaboratoryPaSourceSnapshot.Capture(channel);
                _activeLaboratoryPaSources.Add(snapshot);
                try
                {
                    channel.Stop();
                    channel.clip = clip;
                    channel.pitch = 1f;
                    channel.loop = false;
                    channel.volume = snapshot.OriginalVolume * sliderFactor;
                    SetAudioSourceOffset(channel, clip, startOffsetSeconds);
                    AnnouncementFade.Begin(channel, clip, snapshot.OriginalVolume * sliderFactor, startOffsetSeconds);
                    channel.Play();
                    startedChannels++;
                    paObjects.Add(channel.gameObject.GetInstanceID());
                    if (subtitleClock == null || channel.volume > subtitleClock.volume)
                        subtitleClock = channel;
                }
                catch (Exception exception)
                {
                    Log.LogWarning("ISB SOF Audio Plugin: A Laboratory PA channel rejected clip '" + clip.name + "': " + exception.Message);
                }
            }

            if (startedChannels == 0)
            {
                RestoreLaboratoryPaSources();
                Log.LogWarning("ISB SOF Audio Plugin: Laboratory PA channels were detected, but none could " + "start the blackout announcement. Using the existing fallback source.");
                yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), AnnouncementMinDistance, AnnouncementMaxDistance, AnnouncementBaseVolume, _announcementsVolume, true, startOffsetSeconds, false, false, AnnouncementSpatialBlend, AnnouncementRolloffMode);
                yield break;
            }

            if (_subtitleSystem != null && subtitleClock != null)
                _subtitleSystem.Play(subtitleClock, clip.name);
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Audio Plugin: Routed blackout announcement '" + clip.name + "' through " + startedChannels + " Laboratory PA audio channel(s) across " + paObjects.Count + " scene object(s). Existing mixer, spatialization and " + "environmental channel settings were preserved.");
            }

            yield return WaitAnnouncement(clip.name, Mathf.Max(clip.length - startOffsetSeconds, .05f) + .15f, channels.ToArray());
            if (playbackVersion == _laboratoryPaPlaybackVersion)
                RestoreLaboratoryPaSources();
        }

        private List<AudioSource> _laboratoryPaChannels;
        private int _laboratoryPaSceneKey;

        private void ResetLaboratoryPaChannels()
        {
            _laboratoryPaChannels = null;
            _laboratoryPaSceneKey = 0;
        }

        private List<AudioSource> CollectLaboratoryPaChannels()
        {
            int sceneKey = 17;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                sceneKey = unchecked(sceneKey * 31 + SceneManager.GetSceneAt(i).handle);
            bool valid = _laboratoryPaChannels != null && _laboratoryPaChannels.Count > 0 && _laboratoryPaSceneKey == sceneKey;
            if (valid)
                foreach (var channel in _laboratoryPaChannels)
                    if (channel == null || !channel.enabled || !channel.gameObject.activeInHierarchy)
                    {
                        valid = false;
                        break;
                    }
            if (valid)
                return _laboratoryPaChannels;
            AudioSource[] sceneSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            List<AudioSource> result = new List<AudioSource>();
            HashSet<int> seenSources = new HashSet<int>();
            for (int i = 0; i < sceneSources.Length; i++)
            {
                AudioSource source = sceneSources[i];
                if (source == null || !source.enabled || source.gameObject == null || !source.gameObject.activeInHierarchy || !BelongsToLaboratoryPa(source.transform))
                {
                    continue;
                }

                int instanceId = source.GetInstanceID();
                if (seenSources.Add(instanceId))
                    result.Add(source);
            }

            result.Sort(delegate (AudioSource left, AudioSource right)
            {
                float leftVolume = left != null ? left.volume : 0f;
                float rightVolume = right != null ? right.volume : 0f;
                return rightVolume.CompareTo(leftVolume);
            });
            _laboratoryPaChannels = result;
            _laboratoryPaSceneKey = sceneKey;
            return result;
        }

        private static bool BelongsToLaboratoryPa(Transform sourceTransform)
        {
            Transform current = sourceTransform;
            int remainingDepth = LaboratoryPaParentSearchDepth;
            while (current != null && remainingDepth-- > 0)
            {
                if (!string.IsNullOrEmpty(current.name) && current.name.IndexOf(LaboratoryPaObjectToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void SetAudioSourceOffset(AudioSource source, AudioClip clip, float startOffsetSeconds)
        {
            if (source == null || clip == null || startOffsetSeconds <= 0f)
                return;
            try
            {
                int maximumSample = Mathf.Max(clip.samples - 1, 0);
                int requestedSample = Mathf.RoundToInt(startOffsetSeconds * clip.frequency);
                source.timeSamples = Mathf.Clamp(requestedSample, 0, maximumSample);
            }
            catch
            {
                try
                {
                    source.time = startOffsetSeconds;
                }
                catch
                {
                }
            }
        }

        private void RestoreLaboratoryPaSources()
        {
            _laboratoryPaPlaybackVersion++;
            for (int i = _activeLaboratoryPaSources.Count - 1; i >= 0; i--)
            {
                LaboratoryPaSourceSnapshot snapshot = _activeLaboratoryPaSources[i];
                if (snapshot != null)
                    snapshot.Restore();
            }

            _activeLaboratoryPaSources.Clear();
        }

        private sealed class LaboratoryPaSourceSnapshot
        {
            public AudioSource Source;
            public AudioClip OriginalClip;
            public float OriginalPitch;
            public float OriginalVolume;
            public bool OriginalLoop;
            public bool WasPlaying;
            public int OriginalTimeSamples;
            public static LaboratoryPaSourceSnapshot Capture(AudioSource source)
            {
                LaboratoryPaSourceSnapshot snapshot = new LaboratoryPaSourceSnapshot
                {
                    Source = source,
                    OriginalClip = source != null ? source.clip : null,
                    OriginalPitch = source != null ? source.pitch : 1f,
                    OriginalVolume = AnnouncementFade.Volume(source),
                    OriginalLoop = source != null && source.loop,
                    WasPlaying = source != null && source.isPlaying,
                    OriginalTimeSamples = 0
                };
                if (source != null && source.clip != null)
                {
                    try
                    {
                        snapshot.OriginalTimeSamples = source.timeSamples;
                    }
                    catch
                    {
                    }
                }

                return snapshot;
            }

            public void Restore()
            {
                if (Source == null)
                    return;
                try
                {
                    AnnouncementFade.End(Source);
                    Source.Stop();
                    Source.clip = OriginalClip;
                    Source.pitch = OriginalPitch;
                    Source.volume = OriginalVolume;
                    Source.loop = OriginalLoop;
                    if (OriginalClip != null && OriginalTimeSamples > 0)
                    {
                        int maximumSample = Mathf.Max(OriginalClip.samples - 1, 0);
                        Source.timeSamples = Mathf.Clamp(OriginalTimeSamples, 0, maximumSample);
                    }

                    if (WasPlaying && OriginalClip != null)
                    {
                        AnnouncementFade.Begin(Source, OriginalClip, OriginalVolume, Source.time);
                        Source.Play();
                    }
                }
                catch
                {
                }
            }
        }
    }
}
