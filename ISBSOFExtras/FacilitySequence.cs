using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private FacilitySequenceState _facilitySequence = new FacilitySequenceState();
        private AudioSource _facilityAudio;
        private BepInEx.Configuration.ConfigEntry<float> _facilityVolume;
        private Coroutine _facilityAudioLoad;
        private string _facilityAudioKey = "";
        private bool _facilityAudioReported;
        private float _facilityAudioClockAt;
        private bool DeadManActive => _officeAccess?.DestructionActive == true;

        private void QueueGeneratorAnnouncement()
        {
            if (!IsEventAuthority || !_labsPreBlackoutEventActive || _facilitySequence.FreeToggle || DeadManActive)
                return;
            int count = (_takeoverPowerA ? 1 : 0) + (_takeoverPowerB ? 1 : 0) + (_takeoverPowerC ? 1 : 0) + (_takeoverPowerD ? 1 : 0) + (_facilitySequence.masterStarted ? 1 : 0);
            _facilitySequence.cues.RemoveAll(cue => cue.start + cue.duration < SynchronizedTime);
            var settings = _takeoverInfrastructure.powerRestoration;
            if (count < 1 || count > 5)
                return;
            string[] defaults =
            {
                "OneGenerator.wav",
                "TwoGenerators.wav",
                "ThreeGenerators.wav",
                "FourGenerators.wav",
                "FiveGenerators.wav"
            };
            string name = settings?.generatorAudios?.Length == 5 ? settings.generatorAudios[count - 1] : defaults[count - 1];
            double start = Math.Max(SynchronizedTime + .5d, _currentNetworkAnnouncementStart + _currentNetworkAnnouncementLength + .1d);
            foreach (var cue in _facilitySequence.cues)
                start = Math.Max(start, cue.start + cue.duration + .15d);
            var added = CreateFacilityCue(name, start);
            if (DiagnosticsEnabled)
                Log.LogInfo("ISB: power cell " + count + "/5; announcement=" + name + ", start=" + start.ToString("F2", CultureInfo.InvariantCulture));
            _facilitySequence.cues.Add(added);
            if (count == 5)
            {
                double wait = settings != null ? Math.Max(1f, Math.Min(600f, settings.countdownSeconds)) : 60d;
                double countdown = settings != null ? Math.Max(0f, Math.Min(300f, settings.countdownAt)) : 19.7d;
                _facilitySequence.countdownFrom = start + countdown;
                _facilitySequence.restoreAt = FacilityPowerTiming.RestoreAt(start, added.duration, (float)countdown, (float)wait);
            }
        }

        private FacilityAudioCue CreateFacilityCue(string name, double start)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name)
                throw new InvalidOperationException("Invalid facility audio filename.");
            if (NightmareAnnouncementActive) start = Math.Max(start, SynchronizedTime + NightmareState.announcementEnd-NightmareClock+.2d);
            float duration = GetWavDurationSeconds(Path.Combine(_dataFolder, name));
            if (duration <= 0f)
                throw new InvalidOperationException("Facility audio is missing or unreadable: " + name);
            return new FacilityAudioCue
            {
                audio = name,
                start = start,
                duration = duration
            };
        }

        private void TickFacilitySequence()
        {
            if (!_raidisActive || !IsLaboratoryPresentationAllowed() || !_labsPreBlackoutEventActive)
                return;
            TickRemoteCell();
            if (IsEventAuthority && !DeadManActive && _facilitySequence.restoreAt >= 0d && SynchronizedTime >= _facilitySequence.restoreAt && !_takeoverFacilityRestored)
            {
                _facilitySequence.restoreAt = -1d;
                CompleteFacilityRestoration();
                ApplyFacilityPanelStateLocal();
                BroadcastTakeoverState();
                BroadcastFacilityControlState();
                BroadcastFacilitySequence();
            }

            if (DeadManActive || !CanPresentLocally)
                return;
            FacilityAudioCue active = null;
            foreach (var cue in _facilitySequence.cues)
                if (SynchronizedTime >= cue.start - 1d && SynchronizedTime < cue.start + cue.duration)
                {
                    active = cue;
                    break;
                }

            if (active != null)
                EnsureFacilityAudio(active.audio, active.start);
            else if (_facilityAudio != null && !_facilityAudio.isPlaying && _facilityAudioLoad == null)
                StopFacilityAudio();
        }

        private void EnsureFacilityAudio(string name, double start)
        {
            if (!CanPresentLocally)
                return;
            string key = name + "|" + start.ToString("R", CultureInfo.InvariantCulture);
            if (_facilityAudioKey != key)
            {
                StopFacilityAudio();
                _facilityAudioKey = key;
                _facilityAudioLoad = StartCoroutine(LoadFacilityAudio(name, start, key));
            }

            if (_facilityAudio?.isPlaying == true && Time.realtimeSinceStartup >= _facilityAudioClockAt)
            {
                _facilityAudioClockAt = Time.realtimeSinceStartup + 2f;
                float target = (float)(SynchronizedTime - start);
                if (target >= 0f && target < _facilityAudio.clip.length && Math.Abs(_facilityAudio.time - target) > .5f)
                    _facilityAudio.time = target;
            }
        }

        private IEnumerator LoadFacilityAudio(string name, double start, string key)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name)
                yield break;
            string path = Path.Combine(_dataFolder, name);
            AudioClip clip = null;
            if (!_audioClipCache.TryGetValue(path, out clip) || !IsAudioClipReady(clip))
                yield return LoadAudioClip(path, value => clip = value);
            if (!_raidisActive || _facilityAudioKey != key)
                yield break;
            if (!IsAudioClipReady(clip))
            {
                if (!_facilityAudioReported)
                    Log.LogError("ISB: facility audio could not be loaded: " + name);
                _facilityAudioReported = true;
                _facilityAudioLoad = null;
                yield break;
            }

            while (_raidisActive && _facilityAudioKey == key && SynchronizedTime < start)
                yield return null;
            float offset = (float)Math.Max(0d, SynchronizedTime - start);
            if (!_raidisActive || _facilityAudioKey != key || offset >= clip.length)
            {
                _facilityAudioLoad = null;
                yield break;
            }

            _facilityAudio = new GameObject("ISB_FacilityAnnouncement").AddComponent<AudioSource>();
            _facilityAudio.clip = clip;
            _facilityAudio.transform.position = new Vector3(-170f, 20f, -342f);
            _facilityAudio.spatialBlend = DeadManActive && name == _officeAccess.config.deadManSwitch.endAudio ? 0f : AnnouncementSpatialBlend;
            _facilityAudio.minDistance = AnnouncementMinDistance;
            _facilityAudio.maxDistance = AnnouncementMaxDistance;
            _facilityAudio.rolloffMode = AnnouncementRolloffMode;
            _facilityAudio.dopplerLevel = 0f;
            _facilityAudio.spread = 55f;
            _facilityAudio.priority = 48;
            _facilityVolume = name == _officeAccess?.config?.deadManSwitch?.audio ? _deadManSwitchVolume : _announcementsVolume;
            _facilityAudio.volume = AnnouncementBaseVolume * GetVolumeFactor(_facilityVolume);
            _facilityAudio.time = offset;
            _facilityAudio.mute = AnnouncementMuted(name);
            AnnouncementFade.Begin(_facilityAudio, clip, AnnouncementBaseVolume * GetVolumeFactor(_facilityVolume), offset);
            _facilityAudio.Play();
            _subtitleSystem?.Play(_facilityAudio, Path.GetFileNameWithoutExtension(name));
            _facilityAudioLoad = null;
        }

        private void StopFacilityAudio()
        {
            StopRoutine(ref _facilityAudioLoad);
            if (_facilityAudio != null)
            {
                _facilityAudio.Stop();
                Destroy(_facilityAudio.gameObject);
            }

            _facilityAudio = null;
            _facilityVolume = null;
            _facilityAudioKey = "";
        }

        private void BroadcastFacilitySequence()
        {
            if (!IsEventAuthority)
                return;
            _facilitySequence.revision++;
            if (IsFikaServer)
                NetworkBridge.Broadcast("FS|" + GetFacilitySequenceSnapshot());
        }

        private string GetFacilitySequenceSnapshot() => EncodeNetworkString(JsonConvert.SerializeObject(_facilitySequence));
        private void ReceiveFacilitySequence(string value)
        {
            if (IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed() || string.IsNullOrEmpty(value) || value.Length > 16384)
                return;
            try
            {
                var state = JsonConvert.DeserializeObject<FacilitySequenceState>(DecodeNetworkString(value));
                if (state == null || !state.Valid || state.revision < _facilitySequence.revision)
                    return;
                _facilitySequence = state;
                ApplyFacilityPanelStateLocal();
            }
            catch (JsonException)
            {
            }
            catch (FormatException)
            {
            }
        }

        private void ResetFacilitySequences()
        {
            StopFacilityAudio();
            _facilitySequence = new FacilitySequenceState();
            _facilityAudioReported = false;
        }
    }
}
