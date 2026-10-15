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
        private TakeoverAlarmAudio _takeoverAlarmAudio;
        private int _takeoverAlarmChoice = -1;
        private void ChooseTakeoverAlarm()
        {
            if (!TakeoverAlarmChoice.Valid(_takeoverAlarmChoice) && IsEventAuthority)
                _takeoverAlarmChoice = UnityEngine.Random.Range(0, TakeoverAlarmChoice.Count);
        }

        private void ReceiveTakeoverAlarmChoice(int choice)
        {
            if (!TakeoverAlarmChoice.Valid(choice) || choice == _takeoverAlarmChoice)
                return;
            StopTakeoverAlarmLocal();
            _takeoverAlarmChoice = choice;
        }

        private void StartTakeoverAlarmLocal()
        {
            if (DeadManActive || !CanPresentLocally || !_raidisActive || !_labsPreBlackoutEventActive || (!_takeoverAlarmStarted && !NightmareEmergencyMode) || _takeoverInfrastructure?.alarm == null || !_takeoverInfrastructure.alarm.enabled)
            {
                return;
            }

            if (_takeoverAlarmSource != null)
            {
                _takeoverAlarmSource.loop = true;
                _takeoverAlarmSource.mute = _takeoverAlarmDisabled || NightmareMuteAlarm;
                if (_takeoverAlarmSource.clip != null && !_takeoverAlarmSource.isPlaying)
                    _takeoverAlarmSource.Play();
                return;
            }

            if (_takeoverAlarmRoutine != null)
                return;
            ChooseTakeoverAlarm();
            if (!TakeoverAlarmChoice.Valid(_takeoverAlarmChoice))
                return;
            int generation = ++_takeoverAlarmGeneration;
            _takeoverAlarmRoutine = StartCoroutine(StartTakeoverAlarmRoutine(generation));
        }

        private IEnumerator StartTakeoverAlarmRoutine(int generation)
        {
            yield return null;
            TakeoverAlarmAudio audio = null;
            bool started = false;
            try
            {
                TakeoverAlarmDefinition alarm = _takeoverInfrastructure?.alarm;
                if (generation != _takeoverAlarmGeneration || alarm == null || string.IsNullOrWhiteSpace(alarm.audio))
                    yield break;
                audio = new TakeoverAlarmAudio();
                _takeoverAlarmAudio?.Dispose();
                _takeoverAlarmAudio = audio;
                string file = TakeoverAlarmChoice.File(_takeoverAlarmChoice, alarm.audio);
                string path = Path.Combine(_dataFolder, file);
                if (!File.Exists(path))
                    path = Path.Combine(_dataFolder, TakeoverAlarmChoice.File(0, alarm.audio));
                yield return audio.Load(path);
                AudioClip clip = audio.Clip;
                if (DeadManActive || generation != _takeoverAlarmGeneration || !IsAudioClipReady(clip) || !_raidisActive || !_labsPreBlackoutEventActive)
                    yield break;
                DestroyStrayTakeoverAlarmObjects();
                _takeoverAlarmObject = new GameObject(TakeoverAlarmObjectName);
                _takeoverAlarmSource = _takeoverAlarmObject.AddComponent<AudioSource>();
                _takeoverAlarmSource.clip = clip;
                _takeoverAlarmSource.loop = true;
                _takeoverAlarmSource.mute = _takeoverAlarmDisabled || NightmareMuteAlarm;
                _takeoverAlarmSource.playOnAwake = false;
                _takeoverAlarmSource.spatialBlend = 0f;
                _takeoverAlarmSource.volume = Mathf.Clamp01(alarm.volume / 100f) * GetVolumeFactor(_alarmVolume);
                _takeoverAlarmSource.dopplerLevel = 0f;
                _takeoverAlarmSource.Play();
                started = true;
            }
            finally
            {
                if (!started)
                {
                    if (ReferenceEquals(_takeoverAlarmAudio, audio))
                        _takeoverAlarmAudio = null;
                    audio?.Dispose();
                }

                if (generation == _takeoverAlarmGeneration)
                    _takeoverAlarmRoutine = null;
            }
        }

        private void MaintainTakeoverAlarmLoopLocal()
        {
            if (Time.realtimeSinceStartup < _nextTakeoverAlarmHealthCheck)
                return;
            _nextTakeoverAlarmHealthCheck = Time.realtimeSinceStartup + 1f;
            if (DeadManActive || !CanPresentLocally || !_raidisActive || !_labsPreBlackoutEventActive || (!_takeoverAlarmStarted && !NightmareEmergencyMode))
            {
                return;
            }

            if (_takeoverAlarmSource == null)
            {
                StartTakeoverAlarmLocal();
                return;
            }

            _takeoverAlarmSource.loop = true;
            _takeoverAlarmSource.mute = _takeoverAlarmDisabled || NightmareMuteAlarm;
            if (_takeoverAlarmSource.clip != null && !_takeoverAlarmSource.isPlaying)
            {
                _takeoverAlarmSource.Play();
            }
        }

        private void DestroyStrayTakeoverAlarmObjects()
        {
            AudioSource[] sources = Resources.FindObjectsOfTypeAll<AudioSource>();
            foreach (AudioSource source in sources)
            {
                if (source == null || source.gameObject == null)
                    continue;
                if (!string.Equals(source.gameObject.name, TakeoverAlarmObjectName, StringComparison.Ordinal))
                    continue;
                if (_takeoverAlarmSource != null && ReferenceEquals(source, _takeoverAlarmSource))
                    continue;
                source.Stop();
                Destroy(source.gameObject);
            }
        }

        private void StopTakeoverAlarmLocal()
        {
            _takeoverAlarmGeneration++;
            if (_takeoverAlarmRoutine != null)
            {
                StopCoroutine(_takeoverAlarmRoutine);
                _takeoverAlarmRoutine = null;
            }

            if (_takeoverAlarmSource != null)
                _takeoverAlarmSource.Stop();
            if (_takeoverAlarmObject != null)
                Destroy(_takeoverAlarmObject);
            _takeoverAlarmObject = null;
            _takeoverAlarmSource = null;
            _takeoverAlarmAudio?.Dispose();
            _takeoverAlarmAudio = null;
            _nextTakeoverAlarmHealthCheck = 0f;
            DestroyStrayTakeoverAlarmObjects();
        }

        private void SetTakeoverSceneryOptions(bool disableFire, bool disableSirens, bool disableDestroyedVehicles)
        {
            if (_disableTakeoverFire == disableFire && _disableTakeoverSirens == disableSirens && _disableTakeoverDestroyedVehicles == disableDestroyedVehicles)
                return;
            _takeoverFire.Dispose();
            _takeoverSirens.Dispose();
            _takeoverSceneryWorld = null;
            _disableTakeoverFire = disableFire;
            _disableTakeoverSirens = disableSirens;
            _disableTakeoverDestroyedVehicles = disableDestroyedVehicles;
        }

        private void PrepareTakeoverScenery(GameWorld gameWorld)
        {
            if (gameWorld == null)
                return;
            TakeoverFlameTrigger.EventActive = _labsPreBlackoutEventActive;
            if (!ReferenceEquals(_takeoverSceneryWorld, gameWorld))
            {
                _takeoverFire.Load(_takeoverFirePath, Log, CanPresentLocally, _disableTakeoverFire, _disableTakeoverDestroyedVehicles);
                if (!_disableTakeoverSirens)
                    _takeoverSirens.Load(_takeoverSirensPath, Log);
                _scenerySettings?.ApplyPresentation(_takeoverFire, _takeoverSirens);
                _takeoverSceneryWorld = gameWorld;
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF scenery: Loaded raid assets. event=" + _labsPreBlackoutEventActive + ", authority=" + IsEventAuthority + ", presentation=" + CanPresentLocally + ", fire=" + !_disableTakeoverFire + ", sirens=" + !_disableTakeoverSirens + ", vehicles=" + !_disableTakeoverDestroyedVehicles + ".");
                }
            }

            if (!_disableTakeoverSirens)
                _takeoverSirens.Prepare(CanPresentLocally);
            _takeoverFire.SetActive(true, CanPresentLocally);
        }

        private void SyncTakeoverFireLocal()
        {
            TakeoverFlameTrigger.EventActive = _raidisActive && IsLaboratoryPresentationAllowed() && _labsPreBlackoutEventActive;
            if (_raidisActive && IsLaboratoryPresentationAllowed() && _labsPreBlackoutEventActive)
                PrepareTakeoverScenery(_activeRaidWorld);
            _takeoverFire.SetActive(TakeoverFire.ShouldActivate(_raidisActive, IsLaboratoryPresentationAllowed(), _labsPreBlackoutEventActive), CanPresentLocally);
            SyncTakeoverSirensLocal();
        }

        private void SyncTakeoverSirensLocal()
        {
            _takeoverSirens.SetWarningColor(NightmareWarningLights);
            _takeoverSirens.SetActive(!_disableTakeoverSirens && !NightmareBlackout && TakeoverSirens.ShouldActivate(_raidisActive, IsLaboratoryPresentationAllowed(), CanPresentLocally, _labsPreBlackoutEventActive, _takeoverEmergencyLightsStarted || NightmareEmergencyMode, _takeoverEmergencyLightsDisabled));
        }
    }
}
