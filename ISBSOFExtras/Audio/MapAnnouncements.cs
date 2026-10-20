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
        private IEnumerator ScanForMainSequence()
        {
            if (!IsEventAuthority)
                yield break;
            float sequenceTargetTime = Time.realtimeSinceStartup + 25f;
            if (IsFikaNetworkSession && _fikaRaidStartedObservedAt >= 0f)
                sequenceTargetTime = _fikaRaidStartedObservedAt + 25f;
            while (_raidisActive && Time.realtimeSinceStartup < sequenceTargetTime)
                yield return null;
            while (_raidisActive && !_sequenceStarted)
            {
                if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                {
                    yield return new WaitForSeconds(10f);
                    continue;
                }

                CacheEligibleBots(Singleton<GameWorld>.Instance);
                ResolveBlackoutChanceOnce();
                if (_blackoutSelectedForRaid || _labsPreBlackoutEventActive || _listofactiveBots.Count > 0 || _selectedLabsVariant == "containment" || _selectedLabsVariant == "blackout")
                {
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        Log.LogInfo("ISB SOF Audio Plugin: ISB Operator Detected - Starting announcements sequence");
                    }

                    _sequenceStarted = true;
                    if (_announcementRout != null)
                        StopCoroutine(_announcementRout);
                    _announcementRout = StartCoroutine(ISBAnnouncementSequence());
                    yield break;
                }

                yield return new WaitForSeconds(10f);
            }

            _scanner = null;
        }

        private IEnumerator ISBAnnouncementSequence()
        {
            int generation = _audioGeneration;
            try
            {
                if (!CanContinueAnnouncement(generation))
                    yield break;
                yield return new WaitForSeconds(1.5f);
                if (!CanContinueAnnouncement(generation))
                    yield break;
                if (!_labsPreBlackoutEventActive)
                    _labsPreBlackoutEventActive = LoadLabsPreBlackoutEventStatus();
                SyncTakeoverFireLocal();
                ResolveBlackoutChanceOnce();
                while (!_blackoutChanceResolved && CanContinueAnnouncement(generation))
                {
                    yield return new WaitForSeconds(0.5f);
                    ResolveBlackoutChanceOnce();
                }

                if (!CanContinueAnnouncement(generation))
                    yield break;
                LabsAnnouncementPlan plan = LabsAnnouncementConfig.Select(_labsAnnouncements, _preBlackoutSequences, _blackoutSelectedForRaid, _labsPreBlackoutEventActive, true, () => UnityEngine.Random.value);
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF announcements: blackout=" + _blackoutSelectedForRaid + ", containment=" + _labsPreBlackoutEventActive + ", preBlackout=" + plan.PreBlackout + ", entries=" + plan.Audios.Length + ", initialDelay=" + plan.InitialDelay + ".");
                }

                if (_blackoutSelectedForRaid)
                {
                    if (!IsFikaNetworkSession)
                    {
                        if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                        {
                            Log.LogInfo("ISB SOF Audio Plugin: Standalone Laboratory blackout selected; delaying the event by " + StandaloneBlackoutDelay.ToString("F0", CultureInfo.InvariantCulture) + " seconds.");
                        }

                        yield return new WaitForSeconds(StandaloneBlackoutDelay);
                        if (!CanContinueAnnouncement(generation))
                            yield break;
                    }

                    Coroutine preBlackoutPlayback = null;
                    if (!string.IsNullOrEmpty(plan.PreBlackout))
                    {
                        preBlackoutPlayback = StartCoroutine(PlayPreBlackoutAudio(plan.PreBlackout));
                        yield return null;
                        if (!CanContinueAnnouncement(generation))
                            yield break;
                    }

                    if (!_blackoutApplied)
                    {
                        StopRoutine(ref _blackoutRoutine);
                        Coroutine activeBlackoutRoutine = StartCoroutine(DisableLaboratoryBlackout());
                        _blackoutRoutine = _blackoutTransitionInProgress ? activeBlackoutRoutine : null;
                        yield return activeBlackoutRoutine;
                        if (!CanContinueAnnouncement(generation))
                            yield break;
                    }

                    if (preBlackoutPlayback != null)
                        yield return preBlackoutPlayback;
                }
                else
                {
                    ActivateManualShutdownControls();
                }

                if (!CanContinueAnnouncement(generation))
                    yield break;
                if (plan.InitialDelay > 0f)
                    yield return new WaitForSeconds(plan.InitialDelay);
                yield return PlayAnnouncementEntries(plan.Audios, generation);
            }
            finally
            {
                if (generation == _audioGeneration)
                    _announcementRout = null;
            }
        }

        private bool CanContinueAnnouncement(int generation)
        {
            return !DeadManActive && generation == _audioGeneration && _raidisActive && IsEventAuthority && IsLaboratoryPresentationAllowed();
        }

        private IEnumerator PlayAnnouncementEntries(AnnouncementAudioDefinition[] entries, int generation)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (!CanContinueAnnouncement(generation))
                    yield break;
                while (CanContinueAnnouncement(generation) && DecontaminationPaused && IsDecontaminationClip(entries[i].audio))
                    yield return null;
                if (ShouldSkipDecontaminationAudio(entries[i].audio))
                    continue;
                if ((_decontamination?.VideoTimelineEnabled ?? true) && DecontaminationVideoTimeline.Index(entries[i].audio) == 0)
                    StartDecontaminationVideo(DecontaminationClock);
                double scheduledStart = DecontaminationAnnouncementTime(entries[i].audio);
                while (CanContinueAnnouncement(generation) && ((!ShouldSkipDecontaminationAudio(entries[i].audio) && DecontaminationClock < scheduledStart) || (DecontaminationPaused && IsDecontaminationClip(entries[i].audio))))
                    yield return null;
                if (!CanContinueAnnouncement(generation))
                    yield break;
                while (CanContinueAnnouncement(generation) && DecontaminationPaused && IsDecontaminationClip(entries[i].audio))
                    yield return null;
                if (ShouldSkipDecontaminationAudio(entries[i].audio))
                    continue;
                yield return PlayISBLeadAnnouncement(entries[i].audio, scheduledStart < 0d ? -1d : scheduledStart + (_officeAccess?.pausedSeconds ?? 0d) + NightmareDecontaminationDelay);
                if (!CanContinueAnnouncement(generation))
                    yield break;
                if (i + 1 < entries.Length && entries[i].nextDelay > 0f && DecontaminationAnnouncementTime(entries[i + 1].audio) < 0d)
                    yield return new WaitForSeconds(entries[i].nextDelay);
            }
        }

        private IEnumerator PlayTakeoverPowerRestorationSound()
        {
            if (!IsLaboratoryPresentationAllowed())
                yield break;
            TakeoverPowerRestorationAudioDefinition powerAudio = _takeoverInfrastructure?.powerRestoration;
            if (powerAudio == null || !powerAudio.enabled || string.IsNullOrWhiteSpace(powerAudio.audio))
                yield break;
            string path = Path.Combine(_dataFolder, powerAudio.audio.Trim());
            if (!File.Exists(path))
            {
                yield break;
            }

            AudioClip clip;
            if (!_audioClipCache.TryGetValue(path, out clip) || !IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    _audioClipCache.Remove(path);
                    Destroy(clip);
                }

                AudioClip loadedClip = null;
                yield return StartCoroutine(LoadAudioClip(path, delegate (AudioClip value)
                {
                    loadedClip = value;
                }));
                clip = loadedClip;
            }

            if (!IsAudioClipReady(clip))
            {
                yield break;
            }

            yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), 1f, 150f, powerAudio.volume, null, false, 0f, false, true);
        }

        private IEnumerator PlayLaboratoryBlackoutSound()
        {
            if (!IsLaboratoryPresentationAllowed())
            {
                yield break;
            }

            if (string.IsNullOrEmpty(_blackoutSoundPath) || !File.Exists(_blackoutSoundPath))
            {
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Blackout: Optional sound '" + BlackoutBridge.BlackoutSoundFileName + "' was not found in the audio folder; continuing without it.");
                }

                yield break;
            }

            AudioClip clip;
            if (!_audioClipCache.TryGetValue(_blackoutSoundPath, out clip) || !IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    _audioClipCache.Remove(_blackoutSoundPath);
                    Destroy(clip);
                }

                AudioClip loadedClip = null;
                yield return StartCoroutine(LoadAudioClip(_blackoutSoundPath, delegate (AudioClip value)
                {
                    loadedClip = value;
                }));
                clip = loadedClip;
            }

            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning("ISB SOF Blackout: The blackout sound clip was invalid at playback time.");
                yield break;
            }

            yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), 1f, 150f, 4f, _blackoutVolume, false, 0f, true, true);
        }

        private IEnumerator PlayPreBlackoutAudio(string fileName)
        {
            if (!IsLaboratoryPresentationAllowed() || string.IsNullOrWhiteSpace(fileName))
                yield break;
            string path = Path.Combine(_dataFolder, fileName);
            if (_enablePreBlackoutEvent?.Value != true)
            {
                yield return WaitAnnouncement(Path.GetFileNameWithoutExtension(path), GetWavDurationSeconds(path));
                yield break;
            }
            if (IsHeadless)
            {
                if (!File.Exists(path))
                {
                    yield break;
                }

                string clipName = Path.GetFileNameWithoutExtension(path);
                float clipLength = GetWavDurationSeconds(path);
                BroadcastFikaPreBlackout(clipName, clipLength);
                yield return WaitAnnouncement(clipName, Mathf.Max(clipLength, 0.05f));
                yield break;
            }

            AudioClip clip;
            if (!_audioClipCache.TryGetValue(path, out clip) || !IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    _audioClipCache.Remove(path);
                    Destroy(clip);
                }

                AudioClip loadedClip = null;
                yield return StartCoroutine(LoadAudioClip(path, value => loadedClip = value));
                clip = loadedClip;
            }

            if (!IsAudioClipReady(clip))
            {
                yield break;
            }

            BroadcastFikaPreBlackout(clip.name, clip.length);
            yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), 1f, 350f, 1f, _preBlackoutVolume, false, 0f, true, false, 1f, AudioRolloffMode.Linear);
        }

        private IEnumerator PlayNetworkPreBlackout(string clipName, float startOffset)
        {
            double resolveStarted = NetworkBridge.NetworkTime;
            AudioClip clip = null;
            yield return StartCoroutine(ResolveNetworkClip(clipName, null, value => clip = value));
            startOffset += (float)Math.Max(0d, NetworkBridge.NetworkTime - resolveStarted);
            if (!IsAudioClipReady(clip) || startOffset >= clip.length)
            {
                yield break;
            }

            yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170f, 20f, -342f), 1f, 350f, 1f, _preBlackoutVolume, false, startOffset, false, false, 1f, AudioRolloffMode.Linear);
        }

        private IEnumerator PlayISBLeadAnnouncement(string fileName, double scheduledStart = -1d)
        {
            if (DeadManActive || !IsLaboratoryPresentationAllowed())
                yield break;
            double queuedAt = SynchronizedTime;
            while (_raidisActive && !DeadManActive && NightmarePriorityPending && !IsNightmareAnnouncement(fileName)) yield return null;
            if (!_raidisActive || DeadManActive) yield break;
            if (SynchronizedTime - queuedAt > .01d && scheduledStart >= 0d) scheduledStart = SynchronizedTime;
            string path = Path.Combine(_dataFolder, fileName);
            if (IsHeadless || !_enableAnnouncements.Value)
            {
                if (!File.Exists(path))
                {
                    Log.LogError("ISB: Required event announcement is missing: " + fileName);
                    _currentNetworkAnnouncementStart = SynchronizedTime;
                    NotifyAnnouncementStarted(Path.GetFileNameWithoutExtension(fileName));
                    yield break;
                }

                string clipName = Path.GetFileNameWithoutExtension(path);
                float clipLength = GetWavDurationSeconds(path);
                BroadcastFikaAnnouncement(clipName, clipLength, scheduledStart);
                NotifyAnnouncementStarted(clipName);
                if (IsEventAuthority && _restrictExfilsDuringBlackout.Value && _blackoutApplied && !_gateBClosed && !_takeoverGatesOpened && string.Equals(clipName, "ISBGateB", StringComparison.OrdinalIgnoreCase))
                {
                    StartCoroutine(DisableGateBAtSynchronizedTime(_currentNetworkAnnouncementStart + AnnouncementTriggerDelays.GateBSeconds));
                }

                yield return WaitAnnouncement(clipName, Mathf.Max(clipLength, 0.05f));
                yield break;
            }

            AudioClip clip;
            if (!_audioClipCache.TryGetValue(path, out clip) || !IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    _audioClipCache.Remove(path);
                    Destroy(clip);
                }

                AudioClip loadedClip = null;
                yield return StartCoroutine(LoadAudioClip(path, delegate (AudioClip value)
                {
                    loadedClip = value;
                }));
                clip = loadedClip;
            }

            if (!IsAudioClipReady(clip))
            {
                Log.LogError($"ISB: Required event announcement could not be decoded: {fileName}");
                _currentNetworkAnnouncementStart = SynchronizedTime;
                NotifyAnnouncementStarted(Path.GetFileNameWithoutExtension(fileName));
                yield break;
            }

            if (ShouldSkipDecontaminationAudio(fileName))
                yield break;
            BroadcastFikaAnnouncement(clip.name, clip.length, scheduledStart);
            NotifyAnnouncementStarted(clip.name);
            if (IsEventAuthority && _restrictExfilsDuringBlackout.Value && _blackoutApplied && !_gateBClosed && !_takeoverGatesOpened && string.Equals(clip.name, "ISBGateB", StringComparison.OrdinalIgnoreCase))
            {
                StartCoroutine(DisableGateBAtSynchronizedTime(_currentNetworkAnnouncementStart + AnnouncementTriggerDelays.GateBSeconds));
            }

            float playbackOffset = (float)Math.Max(0d, AnnouncementNetworkClock(clip.name) - _currentNetworkAnnouncementStart);
            if (playbackOffset >= clip.length)
                yield break;
            if (ShouldRouteThroughLaboratoryPa(clip.name))
            {
                yield return RouteBlackoutClipIntoLaboratoryPa(clip, playbackOffset);
                yield break;
            }

            yield return PlayISBSoldiersRadio(clip, null, new Vector3(-170, 20, -342), AnnouncementMinDistance, AnnouncementMaxDistance, AnnouncementBaseVolume, _announcementsVolume, true, playbackOffset, true, false, AnnouncementSpatialBlend, AnnouncementRolloffMode);
        }

        internal static bool IsRussianSpecialAnnouncement(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = Path.GetFileNameWithoutExtension(name);
            return (name.EndsWith("_RU", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Russian", StringComparison.OrdinalIgnoreCase)) &&
                !IsDecontaminationClip(name) && !ShouldRouteThroughLaboratoryPa(name);
        }

        private void NotifyAnnouncementStarted(string clipName)
        {
            if (!IsEventAuthority || !IsLaboratoryPresentationAllowed())
                return;
            ISB.Compatibility.ISBEffectNotifications.BeginAnnouncement(clipName, Math.Max(0f, AnnouncementTriggerDelays.GetDelay(clipName) - (float)Math.Max(0d, AnnouncementNetworkClock(clipName) - _currentNetworkAnnouncementStart)), Log);
            if (AnnouncementTriggerDelays.IsCameraTracking(clipName))
                ISB.Compatibility.ISBEffectNotifications.Publish("camera_tracking", Log);
            ScheduleLabsChaosSpawn(clipName);
            TriggerDecontamination(clipName);
            if (_cameraHuntRoutine == null && AnnouncementTriggerDelays.IsCameraHunt(clipName))
                _cameraHuntRoutine = StartCoroutine(ApplyCameraHuntAfterAudio(_activeRaidWorld, clipName, _currentNetworkAnnouncementStart + AnnouncementTriggerDelays.GetDelay(clipName)));
        }

        private IEnumerator ApplyCameraHuntAfterAudio(GameWorld world, string clipName, double fireAt)
        {
            while (_raidisActive && world != null && ReferenceEquals(world, _activeRaidWorld) && SynchronizedTime < fireAt)
                yield return null;
            _cameraHuntRoutine = null;
            if (!_raidisActive || !IsEventAuthority || !IsLaboratoryPresentationAllowed() || world == null || !ReferenceEquals(world, _activeRaidWorld) || !ReferenceEquals(world, Singleton<GameWorld>.Instance))
                yield break;
            ApplyAnnouncementEffect(clipName);
        }

        private void ApplyAnnouncementEffect(string clipName)
        {
            if (_announcementEffectFailed)
                return;
            try
            {
                if (!_announcementEffectResolved)
                {
                    _announcementEffectResolved = true;
                    if (!Chainloader.PluginInfos.TryGetValue("com.samc137.isbspecialforces", out var plugin) || plugin?.Instance == null)
                        return;
                    var type = plugin.Instance.GetType().Assembly.GetType("ISBSpecialForces.Components.ISBAnnouncementEvents", false);
                    var method = type?.GetMethod("OnAudioStarted", BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                        throw new MissingMethodException("The ISB client plugin does not support announcement events. Update both ISB DLLs.");
                    _announcementEffectHandler = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), method);
                }

                _announcementEffectHandler?.Invoke(clipName);
            }
            catch (Exception ex)
            {
                _announcementEffectFailed = true;
                Log.LogError("ISB SOF: Announcement effect unavailable: " + ex.GetBaseException().Message);
            }
        }
    }
}
