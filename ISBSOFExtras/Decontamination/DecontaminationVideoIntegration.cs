using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private DecontaminationScreens _decontaminationScreens;
        private Coroutine _decontaminationScreenScan;
        private double _decontaminationVideoStart = -1d;
        private bool _decontaminationVideoFailed;
        private OfficeScreenCanvas _decontaminationFallback;
        private int _decontaminationVideoAttempts;
        private float _decontaminationVideoRetryAt;
        private string DecontaminationVideoPath => Path.Combine(Path.GetDirectoryName(Info.Location), "ISBSpecialForces_Videos", DecontaminationVideoTimeline.FileName);

        private void StartDecontaminationVideo(double sequenceStart)
        {
            if (_decontaminationVideoStart >= 0d)
                return;
            _decontaminationVideoStart = sequenceStart;
            BroadcastDecontaminationVideo();
            TickDecontaminationVideo();
        }

        private double DecontaminationAnnouncementTime(string clip)
        {
            if (!((_decontamination?.VideoTimelineEnabled ?? true) || DecontaminationPaused) || _decontaminationVideoStart < 0d || (ShouldSkipDecontaminationAudio(clip) && !DecontaminationPaused))
                return -1d;
            int index = DecontaminationVideoTimeline.Index(clip);
            if (index < 0)
                return -1d;
            return _decontaminationVideoStart + DecontaminationVideoTimeline.Start(index, AnnouncementTriggerDelays.IsRussian(clip));
        }

        private void TickDecontaminationVideo()
        {
            if (NightmareWarningActive)
            {
                if (_decontaminationScreens != null) StopDecontaminationScreenPlayback();
                return;
            }
            if (DeadManActive || !_raidisActive || !IsLaboratoryPresentationAllowed() || !CanPresentLocally || !((_decontamination?.ScreenVideoEnabled ?? true) || DecontaminationPaused) || _decontaminationVideoStart < 0d)
            {
                StopDecontaminationScreenPlayback();
                return;
            }

            if (Time.realtimeSinceStartup < _decontaminationVideoRetryAt)
                return;
            if (_decontaminationVideoFailed)
            {
                TickDecontaminationFallback();
                return;
            }

            try
            {
                if (_decontaminationScreens == null)
                {
                    if (!File.Exists(DecontaminationVideoPath))
                        throw new FileNotFoundException("Decontamination video is missing.", DecontaminationVideoPath);
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        Log.LogInfo("ISB SOF video: Preparing playback, attempt " + (_decontaminationVideoAttempts + 1) + "/3.");
                    }

                    StopOfficeMonitorBinding();
                    StopRoutine(ref _powerMonitorScan);
                    _powerMonitor?.Dispose();
                    _powerMonitor = null;
                    _emergencyLighting.SetScreensSuppressed(true);
                    _decontaminationScreens = new DecontaminationScreens(DecontaminationVideoPath, Log, renderer => !OfficeScreenCanvas.IsTerminalScreen(renderer));
                    _decontaminationScreenScan = StartCoroutine(_decontaminationScreens.FindScreens());
                }

                _decontaminationScreens.SetPaused(DecontaminationPaused);
                _decontaminationScreens.Tick(DecontaminationClock - _decontaminationVideoStart, Time.realtimeSinceStartup);
            }
            catch (Exception ex)
            {
                _decontaminationVideoFailed = ++_decontaminationVideoAttempts >= 3;
                _decontaminationVideoRetryAt = Time.realtimeSinceStartup + 5f;
                StopDecontaminationScreenPlayback();
                if (_decontaminationVideoFailed)
                    TickDecontaminationFallback();
                Log.LogError("ISB SOF video: Attempt " + _decontaminationVideoAttempts + "/3 failed; " + (_decontaminationVideoFailed ? "using the countdown display. " : "retry in 5 seconds. ") + ex.GetBaseException().Message);
            }
        }

        private void TickDecontaminationFallback()
        {
            try
            {
                if (_decontaminationFallback == null)
                    _decontaminationFallback = new OfficeScreenCanvas(false);
                double elapsed = Math.Max(0d, DecontaminationClock - _decontaminationVideoStart);
                _decontaminationFallback.ShowDecontamination(elapsed, DecontaminationPaused);
                if (_decontaminationScreens == null)
                {
                    StopOfficeMonitorBinding();
                    StopRoutine(ref _powerMonitorScan);
                    _powerMonitor?.Dispose();
                    _powerMonitor = null;
                    _emergencyLighting.SetScreensSuppressed(true);
                    _decontaminationScreens = new DecontaminationScreens(_decontaminationFallback.Frame, Log, renderer => !OfficeScreenCanvas.IsTerminalScreen(renderer));
                    _decontaminationScreenScan = StartCoroutine(_decontaminationScreens.FindScreens());
                }

                _decontaminationScreens.Tick(elapsed, Time.realtimeSinceStartup);
            }
            catch
            {
                StopDecontaminationScreenPlayback();
                _decontaminationVideoRetryAt = Time.realtimeSinceStartup + 5f;
            }
        }

        private void StopDecontaminationScreenPlayback()
        {
            StopRoutine(ref _decontaminationScreenScan);
            _decontaminationScreens?.Dispose();
            _decontaminationScreens = null;
            _decontaminationFallback?.Dispose();
            _decontaminationFallback = null;
            _emergencyLighting.SetScreensSuppressed(false);
        }

        private void ResetDecontaminationVideo()
        {
            StopDecontaminationScreenPlayback();
            _decontaminationVideoStart = -1d;
            _decontaminationVideoFailed = false;
            _decontaminationVideoAttempts = 0;
            _decontaminationVideoRetryAt = 0f;
        }

        private string GetDecontaminationVideoSnapshot() => _decontaminationVideoStart.ToString("R", CultureInfo.InvariantCulture);
        private void BroadcastDecontaminationVideo()
        {
            if (IsFikaServer)
                NetworkBridge.Broadcast("DV|" + GetDecontaminationVideoSnapshot());
        }

        private void ReceiveDecontaminationVideo(string payload)
        {
            if (IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed() || !double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out double start))
                return;
            if (start != -1d && (!DecontaminationVideoTimeline.ValidStart(start) || Math.Abs(SynchronizedTime - start) > 86400d))
                return;
            _decontaminationVideoStart = start;
            TickDecontaminationVideo();
        }
    }
}
