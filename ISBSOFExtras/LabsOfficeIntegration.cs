using System;
using System.Collections.Generic;
using System.Globalization;
using ISB.Compatibility;
using Newtonsoft.Json;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private OfficeAccessState _officeAccess;
        private LabsOfficeScene _officeScene;
        private string _officeRequest;
        private readonly Dictionary<string, string> _officeReplies = new Dictionary<string, string>();
        private readonly Queue<string> _officeReplyOrder = new Queue<string>();
        private bool _officeAbortApplied;
        private void SetOfficeStatus(OfficeAccessState state)
        {
            if (!IsEventAuthority || state?.IsValid() != true)
                return;
            if (_officeAccess != null && _officeAccess.raidCode == state.raidCode)
                return;
            _officeAccess = state;
        }

        private void TickOfficeAccess()
        {
            if (!OfficeMapAvailable)
                return;
            TickOfficeMap();
            TickOfficeRadio();
            ApplyOfficePause();
            if (_officeAccess.stage == 2)
                ConfirmSharedAuthorizationEffect("OFFICE_CODE");
            if (_officeAccess.aborted)
                ConfirmSharedAuthorizationEffect("OFFICE_SHUTDOWN");
            TickDeadManSwitch();
            if (!CanPresentLocally)
                return;
            if (_officeScene == null)
            {
                _officeScene = new LabsOfficeScene(Log, SubmitOfficeCode, RequestOfficeShutdown);
                _officeScene.BootReady = () => _computerReady;
                _officeScene.TerminalStatus = GetTerminalStatus;
                _officeScene.GeneratorStatus = GetGeneratorStatus;
                _officeScene.ControlState = TerminalState;
                _officeScene.TerminalClock = () => SynchronizedTime;
                _officeScene.TerminalCommand = RequestTerminalCommand;
            }

            _officeScene.Tick(_activeRaidWorld, _officeAccess);
        }

        private void OnGUI()
        {
            _officeScene?.Draw();
        }

        private void SubmitOfficeCode(string code, int stage)
        {
            var player = _activeRaidWorld?.MainPlayer;
            if (player == null)
                return;
            _officeRequest = Guid.NewGuid().ToString("N");
            string payload = "OC|" + _officeRequest + "|" + player.ProfileId + "|" + code + "|" + stage.ToString(CultureInfo.InvariantCulture);
            if (IsEventAuthority)
                ReceiveOfficePacket(payload.Split('|'));
            else
                NetworkBridge.SendToServer(payload);
        }

        private void RequestOfficeShutdown()
        {
            RequestTerminalCommand(_officeAccess?.aborted == true ? "DECON_ON" : "DECON_OFF");
        }

        private bool OfficePlayerNear(string profile, OfficePoint point)
        {
            if (_activeRaidWorld == null || string.IsNullOrEmpty(profile) || profile.Length > 64)
                return false;
            foreach (var player in _activeRaidWorld.AllAlivePlayersList)
                if (player != null && !player.IsAI && player.ProfileId == profile && player.HealthController?.IsAlive == true)
                    return (player.Position - (ReferenceEquals(point, _officeAccess.config.keypad) && _officeScene != null ? _officeScene.KeypadPosition : LabsOfficeScene.Point(point))).sqrMagnitude <= 16f;
            return false;
        }

        private void ReceiveOfficePacket(string[] parts)
        {
            if (!_raidisActive || !IsLaboratoryPresentationAllowed())
                return;
            if (parts[0] == "OP" || parts[0] == "OPR")
            {
                ReceiveOfficeMapPacket(parts);
                return;
            }

            if (parts[0] == "OU" && parts.Length == 2)
            {
                ReceiveOfficeSnapshot(parts[1]);
                return;
            }

            if (parts[0] == "OR" && parts.Length == 3)
            {
                if (!IsEventAuthority && parts[1] == _officeRequest)
                {
                    _officeScene?.Result(parts[2] == "1");
                    _officeRequest = null;
                }

                return;
            }

            if (parts[0] == "OA" && parts.Length == 4 && !IsEventAuthority && parts[1] == _activeRaidWorld?.MainPlayer?.ProfileId)
            {
                TerminalFeedback(parts[2], parts[3] == "1");
                return;
            }

            if (parts[0] == "ON" && parts.Length == 3 && !IsEventAuthority && CanPresentLocally)
            {
                LabsTakeoverInfrastructure.NotifyRemote(parts[1], parts[2] == "1");
                return;
            }

            if (!IsEventAuthority || _officeAccess?.IsValid() != true)
                return;
            if (parts[0] == "OQ" && parts.Length == 2 && _labsPreBlackoutEventActive && !_officeAccess.radioOff && OfficePlayerNearRadio(parts[1]))
            {
                _officeAccess.radioOff = true;
                _officeAccess.revision++;
                BroadcastOfficeState();
                return;
            }

            if (parts[0] == "OC" && parts.Length == 5 && parts[1].Length == 32 && Guid.TryParseExact(parts[1], "N", out _) && OfficeAccessState.ValidCode(parts[3]) && int.TryParse(parts[4], out int stage) && OfficePlayerNear(parts[2], _officeAccess.config.keypad))
            {
                string token = parts[2] + ":" + parts[1];
                if (!_officeReplies.TryGetValue(token, out string result))
                {
                    result = _officeAccess.Submit(parts[3], stage) ? "1" : "0";
                    if (result == "1" && stage == 0)
                        _officeAccess.bootStartedAt = SynchronizedTime + .4d;
                    if (result == "0" && _officeAccess.ArmOnWrongCode(parts[3], stage, SynchronizedTime + .5d))
                        BeginDeadManSwitch();
                    _officeReplies[token] = result;
                    _officeReplyOrder.Enqueue(token);
                    while (_officeReplyOrder.Count > 32)
                        _officeReplies.Remove(_officeReplyOrder.Dequeue());
                }

                BroadcastOfficeState();
                if (parts[1] == _officeRequest)
                {
                    _officeScene?.Result(result == "1");
                    _officeRequest = null;
                }

                if (IsFikaServer)
                    NetworkBridge.Broadcast("OR|" + parts[1] + "|" + result);
            }
            else if (parts[0] == "OT" && parts.Length == 4 && parts[3].Length <= 32 && int.TryParse(parts[2], out int revision) && revision >= 0 && revision <= _officeAccess.revision && OfficePlayerNear(parts[1], _officeAccess.config.shutdownButton))
            {
                bool applied = ApplyTerminalCommand(parts[3], parts[1]);
                if (applied)
                    _officeAccess.revision++;
                BroadcastOfficeState();
                if (parts[1] == _activeRaidWorld?.MainPlayer?.ProfileId)
                    TerminalFeedback(parts[3], applied);
                else if (IsFikaServer)
                    NetworkBridge.Broadcast("OA|" + parts[1] + "|" + parts[3] + "|" + (applied ? "1" : "0"));
            }
        }

        private string GetOfficeSnapshot() => _officeAccess == null ? "" : EncodeNetworkString(JsonConvert.SerializeObject(_officeAccess));
        private void BroadcastOfficeState()
        {
            if (IsFikaServer)
                NetworkBridge.Broadcast("OU|" + GetOfficeSnapshot());
        }

        private bool _officeSnapshotReceived;
        private void ReceiveOfficeSnapshot(string payload)
        {
            if (IsEventAuthority || string.IsNullOrEmpty(payload) || payload.Length > 32768)
                return;
            try
            {
                var state = JsonConvert.DeserializeObject<OfficeAccessState>(DecodeNetworkString(payload));
                if (state?.IsValid() != true || (_officeAccess != null && (_officeAccess.raidCode != state.raidCode || state.revision <= _officeAccess.revision)))
                    return;
                if (!_officeSnapshotReceived)
                {
                    foreach (var award in state.operations.awards)
                        _officeAwarded.Add(award.Key);
                    _officeSnapshotReceived = true;
                }

                _officeAccess = state;
            }
            catch (JsonException)
            {
            }
            catch (FormatException)
            {
            }
        }

        private void ResetOfficeAccess()
        {
            _questProgress.Clear();
            _questChecks.Clear();
            _questCacheAt = 0f;
            _questProgressAt = 0f;
            _questProgressFailed = false;
            ResetOfficeMap();
            _officeSnapshotReceived = false;
            ResetOfficeRadio();
            ResetFacilitySequences();
            ResetDeadManSwitch();
            _officeScene?.Dispose();
            _officeScene = null;
            _officeAccess = null;
            _officeRequest = null;
            _officeReplies.Clear();
            _officeReplyOrder.Clear();
            _officeAbortApplied = false;
        }
    }
}
