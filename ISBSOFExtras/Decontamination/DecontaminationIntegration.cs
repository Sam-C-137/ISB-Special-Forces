namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private LabsDecontamination _decontamination;
        private int _lastGasRevisionSent = -1;
        private bool _decontaminationSurvivalRecorded;
        private bool _decontaminationHasStarted;
        private bool _tenMinuteSurvivalRecorded;
        private bool _fiveMinuteSurvivalRecorded;
        private readonly DecontaminationSurvivalTimer _decontaminationSurvivalTimer = new DecontaminationSurvivalTimer();
        private bool CanRecordDecontaminationSurvival(EFT.Player player, EFT.ExitStatus status, string location, string exit)
        {
            if (player != null && ReferenceEquals(player, _activeRaidWorld?.MainPlayer))
                _localFacilityRaidEnded = true;
            if (_decontaminationSurvivalRecorded)
                return false;
            bool eligible = DecontaminationSurvivalQuest.Qualifies(player != null && !player.IsAI && ReferenceEquals(player, _activeRaidWorld?.MainPlayer), _decontaminationHasStarted || _decontamination?.HasStarted(DecontaminationClock) == true, status.ToString(), location, exit);
            if (eligible)
                _decontaminationSurvivalRecorded = true;
            return eligible;
        }

        private void UpdateDecontaminationSurvival()
        {
            var player = _activeRaidWorld?.MainPlayer;
            bool active = _raidisActive && UnityEngine.Time.timeScale > 0f && player != null && !player.IsAI && player.HealthController?.IsAlive == true && !DecontaminationPaused && _decontamination?.HasStarted(DecontaminationClock) == true;
            _decontaminationSurvivalTimer.Update(DecontaminationClock, active);
        }

        private bool CanRecordFiveMinuteSurvival(EFT.Player player, EFT.ExitStatus status, string location)
        {
            if (_fiveMinuteSurvivalRecorded)
                return false;
            UpdateDecontaminationSurvival();
            bool eligible = DecontaminationSurvivalQuest.QualifiesFiveMinutes(player != null && !player.IsAI && ReferenceEquals(player, _activeRaidWorld?.MainPlayer), _decontaminationSurvivalTimer.Seconds, status.ToString(), location);
            if (eligible)
                _fiveMinuteSurvivalRecorded = true;
            return eligible;
        }

        private bool CanRecordTenMinuteSurvival(EFT.Player player, EFT.ExitStatus status, string location)
        {
            if (_tenMinuteSurvivalRecorded)
                return false;
            UpdateDecontaminationSurvival();
            bool eligible = DecontaminationSurvivalQuest.QualifiesTenMinutes(player != null && !player.IsAI && ReferenceEquals(player, _activeRaidWorld?.MainPlayer), _decontaminationSurvivalTimer.Seconds, status.ToString(), location);
            if (eligible)
                _tenMinuteSurvivalRecorded = true;
            return eligible;
        }

        private void InitializeDecontamination()
        {
            DisposeDecontamination();
            ResetDecontaminationEvents();
            _decontaminationSurvivalRecorded = false;
            _tenMinuteSurvivalRecorded = false;
            _fiveMinuteSurvivalRecorded = false;
            _decontaminationSurvivalTimer.Reset();
            _decontaminationHasStarted = false;
            if (!IsLaboratoryPresentationAllowed())
                return;
            string folder = System.IO.Path.GetDirectoryName(typeof(ISBSpecialForcesAudio).Assembly.Location);
            string path = ResolveOptionalBlackoutDataPath(folder, "decontamination.json");
            _decontamination = new LabsDecontamination(_activeRaidWorld, path, Log);
            TickDecontamination();
            if (_decontamination?.Configured == true)
            {
                try
                {
                    LabsFinalLockdown.Prepare();
                }
                catch (System.Exception ex)
                {
                    Log.LogError("ISB SOF: Decontamination native effects unavailable: " + ex.GetBaseException().Message);
                }
            }
        }

        private void TickDecontamination()
        {
            if (_decontamination == null || !_decontamination.Due(UnityEngine.Time.realtimeSinceStartup))
                return;
            if (!_raidisActive || !IsLaboratoryPresentationAllowed())
            {
                DisposeDecontamination();
                return;
            }

            _decontamination.Tick(DecontaminationClock, UnityEngine.Time.realtimeSinceStartup, IsEventAuthority, CanPresentLocally, _decontamination.Configured, true);
            if (!DecontaminationPaused && _decontamination.FireExtinctionStartedAt >= 0d)
                _takeoverFire.BeginExtinguishing(_decontamination.FireExtinctionStartedAt + (_officeAccess?.pausedSeconds ?? 0d));
            _decontaminationHasStarted |= _decontamination.HasStarted(DecontaminationClock);
            UpdateDecontaminationSurvival();
            BroadcastDecontamination();
        }

        private void TriggerDecontamination(string clipName)
        {
            if (!IsEventAuthority || _decontamination == null)
                return;
            _decontamination.Trigger(clipName, _currentNetworkAnnouncementStart - (_officeAccess?.pausedSeconds ?? 0d), _decontamination.Configured);
            if (!_decontamination.Announced)
                return;
            if (_decontamination.Configured && GasRules.MatchesClip(clipName, GasRules.WarningClip))
            {
                StartDecontaminationVideo(_currentNetworkAnnouncementStart - (_officeAccess?.pausedSeconds ?? 0d));
                ISB.Compatibility.ISBEffectNotifications.Publish("decontamination_warning", Log);
            }

            ScheduleDecontaminationEvents(clipName);
            BroadcastDecontamination();
        }

        private bool ShouldSkipDecontaminationAudio(string fileName)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            return name.StartsWith("DecontaminationProcess", System.StringComparison.OrdinalIgnoreCase) && (_decontamination == null || !_decontamination.Configured);
        }

        private void BroadcastDecontamination()
        {
            if (!IsFikaNetworkSession || !IsFikaServer || _decontamination == null || _decontamination.Revision == _lastGasRevisionSent)
                return;
            if (NetworkBridge.Broadcast("D|" + GetDecontaminationSnapshot()))
                _lastGasRevisionSent = _decontamination.Revision;
        }

        private string GetDecontaminationSnapshot() => _decontamination == null ? string.Empty : EncodeNetworkString(_decontamination.SerializeState());
        private void ReceiveDecontamination(string payload)
        {
            if (IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed() || _decontamination == null || string.IsNullOrEmpty(payload) || payload.Length > 45000)
                return;
            _decontamination.AcceptState(DecodeNetworkString(payload));
        }

        private void DisposeDecontamination()
        {
            ResetDecontaminationVideo();
            _decontamination?.Dispose();
            _decontamination = null;
            _lastGasRevisionSent = -1;
        }
    }
}
