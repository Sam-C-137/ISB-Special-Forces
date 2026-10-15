using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private string _nightmareEntity;
        private Coroutine _nightmareGasLoad;
        private AudioClip _nightmareGasClip;
        private AudioSource _nightmareGasSource;
        private bool _nightmareGasPrepared, _nightmareGasStarted;
        private NightmareContainmentRoom _nightmareRoom;
        private DecontaminationScreens _nightmareScreens;
        private Coroutine _nightmareScreenScan, _nightmareAnnouncement, _nightmareLightPreparation;
        private bool _nightmareLightsPrepared, _nightmareWarningLightsPrevious;
        private bool _nightmareRoomFailed, _nightmareVideoFailed, _nightmareBlackoutPrevious;
        private float _nightmareRoomRetryAt;
        private float _nightmareRoomFirstTry;
        private double NightmareClock => _scp173?.Clock ?? 0d;
        private NightmareFacilityState NightmareState => _scp173?.Facility;
        private bool NightmareAnnouncementActive => NightmareState != null && NightmareState.announcementStart >= 0d && NightmareClock < NightmareState.announcementEnd;
        private bool NightmareWarningActive => NightmareAnnouncementActive && NightmareState.announcement == "SCPWarn";
        private bool NightmareWarningLights => NightmareState != null && (NightmareWarningActive || NightmareState.warningPreludeUntil > NightmareClock);
        private bool NightmareBlackout => NightmareState != null && NightmareState.awakeAt >= 0d && NightmareClock < NightmareState.awakeAt + (_scp173.Settings?.activationBlackoutSeconds ?? 2f);
        private bool NightmareMuteAlarm => NightmareBlackout || NightmareWarningActive;
        private bool NightmareEmergencyMode => NightmareState != null && NightmareState.awakeAt >= 0d && !_takeoverFacilityRestored;
        private bool NightmarePriorityPending => NightmareState != null && (!NightmareState.warningCompleted && NightmareState.warningDue >= 0d && NightmareClock >= NightmareState.warningDue || !NightmareState.removalAnnounced && !string.IsNullOrEmpty(NightmareState.removal) || NightmareAnnouncementActive || _nightmareAnnouncement != null);
        private double NightmareDecontaminationDelay => NightmareState == null ? 0d : NightmareState.decontaminationDelay + (!_scp173.Authority && _officeAccess?.aborted != true && (NightmareState.warningSequenceActive || NightmareState.announcementStart >= 0d) && NightmareState.delayUpdatedAt >= 0d ? Math.Max(0d, (NightmareState.warningSequenceActive ? NightmareClock : Math.Min(NightmareClock, NightmareState.announcementEnd)) - NightmareState.delayUpdatedAt) : 0d);
        private static bool IsNightmareAnnouncement(string file) => Path.GetFileNameWithoutExtension(file) == "SCPWarn" || Path.GetFileNameWithoutExtension(file) == "SCPTerminatePlayer" || Path.GetFileNameWithoutExtension(file) == "SCPContainedBots";

        private void TickNightmareFacility()
        {
            if (_scp173.World == null || !_scp173.ContainmentRaid || _scp173.Settings == null)
            {
                if (_nightmareEntity != null) ResetNightmareFacility();
                return;
            }
            if (_nightmareEntity != _scp173.Entity)
            {
                ResetNightmareFacility();
                _nightmareEntity = _scp173.Entity;
            }
            bool present = CanPresentLocally;
            AccumulateNightmareDecontaminationPause();
            if (present && _scp173.RaidStarted && !_nightmareLightsPrepared && _nightmareLightPreparation == null)
                _nightmareLightPreparation = StartCoroutine(PrepareNightmareLights());
            try
            {
                if (_nightmareRoom == null && !_nightmareRoomFailed && _scp173.RaidStarted && Time.realtimeSinceStartup >= _nightmareRoomRetryAt)
                {
                    if (_nightmareRoomFirstTry == 0f) _nightmareRoomFirstTry = Time.realtimeSinceStartup;
                    _nightmareRoomRetryAt = Time.realtimeSinceStartup + 1f;
                    _nightmareRoom = new NightmareContainmentRoom();
                    _nightmareRoom.Prepare(_scp173, present);
                }
                if (present) _nightmareRoom?.PreparePresentation();
                _nightmareRoom?.UpdateButton();
                if (NightmareState.containmentAt >= 0d)
                    _nightmareRoom?.Tick(NightmareClock - NightmareState.containmentAt, _scp173.Settings.containmentSeconds, present);
            }
            catch (Exception ex)
            {
                _nightmareRoom?.Dispose(); _nightmareRoom = null;
                if (Time.realtimeSinceStartup - _nightmareRoomFirstTry > 30f)
                {
                    _nightmareRoomFailed = true;
                    Log.LogError("ISB Nightmare containment unavailable: " + ex.GetBaseException().Message);
                }
            }
            if (_nightmareBlackoutPrevious != NightmareBlackout)
            {
                _nightmareBlackoutPrevious = NightmareBlackout;
                if (CanPresentLocally && NightmareBlackout)
                {
                    StartCoroutine(PlayLaboratoryBlackoutSound());
                    LabsBlackout.DisableAutomaticLights();
                    LabsBlackout.DisableAmbientLight();
                    LabsBlackout.DisableElectricSounds();
                    LabsBlackout.DisableLights();
                    LabsBlackout.ApplyPreparedMaterials();
                }
                else if (CanPresentLocally && !NightmareEmergencyMode && (!_blackoutApplied || _takeoverFacilityRestored))
                    LabsBlackout.RestoreFacilityPower();
                SyncBlackoutSafetyLocal();
                SyncTakeoverSirensLocal();
                StartTakeoverAlarmLocal();
            }
            if (_takeoverAlarmSource != null) _takeoverAlarmSource.mute = _takeoverAlarmDisabled || NightmareMuteAlarm;
            if (_nightmareWarningLightsPrevious != NightmareWarningLights)
            {
                _nightmareWarningLightsPrevious = NightmareWarningLights;
                _emergencyLighting.SetWarningColor(NightmareWarningLights);
                _takeoverSirens.SetWarningColor(NightmareWarningLights);
                SyncBlackoutSafetyLocal();
            }
            TickNightmareContainmentAudio();
            TickNightmareVideo();
            if (!_scp173.Authority || !_raidisActive || _nightmareAnnouncement != null || DeadManActive) return;
            if (NightmarePriorityPending && !CurrentAnnouncementBusy())
            {
                string name = !NightmareState.warningCompleted && NightmareState.warningDue >= 0d && NightmareClock >= NightmareState.warningDue ? "SCPWarn" : NightmareState.removal == "player" ? "SCPTerminatePlayer" : "SCPContainedBots";
                _nightmareAnnouncement = StartCoroutine(PlayNightmareAnnouncement(name));
            }
        }

        private IEnumerator LoadNightmareContainmentAudio()
        {
            yield return null;
            string path = Path.Combine(_dataFolder, _scp173.Settings.containmentGasAudio);
            if (!string.IsNullOrEmpty(_scp173.Settings.containmentGasAudio))
            {
                yield return LoadAudioClip(path, clip => _nightmareGasClip = clip);
                if (!IsAudioClipReady(_nightmareGasClip)) Log.LogError("ISB Nightmare containment gas audio could not be loaded.");
            }
            _nightmareGasLoad = null;
        }

        private void TickNightmareContainmentAudio()
        {
            if (!CanPresentLocally) return;
            if (!_nightmareGasPrepared && _scp173.RaidStarted)
            {
                _nightmareGasPrepared = true;
                _nightmareGasLoad = StartCoroutine(LoadNightmareContainmentAudio());
            }
            if (IsAudioClipReady(_nightmareGasClip)) _audioLastUsed[_nightmareGasClip] = Time.realtimeSinceStartup;
            var state = NightmareState;
            if (state.containmentAt < 0d || !IsAudioClipReady(_nightmareGasClip)) return;
            float elapsed = (float)Math.Max(0d, NightmareClock - state.containmentAt);
            if (!_nightmareGasStarted)
            {
                _nightmareGasStarted = true;
                if (elapsed >= _nightmareGasClip.length) return;
                var sound = new GameObject("ISB_NightmareContainment_GasAudio");
                sound.transform.position = new Vector3(-130.75f, 1.85f, -357.515f);
                _nightmareGasSource = sound.AddComponent<AudioSource>();
                _nightmareGasSource.playOnAwake = false;
                _nightmareGasSource.clip = _nightmareGasClip;
                _nightmareGasSource.spatialBlend = 1f;
                _nightmareGasSource.minDistance = 4f;
                _nightmareGasSource.maxDistance = 35f;
                _nightmareGasSource.rolloffMode = AudioRolloffMode.Linear;
                _nightmareGasSource.dopplerLevel = 0f;
                _nightmareGasSource.volume = 0f;
                _nightmareGasSource.time = elapsed;
                _nightmareGasSource.Play();
            }
            if (_nightmareGasSource == null) return;
            var settings = _scp173.Settings;
            float fadeIn = settings.containmentGasFadeInSeconds <= 0f ? 1f : elapsed / settings.containmentGasFadeInSeconds;
            float fadeOut = settings.containmentGasFadeOutSeconds <= 0f ? 1f : (_nightmareGasClip.length - elapsed) / settings.containmentGasFadeOutSeconds;
            _nightmareGasSource.volume = settings.containmentGasVolume * Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut));
            if (elapsed >= _nightmareGasClip.length)
            {
                _nightmareGasSource.Stop();
                Destroy(_nightmareGasSource.gameObject);
                _nightmareGasSource = null;
            }
        }

        private IEnumerator PrepareNightmareLights()
        {
            yield return null;
            try
            {
                yield return _emergencyLighting.Prepare();
                yield return LabsBlackout.PrepareMaterials();
                _nightmareLightsPrepared = true;
                SyncBlackoutSafetyLocal();
            }
            finally { _nightmareLightPreparation = null; }
        }

        private bool CurrentAnnouncementBusy()
        {
            if (!string.IsNullOrEmpty(_currentNetworkAnnouncement))
            {
                double start = IsDecontaminationClip(_currentNetworkAnnouncement) && _deconAnnouncementStart >= 0d ? _deconAnnouncementStart : _currentNetworkAnnouncementStart;
                if (AnnouncementClock(_currentNetworkAnnouncement) < start + _currentNetworkAnnouncementLength + .2d) return true;
            }
            foreach (var cue in _facilitySequence.cues)
                if (SynchronizedTime >= cue.start && SynchronizedTime < cue.start + cue.duration + .2d) return true;
            return _facilityAudioLoad != null || _facilityAudio?.isPlaying == true;
        }

        private IEnumerator PlayNightmareAnnouncement(string name)
        {
            yield return null;
            var state = NightmareState;
            try
            {
                if (name == "SCPWarn")
                {
                    state.warningSequenceActive = true;
                    state.delayUpdatedAt = NightmareClock;
                    for (int pass = 0; pass < 2 && _raidisActive && !DeadManActive; pass++)
                    {
                        state.warningPreludeUntil = NightmareClock + 2d;
                        DelayNightmareFacilityCues(SynchronizedTime + 2.3d);
                        while (_raidisActive && !DeadManActive && NightmareClock < state.warningPreludeUntil) yield return null;
                        state.warningPreludeUntil = -1d;
                        if (!_raidisActive || DeadManActive) break;
                        yield return PlayISBLeadAnnouncement(name + ".wav");
                        AccumulateNightmareDecontaminationPause();
                        state.announcementStart = state.announcementEnd = -1d;
                        state.announcement = "";
                        StopNightmareVideo();
                        double pause = pass == 0 ? 8d : 15d;
                        state.warningHoldUntil = NightmareClock + pause;
                        DelayNightmareFacilityCues(SynchronizedTime + pause + .3d);
                        while (_raidisActive && !DeadManActive && NightmareClock < state.warningHoldUntil) yield return null;
                        state.warningHoldUntil = -1d;
                    }
                }
                else yield return PlayISBLeadAnnouncement(name + ".wav");
            }
            finally
            {
                if (ReferenceEquals(state, NightmareState))
                {
                    AccumulateNightmareDecontaminationPause();
                    state.announcementStart = state.announcementEnd = -1d;
                    state.delayUpdatedAt = -1d;
                    state.announcement = "";
                    if (name == "SCPWarn")
                    {
                        state.warningCompleted = true;
                        state.warningSequenceActive = false;
                        state.warningPreludeUntil = state.warningHoldUntil = -1d;
                    }
                    else state.removalAnnounced = true;
                }
                StopNightmareVideo();
                _nightmareAnnouncement = null;
            }
        }

        private void NightmareAnnouncementStarted(string name, float length)
        {
            if (!IsNightmareAnnouncement(name) || !_scp173.Authority) return;
            var state = NightmareState;
            state.announcement = Path.GetFileNameWithoutExtension(name);
            state.announcementStart = NightmareClock;
            state.announcementEnd = NightmareClock + length + .2d;
            state.delayUpdatedAt = NightmareClock;
            DelayNightmareFacilityCues(SynchronizedTime + length + .3d);
        }

        private void AccumulateNightmareDecontaminationPause()
        {
            var state = NightmareState;
            if (state == null || !_scp173.Authority || (!state.warningSequenceActive && state.announcementStart < 0d) || state.delayUpdatedAt < 0d) return;
            double now = state.warningSequenceActive ? NightmareClock : Math.Min(NightmareClock, state.announcementEnd);
            if (_officeAccess?.aborted != true) state.decontaminationDelay += Math.Max(0d, now - state.delayUpdatedAt);
            state.delayUpdatedAt = now;
        }

        private void DelayNightmareFacilityCues(double until)
        {
            double delta = 0d;
            foreach (var cue in _facilitySequence.cues)
                if (cue.start >= SynchronizedTime && cue.start < until) delta = Math.Max(delta, until - cue.start);
            if (delta <= 0d) return;
            foreach (var cue in _facilitySequence.cues) if (cue.start >= SynchronizedTime) cue.start += delta;
            if (_facilitySequence.countdownFrom >= SynchronizedTime) _facilitySequence.countdownFrom += delta;
            if (_facilitySequence.restoreAt >= SynchronizedTime) _facilitySequence.restoreAt += delta;
            BroadcastFacilitySequence();
        }

        private void TickNightmareVideo()
        {
            if (!NightmareWarningActive || !CanPresentLocally || !_raidisActive || DeadManActive)
            {
                if (_nightmareScreens != null) StopNightmareVideo();
                return;
            }
            if (_nightmareVideoFailed) return;
            try
            {
                if (_nightmareScreens == null)
                {
                    StopDecontaminationScreenPlayback();
                    StopOfficeMonitorBinding();
                    StopRoutine(ref _powerMonitorScan);
                    _powerMonitor?.Dispose(); _powerMonitor = null;
                    _emergencyLighting.SetScreensSuppressed(true);
                    string path = Path.Combine(Path.GetDirectoryName(Info.Location), "ISBSpecialForces_Videos", "SCPWarn.mp4");
                    if (!File.Exists(path)) throw new FileNotFoundException("Nightmare warning video is missing.", path);
                    _nightmareScreens = new DecontaminationScreens(path, Log, r => !OfficeScreenCanvas.IsTerminalScreen(r));
                    _nightmareScreenScan = StartCoroutine(_nightmareScreens.FindScreens());
                }
                _nightmareScreens.Tick(NightmareClock - NightmareState.announcementStart, Time.realtimeSinceStartup);
            }
            catch (Exception ex)
            {
                _nightmareVideoFailed = true; StopNightmareVideo();
                Log.LogError("ISB Nightmare warning video unavailable: " + ex.GetBaseException().Message);
            }
        }

        private void StopNightmareVideo()
        {
            StopRoutine(ref _nightmareScreenScan);
            _nightmareScreens?.Dispose(); _nightmareScreens = null;
            _emergencyLighting.SetScreensSuppressed(false);
        }

        private void ResetNightmareFacility()
        {
            StopRoutine(ref _nightmareGasLoad);
            if (_nightmareGasSource != null) { _nightmareGasSource.Stop(); Destroy(_nightmareGasSource.gameObject); }
            _nightmareGasSource = null; _nightmareGasClip = null;
            _nightmareGasPrepared = _nightmareGasStarted = false;
            StopRoutine(ref _nightmareAnnouncement);
            StopRoutine(ref _nightmareLightPreparation);
            StopNightmareVideo();
            _nightmareRoom?.Dispose(); _nightmareRoom = null;
            _nightmareEntity = null; _nightmareRoomFailed = _nightmareVideoFailed = _nightmareBlackoutPrevious = false;
            _nightmareRoomRetryAt = _nightmareRoomFirstTry = 0f;
            _nightmareLightsPrepared = _nightmareWarningLightsPrevious = false;
            _emergencyLighting.SetWarningColor(false);
            _takeoverSirens.SetWarningColor(false);
        }
    }
}
