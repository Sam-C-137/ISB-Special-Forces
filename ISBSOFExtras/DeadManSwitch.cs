using System;
using System.Collections;
using System.IO;
using System.Linq;
using EFT;
using UnityEngine;
using UnityEngine.UI;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private OfficeScreenCanvas _officeCanvas;
        private DecontaminationScreens _officeMonitor, _terminalMonitor, _powerMonitor;
        private OfficeComputerBoot _computerBoot;
        private bool _computerReady;
        private Coroutine _terminalMonitorScan, _powerMonitorScan;
        private Coroutine _officeMonitorScan;
        private Texture2D _deadManImage;
        private string _officeMonitorMode = "";
        private float _monitorTickAt, _monitorRetryAt, _gateRetryAt;
        private bool _deadManBegan, _deadManGates, _deadManKilled, _deadManFx;
        private bool _deadManErrorReported;
        private bool _localFacilityRaidEnded;
        private FacilityDestructionEffects _destructionEffects;
        private GameObject _endingOverlay;
        private Image _endingImage;
        private float _deadManBotsAt;
        private EFT.HealthSystem.ActiveHealthController _endingHealth;
        private float _endingDamageCoeff;
        private void ProtectEndingPlayer()
        {
            var health = _activeRaidWorld?.MainPlayer?.ActiveHealthController;
            if (_localFacilityRaidEnded || health == null || !health.IsAlive || _endingHealth != null)
                return;
            _endingHealth = health;
            _endingDamageCoeff = health.DamageCoeff;
            health.SetDamageCoeff(0f);
        }

        private void RestoreEndingPlayer()
        {
            if (_endingHealth != null)
                _endingHealth.SetDamageCoeff(_endingDamageCoeff);
            _endingHealth = null;
        }

        private double DeadManElapsed => DeadManActive ? SynchronizedTime - _officeAccess.destructionStart : -1d;
        private double DeadManEnd => _officeAccess.config.deadManSwitch.countdownAt + _officeAccess.config.deadManSwitch.countdownSeconds;

        private void BeginDeadManSwitch()
        {
            if (_deadManBegan || !DeadManActive)
                return;
            _deadManBegan = true;
            _takeoverAlarmDisabled = true;
            StopTakeoverAlarmLocal();
            if (IsEventAuthority)
                BroadcastTakeoverState();
            if (IsEventAuthority)
                SetPanicEvacuation(true);
            if (DiagnosticsEnabled)
                Log.LogInfo("ISB: Dead Man's Switch armed; countdown starts at " + (_officeAccess.destructionStart + _officeAccess.config.deadManSwitch.countdownAt).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".");
            StopRoutine(ref _announcementRout);
            StopRoutine(ref _scanner);
            StopRoutine(ref _decontaminationGateRoutine);
            StopRoutine(ref _decontaminationLockRoutine);
            StopFacilityAudio();
            _subtitleSystem?.Stop();
            RestoreLaboratoryPaSources();
            foreach (var entry in _activeAudioObjects)
            {
                if (entry?.Source != null)
                    entry.Source.Stop();
                if (entry?.Obj != null)
                    Destroy(entry.Obj);
            }

            _activeAudioObjects.Clear();
            _currentNetworkAnnouncement = "";
            _currentNetworkAnnouncementLength = 0;
            _facilitySequence.restoreAt = -1d;
            if (CanPresentLocally)
                StartCoroutine(LoadFacilityExplosion());
            StopOfficeMonitorBinding();
            ResetDecontaminationVideo();
            if (IsEventAuthority)
                BroadcastDecontaminationVideo();
        }

        private void TickDeadManSwitch()
        {
            if (!DeadManActive || !_raidisActive || !IsLaboratoryPresentationAllowed())
                return;
            BeginDeadManSwitch();
            var settings = _officeAccess.config.deadManSwitch;
            double elapsed = DeadManElapsed;
            _subtitleSystem?.SetForeground(elapsed >= DeadManEnd);
            UpdateDestructionFilter();
            if (!_localFacilityRaidEnded)
                EnsureFacilityAudio(elapsed >= settings.EndAudioAt ? settings.endAudio : settings.audio, _officeAccess.destructionStart + (elapsed >= settings.EndAudioAt ? settings.EndAudioAt : 0d));
            if (elapsed >= settings.countdownAt && (!_deadManGates || Time.realtimeSinceStartup >= _gateRetryAt))
            {
                _gateRetryAt = Time.realtimeSinceStartup + 2f;
                _decontaminationGatesOrdered = true;
                _takeoverGatesOpened = true;
                _gateBClosed = false;
                LabsFinalLockdown.ReleaseEvacuationGates();
                if (!_deadManGates)
                    _deadManGates = BlackoutBridge.ActivateGateExfils(EvacuationGates, Log, _blackoutApplied) >= 2;
                BlackoutBridge.ForceEvacuationExfils(EvacuationGates, Log);
            }

            if (elapsed >= DeadManEnd)
            {
                if (CanPresentLocally && !_localFacilityRaidEnded && !_deadManFx)
                {
                    _deadManFx = true;
                    _destructionEffects = new FacilityDestructionEffects(_activeRaidWorld?.MainPlayer, Log, _facilityExplosionClips, settings.endEffectSeconds);
                }

                _destructionEffects?.Tick((float)(elapsed - DeadManEnd));
                if (elapsed >= settings.FadeAt)
                {
                    if (elapsed < settings.PlayersDieAt)
                        ProtectEndingPlayer();
                    if (IsEventAuthority && _activeRaidWorld != null && Time.realtimeSinceStartup >= _deadManBotsAt)
                    {
                        _deadManBotsAt = Time.realtimeSinceStartup + 1f;
                        foreach (var bot in _activeRaidWorld.AllAlivePlayersList.ToArray())
                            if (bot != null && bot.IsAI && bot.HealthController?.IsAlive == true)
                                bot.ActiveHealthController?.Kill(EDamageType.Explosion);
                    }
                }

                if (!_deadManKilled && elapsed >= settings.PlayersDieAt)
                {
                    _deadManKilled = true;
                    RestoreEndingPlayer();
                    var local = _activeRaidWorld?.MainPlayer;
                    if (!_localFacilityRaidEnded && local?.HealthController?.IsAlive == true && _activeRaidWorld.AllAlivePlayersList.Contains(local))
                        local.ActiveHealthController?.Kill(EDamageType.Explosion);
                }
            }
        }

        private void TickOfficeMonitor()
        {
            if (!_raidisActive || !IsLaboratoryPresentationAllowed() || !CanPresentLocally || !_labsPreBlackoutEventActive)
                return;
            if (Time.realtimeSinceStartup < _monitorTickAt || Time.realtimeSinceStartup < _monitorRetryAt)
                return;
            _monitorTickAt = Time.realtimeSinceStartup + (_officeAccess?.stage >= 1 && !_computerReady ? 1f / 30f : .1f);
            try
            {
                if (_officeCanvas == null)
                    _officeCanvas = new OfficeScreenCanvas();
                if (DeadManActive)
                {
                    if (_deadManImage == null)
                    {
                        string name = _officeAccess.config.deadManSwitch.image;
                        if (Path.GetFileName(name) != name)
                            throw new InvalidOperationException("Invalid Dead Man's Switch image path.");
                        _deadManImage = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!ImageConversion.LoadImage(_deadManImage, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Info.Location), "Office", name))))
                            throw new InvalidOperationException("Dead Man's Switch image could not be decoded.");
                    }

                    var settings = _officeAccess.config.deadManSwitch;
                    _officeCanvas.ShowDestruction(_deadManImage, DeadManElapsed, settings.countdownAt, settings.countdownSeconds);
                }
                else if (_officeAccess?.IsValid() == true)
                    _officeCanvas.ShowCode(_officeAccess.operations.codeUploaded ? _officeAccess.raidCode : OfficeText("LockedCode", "[LOCKED]"));
                _officeCanvas.ShowPower(SynchronizedTime, DeadManActive ? -1d : _facilitySequence.countdownFrom, DeadManActive ? -1d : _facilitySequence.restoreAt, DeadManActive ? null : _facilitySequence.remoteOperations);
                TickTerminalScreens();
                bool powerOnly = !DeadManActive && !NightmareWarningActive && OfficeScreenCanvas.PowerPopup != null && _decontaminationScreens == null;
                if (!powerOnly)
                {
                    StopRoutine(ref _powerMonitorScan);
                    _powerMonitor?.Dispose();
                    _powerMonitor = null;
                }
                else
                {
                    if (_powerMonitor == null)
                    {
                        _powerMonitor = new DecontaminationScreens(_officeCanvas.PowerFrame, Log, renderer => !OfficeScreenCanvas.IsCodeScreen(renderer) && !OfficeScreenCanvas.IsTerminalScreen(renderer) && !OfficeScreenCanvas.IsTerminalOverlay(renderer));
                        _powerMonitorScan = StartCoroutine(_powerMonitor.FindScreens());
                    }

                    _powerMonitor.Tick(0d, Time.realtimeSinceStartup);
                }

                string mode = DeadManActive ? "destruction" : _decontaminationScreens == null && !NightmareWarningActive && _officeAccess?.IsValid() == true ? "office" : "video";
                if (_officeMonitorMode != mode)
                {
                    StopOfficeMonitorBinding();
                    _officeMonitorMode = mode;
                }

                if (mode == "video")
                    return;
                if (_officeMonitor == null)
                {
                    _officeMonitor = new DecontaminationScreens(_officeCanvas.Frame, Log, mode == "office" ? new Func<Renderer, bool>(OfficeScreenCanvas.IsCodeScreen) : null, mode == "destruction");
                    _officeMonitorScan = StartCoroutine(_officeMonitor.FindScreens());
                }

                _officeMonitor.Tick(0d, Time.realtimeSinceStartup);
            }
            catch (Exception ex)
            {
                if (_deadManImage != null)
                    Destroy(_deadManImage);
                _deadManImage = null;
                StopOfficeMonitorBinding();
                _monitorRetryAt = Time.realtimeSinceStartup + 15f;
                if (!_deadManErrorReported)
                    Log.LogError("ISB: office monitor presentation failed: " + ex.GetBaseException().Message);
                _deadManErrorReported = true;
            }
        }

        private void TickTerminalScreens()
        {
            if (DeadManActive)
            {
                StopTerminalScreens();
                return;
            }

            if (_officeAccess?.stage < 1)
                _officeCanvas.ShowStandby();
            else if (!_computerReady)
            {
                double elapsed = SynchronizedTime - _officeAccess.bootStartedAt;
                if (_computerBoot == null)
                    _computerBoot = new OfficeComputerBoot(Path.Combine(Path.GetDirectoryName(Info.Location), "ISBSpecialForces_Videos", "SystemStartup.mp4"), Log);
                _computerBoot.Tick(elapsed);
                _officeCanvas.ShowStartup(_computerBoot.Frame, _computerBoot.VideoFinished, _computerBoot.LoadingProgress);
                if (_computerBoot.Complete)
                {
                    _computerReady = true;
                    _computerBoot.Dispose();
                    _computerBoot = null;
                }
            }

            if (_computerReady)
            {
                if (_officeAccess?.consoleShown == true)
                    _officeCanvas.ShowTerminal(GetTerminalStatus(), GetTerminalDisplayControls(), _officeAccess.stage >= 2);
                else
                    _officeCanvas.ShowDesktop();
            }

            if (_terminalMonitor == null && _officeAccess?.IsValid() == true)
            {
                _terminalMonitor = new DecontaminationScreens(_officeCanvas.TerminalFrame, Log, OfficeScreenCanvas.IsTerminalScreen, true);
                _terminalMonitorScan = StartCoroutine(_terminalMonitor.FindScreens());
            }

            _terminalMonitor?.Tick(0, Time.realtimeSinceStartup);
        }

        private void StopTerminalScreens()
        {
            _computerBoot?.Dispose();
            _computerBoot = null;
            StopRoutine(ref _terminalMonitorScan);
            _terminalMonitor?.Dispose();
            _terminalMonitor = null;
            StopRoutine(ref _powerMonitorScan);
            _powerMonitor?.Dispose();
            _powerMonitor = null;
        }

        private AudioClip[] _facilityExplosionClips;
        private IEnumerator LoadFacilityExplosion()
        {
            var clips = new AudioClip[3];
            _facilityExplosionClips = clips;
            for (int i = 0; i < clips.Length && _raidisActive; i++)
            {
                int index = i;
                yield return LoadAudioClip(Path.Combine(_dataFolder, "FacilityExplosion" + (i + 1) + ".wav"), clip => clips[index] = clip);
            }
        }

        private void StopOfficeMonitorBinding()
        {
            StopRoutine(ref _officeMonitorScan);
            _officeMonitor?.Dispose();
            _officeMonitor = null;
        }

        private Action<GameWorld, bool> _panicEvacuation;
        private void SetPanicEvacuation(bool active)
        {
            try
            {
                if (_panicEvacuation == null && BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.samc137.isbspecialforces", out var plugin))
                {
                    var type = plugin.Instance.GetType().Assembly.GetType("ISBSpecialForces.Behavior.DeadManSwitchEvacuation");
                    _panicEvacuation = (Action<GameWorld, bool>)Delegate.CreateDelegate(typeof(Action<GameWorld, bool>), type.GetMethod("SetActive"));
                }

                _panicEvacuation?.Invoke(active ? _activeRaidWorld : null, active);
            }
            catch (Exception ex)
            {
                Log.LogError("ISB: evacuation layer unavailable: " + ex.GetBaseException().Message);
            }
        }

        private void UpdateDestructionFilter()
        {
            bool visible = !_localFacilityRaidEnded && DeadManActive && CanPresentLocally && _raidisActive && DeadManElapsed >= DeadManEnd;
            if (!visible)
            {
                if (_endingOverlay != null)
                    _endingOverlay.SetActive(false);
                return;
            }

            if (_endingOverlay == null)
            {
                _endingOverlay = new GameObject("ISB_FacilityEnding", typeof(RectTransform), typeof(Canvas));
                var canvas = _endingOverlay.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32760;
                var cover = new GameObject("Fade", typeof(RectTransform), typeof(Image));
                cover.transform.SetParent(_endingOverlay.transform, false);
                _endingImage = cover.GetComponent<Image>();
                _endingImage.raycastTarget = false;
                var rect = cover.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }

            _endingOverlay.SetActive(true);
            var settings = _officeAccess.config.deadManSwitch;
            float whiteAt = Math.Max(2f, settings.endEffectSeconds);
            float alpha = Mathf.Clamp01((float)(DeadManElapsed - DeadManEnd - whiteAt + .5d) / .5f);
            float black = Mathf.Clamp01((float)((DeadManElapsed - settings.FadeAt) / settings.fadeSeconds));
            _endingImage.color = new Color(1f - black, 1f - black, 1f - black, alpha);
        }

        private void ResetDeadManSwitch()
        {
            RestoreEndingPlayer();
            _subtitleSystem?.SetForeground(false);
            if (_endingOverlay != null)
                Destroy(_endingOverlay);
            _endingOverlay = null;
            _endingImage = null;
            _deadManBotsAt = 0f;
            SetPanicEvacuation(false);
            StopOfficeMonitorBinding();
            StopTerminalScreens();
            _officeCanvas?.Dispose();
            _officeCanvas = null;
            _destructionEffects?.Dispose();
            _destructionEffects = null;
            if (_deadManImage != null)
                Destroy(_deadManImage);
            _deadManImage = null;
            _localFacilityRaidEnded = false;
            _computerReady = false;
            _facilityExplosionClips = null;
            _deadManBegan = _deadManGates = _deadManKilled = _deadManFx = _deadManErrorReported = false;
            _officeMonitorMode = "";
            _monitorTickAt = _monitorRetryAt = _gateRetryAt = 0;
        }
    }
}
