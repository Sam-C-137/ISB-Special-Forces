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
using UnityEngine.Networking;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private AudioClip FindCachedClipByName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return null;
            foreach (AudioClip clip in _audioClipCache.Values)
            {
                if (IsAudioClipReady(clip) && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return null;
        }

        private AudioClip FindCachedRadioClip(string category, string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return null;
            List<string> paths = GetAudioPathsForCategory(category);
            string exactPath = FindAudioPathByClipName(paths, clipName);
            AudioClip exactClip;
            if (!string.IsNullOrEmpty(exactPath) && _audioClipCache.TryGetValue(exactPath, out exactClip) && IsAudioClipReady(exactClip))
            {
                return exactClip;
            }

            List<AudioClip> categoryClips = GetAudioClipsForCategory(category);
            for (int i = 0; i < categoryClips.Count; i++)
            {
                AudioClip clip = categoryClips[i];
                if (IsAudioClipReady(clip) && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return null;
        }

        private List<string> GetAudioPathsForCategory(string category)
        {
            category = PortableRadioTiming.AudioCategory(category);
            if (string.Equals(category, "WDF", StringComparison.Ordinal))
                return _whiteTuskAishiDeathAudioPaths;
            if (string.Equals(category, "WHF", StringComparison.Ordinal))
                return _whiteTuskAishiHeavyDamageAudioPaths;
            if (string.Equals(category, "WNF", StringComparison.Ordinal))
                return _whiteTuskAishiBotAudioPaths;
            if (string.Equals(category, "WD", StringComparison.Ordinal))
                return _whiteTusksCommonDeathAudioPaths;
            if (string.Equals(category, "WH", StringComparison.Ordinal))
                return _whiteTusksCommonHeavyDamageAudioPaths;
            if (string.Equals(category, "WN", StringComparison.Ordinal))
                return _whiteTusksCommonBotAudioPaths;
            if (string.Equals(category, "DF", StringComparison.Ordinal))
                return _AishiDeathAudioPaths;
            if (string.Equals(category, "HF", StringComparison.Ordinal))
                return _AishiHeavyDamageAudioPaths;
            if (string.Equals(category, "NF", StringComparison.Ordinal))
                return _AishiBotAudioPaths;
            if (string.Equals(category, "D", StringComparison.Ordinal))
                return _deathAudioPaths;
            if (string.Equals(category, "H", StringComparison.Ordinal))
                return _heavyDamageAudioPaths;
            return _botAudioPaths;
        }

        private List<AudioClip> GetAudioClipsForCategory(string category)
        {
            category = PortableRadioTiming.AudioCategory(category);
            if (string.Equals(category, "WDF", StringComparison.Ordinal))
                return _wavsofWhiteTuskAishiDeaths;
            if (string.Equals(category, "WHF", StringComparison.Ordinal))
                return _wavsofWhiteTuskAishiHeavyDamage;
            if (string.Equals(category, "WNF", StringComparison.Ordinal))
                return _wavsofWhiteTuskAishiBots;
            if (string.Equals(category, "WD", StringComparison.Ordinal))
                return _wavsofWhiteTusksCommonDeaths;
            if (string.Equals(category, "WH", StringComparison.Ordinal))
                return _wavsofWhiteTusksCommonHeavyDamage;
            if (string.Equals(category, "WN", StringComparison.Ordinal))
                return _wavsofWhiteTusksCommonBots;
            if (string.Equals(category, "DF", StringComparison.Ordinal))
                return _wavsofAishiDeaths;
            if (string.Equals(category, "HF", StringComparison.Ordinal))
                return _wavsofAishiHeavyDamage;
            if (string.Equals(category, "NF", StringComparison.Ordinal))
                return _wavsofAishiBots;
            if (string.Equals(category, "D", StringComparison.Ordinal))
                return _wavsofDeaths;
            if (string.Equals(category, "H", StringComparison.Ordinal))
                return _wavsofHeavyDamage;
            return _wavsofBots;
        }

        private bool IsRadioCategoryEnabled(string category)
        {
            category = PortableRadioTiming.AudioCategory(category);
            if (string.Equals(category, "WDF", StringComparison.Ordinal))
                return _enableWhiteTuskAishi.Value && _enableAishiDeathChatter.Value;
            if (string.Equals(category, "WHF", StringComparison.Ordinal))
                return _enableWhiteTuskAishi.Value && _enableAishiHeavyDamageChatter.Value;
            if (string.Equals(category, "WNF", StringComparison.Ordinal))
                return _enableWhiteTuskAishi.Value && _enableAishiRadioChatter.Value;
            if (string.Equals(category, "WD", StringComparison.Ordinal))
                return _enableWhiteTusksCommon.Value && _enableDeathChatter.Value;
            if (string.Equals(category, "WH", StringComparison.Ordinal))
                return _enableWhiteTusksCommon.Value && _enableHeavyDamageChatter.Value;
            if (string.Equals(category, "WN", StringComparison.Ordinal))
                return _enableWhiteTusksCommon.Value && _enableRadioChatter.Value;
            if (string.Equals(category, "DF", StringComparison.Ordinal))
                return _enableAishiDeathChatter.Value;
            if (string.Equals(category, "HF", StringComparison.Ordinal))
                return _enableAishiHeavyDamageChatter.Value;
            if (string.Equals(category, "NF", StringComparison.Ordinal))
                return _enableAishiRadioChatter.Value;
            if (string.Equals(category, "D", StringComparison.Ordinal))
                return _enableDeathChatter.Value;
            if (string.Equals(category, "H", StringComparison.Ordinal))
                return _enableHeavyDamageChatter.Value;
            return string.Equals(category, "N", StringComparison.Ordinal) && _enableRadioChatter.Value;
        }

        private void AddClipToCategoryList(string category, AudioClip clip)
        {
            if (!IsAudioClipReady(clip))
                return;
            List<AudioClip> list = GetAudioClipsForCategory(category);
            if (!list.Contains(clip))
                list.Add(clip);
        }

        private static string FindAudioPathByClipName(List<string> paths, string clipName)
        {
            if (paths == null || string.IsNullOrEmpty(clipName))
                return null;
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (string.Equals(Path.GetFileNameWithoutExtension(path), clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        private void IndexAudioPaths()
        {
            IndexWavFolder(_ISBSpecialForces_RadiosFolder, _botAudioPaths, "ISB SOF Audio Plugin: Radio comms folder not found.");
            IndexWavFolder(_ISBSpecialForces_DeathRadiosFolder, _deathAudioPaths, "ISB SOF Audio Plugin: ISB KIA comms folder not found.");
            IndexWavFolder(_ISBSpecialForces_HeavyDamageRadiosFolder, _heavyDamageAudioPaths, "ISB SOF Audio Plugin: Heavy damage comms folder not found.");
            IndexOptionalWavFolder(_enableAishiRadioChatter.Value, _ISBSpecialForces_AishiRadiosFolder, _AishiBotAudioPaths, "ISB SOF Audio Plugin: Aishi radio comms folder not found.");
            IndexOptionalWavFolder(_enableAishiDeathChatter.Value, _ISBSpecialForces_AishiDeathRadiosFolder, _AishiDeathAudioPaths, "ISB SOF Audio Plugin: Aishi KIA comms folder not found.");
            IndexOptionalWavFolder(_enableAishiHeavyDamageChatter.Value, _ISBSpecialForces_AishiHeavyDamageRadiosFolder, _AishiHeavyDamageAudioPaths, "ISB SOF Audio Plugin: Aishi heavy damage comms folder not found.");
            IndexOptionalWavFolder(_enableWhiteTusksCommon.Value && _enableRadioChatter.Value, _ISBSpecialForces_WhiteTusksCommonRadiosFolder, _whiteTusksCommonBotAudioPaths, "ISB SOF Audio Plugin: White Tusks common radio folder not found.");
            IndexOptionalWavFolder(_enableWhiteTusksCommon.Value && _enableDeathChatter.Value, _ISBSpecialForces_WhiteTusksCommonDeathRadiosFolder, _whiteTusksCommonDeathAudioPaths, "ISB SOF Audio Plugin: White Tusks common KIA folder not found.");
            IndexOptionalWavFolder(_enableWhiteTusksCommon.Value && _enableHeavyDamageChatter.Value, _ISBSpecialForces_WhiteTusksCommonHeavyDamageRadiosFolder, _whiteTusksCommonHeavyDamageAudioPaths, "ISB SOF Audio Plugin: White Tusks common heavy damage folder not found.");
            IndexOptionalWavFolder(_enableWhiteTuskAishi.Value && _enableAishiRadioChatter.Value, _ISBSpecialForces_WhiteTuskAishiRadiosFolder, _whiteTuskAishiBotAudioPaths, "ISB SOF Audio Plugin: White Tusk Aishi radio folder not found.");
            IndexOptionalWavFolder(_enableWhiteTuskAishi.Value && _enableAishiDeathChatter.Value, _ISBSpecialForces_WhiteTuskAishiDeathRadiosFolder, _whiteTuskAishiDeathAudioPaths, "ISB SOF Audio Plugin: White Tusk Aishi KIA folder not found.");
            IndexOptionalWavFolder(_enableWhiteTuskAishi.Value && _enableAishiHeavyDamageChatter.Value, _ISBSpecialForces_WhiteTuskAishiHeavyDamageRadiosFolder, _whiteTuskAishiHeavyDamageAudioPaths, "ISB SOF Audio Plugin: White Tusk Aishi heavy damage folder not found.");
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF audio: Indexed radio=" + _botAudioPaths.Count + "/" + _AishiBotAudioPaths.Count + ", KIA=" + _deathAudioPaths.Count + "/" + _AishiDeathAudioPaths.Count + ", heavy damage=" + _heavyDamageAudioPaths.Count + "/" + _AishiHeavyDamageAudioPaths.Count + " (standard/Aishi).");
            }
        }

        private void IndexWavFolder(string folder, List<string> target, string missingMessage)
        {
            target.Clear();
            if (!Directory.Exists(folder))
            {
                Log.LogWarning(missingMessage);
                return;
            }

            target.AddRange(Directory.GetFiles(folder, "*.wav"));
        }

        private void IndexOptionalWavFolder(bool enabled, string folder, List<string> target, string missingMessage)
        {
            target.Clear();
            if (!enabled)
                return;
            if (!Directory.Exists(folder))
            {
                Log.LogWarning(missingMessage);
                return;
            }

            target.AddRange(Directory.GetFiles(folder, "*.wav"));
        }

        private IEnumerator PreloadAudioCacheGradually()
        {
            if (_audioCacheStarted)
                yield break;
            _audioCacheStarted = true;
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF audio: Starting bounded announcement preload; remaining clips load on demand.");
            }

            if (IsHeadless || !IsLaboratoryPresentationAllowed())
            {
                _announcementCacheReady = true;
                _audioCacheRoutine = null;
                yield break;
            }

            HashSet<string> announcementFilesToLoad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_blackoutSoundPath))
                announcementFilesToLoad.Add(BlackoutBridge.BlackoutSoundFileName);
            foreach (PreBlackoutSequenceDefinition sequence in _preBlackoutSequences)
            {
                announcementFilesToLoad.Add(sequence.preBlackout);
                foreach (AnnouncementAudioDefinition entry in sequence.afterBlackout)
                    announcementFilesToLoad.Add(entry.audio);
            }

            foreach (AnnouncementSequenceDefinition sequence in _labsAnnouncements.regular)
            {
                foreach (AnnouncementAudioDefinition entry in sequence.audios)
                    announcementFilesToLoad.Add(entry.audio);
            }

            foreach (AnnouncementSequenceDefinition sequence in _labsAnnouncements.blackout)
            {
                foreach (AnnouncementAudioDefinition entry in sequence.audios)
                    announcementFilesToLoad.Add(entry.audio);
            }

            int preloaded = 0;
            foreach (string fileName in announcementFilesToLoad)
            {
                if (IsEventAuthority && ShouldSkipDecontaminationAudio(fileName))
                    continue;
                if (preloaded++ >= AnnouncementPreloadLimit)
                    break;
                if (!_raidisActive)
                    yield break;
                string filePath = Path.Combine(_dataFolder, fileName);
                yield return StartCoroutine(LoadAudioClip(filePath, null));
                yield return null;
            }

            _announcementCacheReady = true;
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF audio: Announcement preload ready. cached=" + _audioClipCache.Count + ".");
            }

            _audioCacheRoutine = null;
        }

        private IEnumerator LoadAudioClip(string fullPath, Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrEmpty(fullPath) || !_raidisActive || IsHeadless)
                yield break;
            int generation = _audioGeneration;
            CancellationToken token = _audioCancellation.Token;
            while (_loadingAudio.Contains(fullPath))
            {
                yield return null;
                if (token.IsCancellationRequested || generation != _audioGeneration)
                    yield break;
            }

            AudioClip cachedClip;
            if (_audioClipCache.TryGetValue(fullPath, out cachedClip))
            {
                if (IsAudioClipReady(cachedClip))
                {
                    _audioLastUsed[cachedClip] = Time.realtimeSinceStartup;
                    onLoaded?.Invoke(cachedClip);
                    yield break;
                }

                _audioClipCache.Remove(fullPath);
                if (!ReferenceEquals(cachedClip, null))
                    _audioLastUsed.Remove(cachedClip);
                if (cachedClip != null)
                    Destroy(cachedClip);
            }

            if (!File.Exists(fullPath))
            {
                Log.LogWarning("ISB SOF audio: Missing file: " + fullPath);
                yield break;
            }

            _loadingAudio.Add(fullPath);
            AudioClip clip = null;
            bool retained = false;
            try
            {
                var decode = PcmWavDecoder.DecodeAsync(fullPath, token);
                while (!decode.IsCompleted)
                {
                    if (token.IsCancellationRequested || generation != _audioGeneration)
                        yield break;
                    yield return null;
                }

                if (token.IsCancellationRequested || generation != _audioGeneration)
                    yield break;
                if (decode.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && decode.Result != null)
                    clip = CreateDecodedAudioClip(decode.Result, fullPath);
                if (!IsAudioClipReady(clip))
                {
                    if (clip != null)
                        Destroy(clip);
                    clip = null;
                    string uri = new Uri(fullPath).AbsoluteUri;
                    using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
                    {
                        var handler = www.downloadHandler as DownloadHandlerAudioClip;
                        if (handler != null)
                            handler.streamAudio = false;
                        var operation = www.SendWebRequest();
                        while (!operation.isDone)
                        {
                            if (token.IsCancellationRequested || generation != _audioGeneration)
                                yield break;
                            yield return null;
                        }

                        if (token.IsCancellationRequested || generation != _audioGeneration)
                            yield break;
                        if (www.result == UnityWebRequest.Result.Success)
                        {
                            AudioClip requestClip = DownloadHandlerAudioClip.GetContent(www);
                            try
                            {
                                if (requestClip != null)
                                {
                                    if (requestClip.loadState == AudioDataLoadState.Unloaded)
                                        requestClip.LoadAudioData();
                                    float deadline = Time.realtimeSinceStartup + 5f;
                                    while (requestClip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline)
                                    {
                                        if (token.IsCancellationRequested || generation != _audioGeneration)
                                            yield break;
                                        yield return null;
                                    }

                                    if (token.IsCancellationRequested || generation != _audioGeneration)
                                        yield break;
                                    clip = CloneAudioClipToOwned(requestClip, fullPath);
                                }
                            }
                            finally
                            {
                                if (requestClip != null)
                                    Destroy(requestClip);
                            }
                        }
                    }
                }

                if (!IsAudioClipReady(clip) || token.IsCancellationRequested || generation != _audioGeneration)
                {
                    if (!token.IsCancellationRequested && generation == _audioGeneration)
                        Log.LogWarning("ISB SOF audio: WAV decoding and Unity fallback failed: " + fullPath);
                    yield break;
                }

                _audioClipCache[fullPath] = clip;
                _audioLastUsed[clip] = Time.realtimeSinceStartup;
                retained = true;
                onLoaded?.Invoke(clip);
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogDebug($"ISB SOF Audio Plugin: Audio clip ready | clip: {clip.name} | " + $"length: {clip.length:F2}s | samples: {clip.samples} | channels: {clip.channels} | " + $"frequency: {clip.frequency}Hz | state: {clip.loadState}.");
                }
            }
            finally
            {
                if (!retained && clip != null)
                    Destroy(clip);
                if (generation == _audioGeneration)
                    _loadingAudio.Remove(fullPath);
            }
        }

        private AudioClip CreateDecodedAudioClip(PcmWavData data, string fullPath)
        {
            AudioClip clip = null;
            try
            {
                clip = AudioClip.Create(Path.GetFileNameWithoutExtension(fullPath), data.Samples.Length / data.Channels, data.Channels, data.Frequency, false);
                if (clip != null && clip.SetData(data.Samples, 0))
                    return clip;
            }
            catch (Exception)
            {
            }

            if (clip != null)
                Destroy(clip);
            return null;
        }

        private AudioClip CloneAudioClipToOwned(AudioClip source, string fullPath)
        {
            if (!IsAudioClipReady(source))
                return null;
            try
            {
                int sampleValueCount = checked(source.samples * source.channels);
                float[] samples = new float[sampleValueCount];
                if (!source.GetData(samples, 0))
                    throw new InvalidOperationException("AudioClip.GetData failed.");
                AudioClip ownedClip = AudioClip.Create(Path.GetFileNameWithoutExtension(fullPath), source.samples, source.channels, source.frequency, false);
                if (ownedClip == null || !ownedClip.SetData(samples, 0))
                {
                    if (ownedClip != null)
                        Destroy(ownedClip);
                    throw new InvalidOperationException("AudioClip.SetData failed.");
                }

                return ownedClip;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsAudioClipReady(AudioClip clip)
        {
            return clip != null && clip.loadState == AudioDataLoadState.Loaded && clip.samples > 0 && clip.channels > 0 && clip.frequency > 0 && clip.length > 0.01f;
        }

        private readonly HashSet<AudioClip> _pinnedAudio = new HashSet<AudioClip>();
        private readonly List<KeyValuePair<string, AudioClip>> _unusedAudio = new List<KeyValuePair<string, AudioClip>>();
        private Comparison<KeyValuePair<string, AudioClip>> _audioComparison;
        private int CompareAudioAge(KeyValuePair<string, AudioClip> left, KeyValuePair<string, AudioClip> right) => AudioLastUsed(left.Value).CompareTo(AudioLastUsed(right.Value));
        private void TrimAudioClipCache()
        {
            float now = Time.realtimeSinceStartup;
            if (!_raidisActive || now < _nextAudioCacheTrim)
                return;
            _nextAudioCacheTrim = now + 5f;
            long bytes = 0;
            foreach (var clip in _audioClipCache.Values)
                if (clip != null)
                    bytes += (long)clip.samples * clip.channels * sizeof(float);
            if (bytes <= AudioCacheBudget)
                return;
            var pinned = _pinnedAudio;
            pinned.Clear();
            if (_takeoverAlarmSource != null)
                pinned.Add(_takeoverAlarmSource.clip);
            if (_facilityAudio != null)
                pinned.Add(_facilityAudio.clip);
            if (_facilityExplosionClips != null)
                foreach (var clip in _facilityExplosionClips)
                    pinned.Add(clip);
            foreach (var active in _activeAudioObjects)
                if (active?.Source != null)
                    pinned.Add(active.Source.clip);
            foreach (var active in _activeLaboratoryPaSources)
                if (active?.Source != null)
                    pinned.Add(active.Source.clip);
            var unused = _unusedAudio;
            unused.Clear();
            foreach (var entry in _audioClipCache)
            {
                if (entry.Value == null)
                    continue;
                if (_audioLastUsed.TryGetValue(entry.Value, out float lastUsed) && now - lastUsed < 30f)
                    continue;
                if (!pinned.Contains(entry.Value))
                    unused.Add(entry);
            }

            if (_audioComparison == null)
                _audioComparison = CompareAudioAge;
            unused.Sort(_audioComparison);
            foreach (var entry in unused)
            {
                if (bytes <= AudioCacheBudget)
                    break;
                bytes -= (long)entry.Value.samples * entry.Value.channels * sizeof(float);
                _audioClipCache.Remove(entry.Key);
                _audioLastUsed.Remove(entry.Value);
                Destroy(entry.Value);
            }

            pinned.Clear();
            unused.Clear();
        }

        private float AudioLastUsed(AudioClip clip)
        {
            return _audioLastUsed.TryGetValue(clip, out float time) ? time : 0f;
        }

        private void DestroyAudioClipCache()
        {
            _audioGeneration++;
            _audioCancellation.Cancel();
            _audioCancellation.Dispose();
            _audioCancellation = new CancellationTokenSource();
            _loadingAudio.Clear();
            _audioLastUsed.Clear();
            _nextAudioCacheTrim = 0f;
            foreach (AudioClip clip in _audioClipCache.Values)
            {
                if (clip != null)
                    Destroy(clip);
            }

            _audioClipCache.Clear();
            _pinnedAudio.Clear();
            _unusedAudio.Clear();
        }

        private float GetWavDurationSeconds(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (new string (reader.ReadChars(4)) != "RIFF")
                        return 0.05f;
                    reader.ReadInt32();
                    if (new string (reader.ReadChars(4)) != "WAVE")
                        return 0.05f;
                    int byteRate = 0;
                    int dataSize = 0;
                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunkId = new string (reader.ReadChars(4));
                        int chunkSize = reader.ReadInt32();
                        long nextChunk = stream.Position + chunkSize + (chunkSize & 1);
                        if (chunkId == "fmt " && chunkSize >= 16)
                        {
                            reader.ReadInt16();
                            reader.ReadInt16();
                            reader.ReadInt32();
                            byteRate = reader.ReadInt32();
                        }
                        else if (chunkId == "data")
                        {
                            dataSize = chunkSize;
                        }

                        stream.Position = Math.Min(nextChunk, stream.Length);
                        if (byteRate > 0 && dataSize > 0)
                            break;
                    }

                    if (byteRate > 0 && dataSize > 0)
                        return Mathf.Max((float)dataSize / byteRate, 0.05f);
                }
            }
            catch (Exception)
            {
            }

            return 0.05f;
        }
    }
}
