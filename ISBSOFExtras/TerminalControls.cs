using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private double DecontaminationClock => (_officeAccess?.Clock(SynchronizedTime) ?? SynchronizedTime) - NightmareDecontaminationDelay;
        private bool DecontaminationPaused => _officeAccess?.aborted == true || NightmareAnnouncementActive;

        private static bool IsDecontaminationClip(string name) => Path.GetFileNameWithoutExtension(name ?? "").StartsWith("DecontaminationProcess", StringComparison.OrdinalIgnoreCase);
        private double AnnouncementNetworkClock(string name) => IsNightmareAnnouncement(name) ? NightmareClock : SynchronizedTime;
        private double AnnouncementClock(string name) => IsDecontaminationClip(name) ? DecontaminationClock : AnnouncementNetworkClock(name);
        private IEnumerator WaitAnnouncement(string name, float duration, params AudioSource[] sources) => WaitAnnouncement(name, duration, true, sources);
        private IEnumerator WaitAnnouncement(string name, float duration, bool mutable, params AudioSource[] sources)
        {
            var originalMute = new bool[sources.Length];
            for (int i = 0; i < sources.Length; i++)
                originalMute[i] = sources[i] != null && sources[i].mute;
            try
            {
                double end = AnnouncementClock(name) + duration;
                bool paused = false, restoreSubtitles = false;
                while (_raidisActive && !DeadManActive && AnnouncementClock(name) < end)
                {
                    if (mutable)
                        for (int i = 0; i < sources.Length; i++)
                            if (sources[i] != null)
                                sources[i].mute = originalMute[i] || AnnouncementMuted(name);
                    bool pause = IsDecontaminationClip(name) && DecontaminationPaused;
                    if (pause != paused)
                    {
                        foreach (var source in sources)
                            if (source != null)
                            {
                                if (pause)
                                    source.Pause();
                                else
                                    source.UnPause();
                            }

                        paused = pause;
                        restoreSubtitles = true;
                    }

                    if (IsDecontaminationClip(name))
                    {
                        if (_facilityAudio?.isPlaying == true)
                            restoreSubtitles = true;
                        else if (!paused && restoreSubtitles)
                        {
                            foreach (var source in sources)
                                if (source?.isPlaying == true)
                                {
                                    _subtitleSystem?.Play(source, name);
                                    break;
                                }

                            restoreSubtitles = false;
                        }
                    }

                    yield return null;
                }
            }
            finally
            {
                if (mutable)
                    for (int i = 0; i < sources.Length; i++)
                        if (sources[i] != null)
                            sources[i].mute = originalMute[i];
            }
        }

        private void RequestTerminalCommand(string command)
        {
            var player = _activeRaidWorld?.MainPlayer;
            if (player == null || _officeAccess == null)
                return;
            string message = "OT|" + player.ProfileId + "|" + _officeAccess.revision + "|" + command;
            if (IsEventAuthority)
                ReceiveOfficePacket(message.Split('|'));
            else
                NetworkBridge.SendToServer(message);
        }

        private bool ApplyTerminalCommand(string command, string profile)
        {
            if (!_labsPreBlackoutEventActive || DeadManActive || _officeAccess == null || !_officeAccess.ConsoleReady(SynchronizedTime))
                return false;
            if (command == "VIEW")
            {
                _officeAccess.consoleShown = true;
                _officeAccess.operations.Record(profile, ISB.Compatibility.OfficeRaidOperations.Code);
                if (_officeAccess.aborted)
                    _officeAccess.operations.Record(profile, ISB.Compatibility.OfficeRaidOperations.Shutdown);
                return true;
            }

            if (command.StartsWith("DOWNLOAD:", StringComparison.Ordinal) || command.StartsWith("EJECT:", StringComparison.Ordinal))
                return OfficeDownloadCommand(profile, command);
            if (command == "DECON_ON" || command == "DECON_OFF")
            {
                if (!_officeAccess.SetDecontamination(command == "DECON_ON", SynchronizedTime))
                    return false;
                if (command == "DECON_OFF")
                    _officeAccess.operations.Record(profile, ISB.Compatibility.OfficeRaidOperations.Shutdown);
                ApplyOfficePause();
                QueueSystemRestart();
                return true;
            }

            if (command == "ALARM_ON" || command == "ALARM_OFF")
            {
                bool disabled = command == "ALARM_OFF";
                return disabled != _takeoverAlarmDisabled && ApplyTakeoverActionAuthority(LabsTakeoverInfrastructure.AlarmAction);
            }

            if (command == "LIGHTS_ON" || command == "LIGHTS_OFF")
            {
                bool disabled = command == "LIGHTS_OFF";
                return disabled != _takeoverEmergencyLightsDisabled && ApplyTakeoverActionAuthority(LabsTakeoverInfrastructure.EmergencyLightsAction);
            }

            if (command == "POWER_ON")
                return ApplyTakeoverActionAuthority(LabsTakeoverInfrastructure.MasterAction);
            if (command == "POWER_OFF")
                return ApplyTakeoverActionAuthority(LabsTakeoverInfrastructure.ShutdownMasterAction);
            if (command == "GATES_OPEN")
                return ApplyTakeoverActionAuthority(LabsTakeoverInfrastructure.GatesAction);
            if (command.StartsWith("PISTON_", StringComparison.Ordinal) && command.Length == 8)
                return ScheduleRemoteCell("P:" + command.Substring(7), false);
            if (command.StartsWith("CELL_", StringComparison.Ordinal) && command.Length == 6)
            {
                string cell = command.Substring(5);
                bool shutdown = !_blackoutApplied;
                return ScheduleRemoteCell(cell, shutdown);
            }

            return false;
        }

        private bool ScheduleRemoteCell(string cell, bool shutdown)
        {
            if (!CanScheduleCell(cell, shutdown) || !_facilitySequence.Schedule(cell, shutdown, SynchronizedTime))
                return false;
            ApplyFacilityPanelStateLocal();
            BroadcastFacilitySequence();
            return true;
        }

        private bool TerminalState(string name)
        {
            switch (name)
            {
                case "POWER":
                    return !_blackoutApplied;
                case "ALARM":
                    return _takeoverAlarmStarted && !_takeoverAlarmDisabled;
                case "LIGHTS":
                    return _takeoverEmergencyLightsStarted && !_takeoverEmergencyLightsDisabled;
                case "DECON":
                    return _officeAccess?.aborted != true;
                case "GATES":
                    return _takeoverGatesOpened;
                case "GATE_A":
                    return _takeoverGateA;
                case "GATE_B":
                    return _takeoverGateB;
                case "GATE_C":
                    return _takeoverGateC;
                case "CELL_A":
                    return _takeoverPowerA && !_manualShutdownA;
                case "CELL_B":
                    return _takeoverPowerB && !_manualShutdownB;
                case "CELL_C":
                    return _takeoverPowerC && !_manualShutdownC;
                case "CELL_D":
                    return _takeoverPowerD && !_manualShutdownD;
                default:
                    return false;
            }
        }

        private void TerminalFeedback(string command, bool accepted)
        {
            if (command == "VIEW")
                return;
            if (command.StartsWith("DOWNLOAD:") || command.StartsWith("EJECT:"))
            {
                _officeScene?.CommandResult(accepted);
                if (!accepted && command.StartsWith("EJECT:") && _officeAccess?.operations.CanEject(_activeRaidWorld.MainPlayer.ProfileId) == true)
                {
                    OfficeNotice("InventoryFull", "No inventory space - free one slot and eject the SSD again.");
                    return;
                }

                if (!accepted)
                    OfficeNotice(_officeAccess?.operations.diskInstalled != true ? "NoDisk" : "DownloadBusy", _officeAccess?.operations.diskInstalled != true ? "No hard drive - install an SSD to download." : "This SSD is in use by another operator or the operation is unavailable.");
                return;
            }

            _officeScene?.CommandResult(accepted);
            if (accepted && (command.StartsWith("CELL_") || command.StartsWith("PISTON_")))
            {
                LabsTakeoverInfrastructure.ShowTerminalNotice("RemoteStarted", "Remote operation scheduled: 02:00.");
                return;
            }

            string action = command == "POWER_ON" ? "MASTER" : command == "POWER_OFF" ? "SHUTDOWN_MASTER" : command == "GATES_OPEN" ? "GATES" : command;
            LabsTakeoverInfrastructure.NotifyRemote(action, accepted);
        }

        private bool CanScheduleCell(string cell, bool shutdown)
        {
            if (cell.StartsWith("P:", StringComparison.Ordinal))
                return CanUseGateOverride() && (cell == "P:A" ? !_takeoverGateA : cell == "P:B" ? !_takeoverGateB : cell == "P:C" && !_takeoverGateC);
            if (shutdown ? !CanAuthorizeFacilityShutdown() : !CanAuthorizeFacilityPower())
                return false;
            bool a = shutdown ? _manualShutdownA : _takeoverPowerA;
            bool b = shutdown ? _manualShutdownB : _takeoverPowerB;
            bool c = shutdown ? _manualShutdownC : _takeoverPowerC;
            bool d = shutdown ? _manualShutdownD : _takeoverPowerD;
            return cell == "A" ? !a : cell == "B" ? !b : cell == "C" ? !c : cell == "D" && !d;
        }

        private void TickRemoteCell()
        {
            if (!IsEventAuthority || DeadManActive)
                return;
            bool changed = false;
            while (_facilitySequence.TryTakeDue(SynchronizedTime, out var operation))
            {
                changed = true;
                string action = operation.Action;
                bool applied = CanScheduleCell(operation.cell, operation.shutdown) && ApplyTakeoverActionAuthority(action, true);
                if (CanPresentLocally)
                    LabsTakeoverInfrastructure.NotifyRemote(action, applied);
                if (IsFikaServer)
                    NetworkBridge.Broadcast("ON|" + action + "|" + (applied ? "1" : "0"));
            }

            if (!changed)
                return;
            ApplyFacilityPanelStateLocal();
            BroadcastFacilitySequence();
        }

        private string GetGeneratorStatus()
        {
            bool shutdown = !_blackoutApplied;
            string result = "A [" + ((shutdown ? _manualShutdownA : _takeoverPowerA) ? "OK" : "--") + "]  B [" + ((shutdown ? _manualShutdownB : _takeoverPowerB) ? "OK" : "--") + "]  C [" + ((shutdown ? _manualShutdownC : _takeoverPowerC) ? "OK" : "--") + "]  D [" + ((shutdown ? _manualShutdownD : _takeoverPowerD) ? "OK" : "--") + "]";
            foreach (var operation in _facilitySequence.remoteOperations)
            {
                int seconds = Math.Max(0, (int)Math.Ceiling(operation.finishAt - SynchronizedTime));
                result += "\n" + ISB.Compatibility.ISBLocalizedText.Get(operation.cell.StartsWith("P:", StringComparison.Ordinal) ? "ISB/Office/RemotePistonLabel" : "ISB/Office/RemoteCellLabel", operation.cell.StartsWith("P:", StringComparison.Ordinal) ? "> REMOTE PISTON " : "> REMOTE CELL ") + operation.cell.Replace("P:", "") + " > " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }

            return result;
        }

        private void QueueSystemRestart()
        {
            double start = SynchronizedTime + .4d;
            foreach (var cue in _facilitySequence.cues)
                start = Math.Max(start, cue.start + cue.duration + .15d);
            _facilitySequence.cues.RemoveAll(cue => cue.start + cue.duration < SynchronizedTime);
            _facilitySequence.cues.Add(CreateFacilityCue("RestartingSystems.wav", start));
            BroadcastFacilitySequence();
        }

        private void ApplyOfficePause()
        {
            if (_decontamination == null || _officeAccess == null)
                return;
            bool paused = _officeAccess.aborted;
            _decontamination.SetPaused(paused);
            if (_officeAbortApplied == paused)
                return;
            _officeAbortApplied = paused;
            if (paused)
            {
                StopRoutine(ref _decontaminationLockRoutine);
                if (!_decontaminationExfilsLocked)
                    LabsFinalLockdown.Reset();
            }
            else if (_decontaminationClosingStarted >= 0d && !_decontaminationExfilsLocked)
            {
                double start = _decontaminationClosingStarted;
                _decontaminationClosingStarted = -1d;
                BeginDecontaminationClosing(start, null);
            }

            BroadcastDecontamination();
        }
    }
}
