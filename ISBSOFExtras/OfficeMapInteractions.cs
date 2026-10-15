using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using ISB.Compatibility;
using Newtonsoft.Json;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private sealed class OfficePlant
        {
            internal GamePlayerOwner Owner;
            internal Item Item;
            internal string Kind, Token;
            internal bool Started, Consumed, Busy;
            internal float RetryAt, RequestedAt;
            internal int Generation;
        }

        private readonly Dictionary<string, EFT.Interactive.PlaceItemTrigger> _officeStations = new Dictionary<string, EFT.Interactive.PlaceItemTrigger>();
        private float _officeZoneSearchAt;
        private readonly HashSet<string> _officeQuestEffects = new HashSet<string>();
        private static readonly string[] OfficePlantKinds =
        {
            "HACK",
            "UPLOAD",
            "SSD"
        };
        private readonly Dictionary<string, float> _officeAwardRetry = new Dictionary<string, float>();
        private readonly HashSet<string> _officeAwarded = new HashSet<string>();
        private OfficePlant _officePlant;
        private int _officeGeneration;
        private float _officeMapTick;
        private OfficeTerminalFile[] _officeFiles;
        private bool _officeGrantError;
        private bool OfficeMapAvailable => _raidisActive && _labsPreBlackoutEventActive && IsLaboratoryPresentationAllowed() && _officeAccess?.config?.enabled == true && _officeAccess.operations != null;

        private static OfficePoint OfficePlantPoint(string kind) => kind == "HACK" ? new OfficePoint
        {
            x = -120.924f,
            y = 1.374f,
            z = -324.012f
        }

        : kind == "UPLOAD" ? new OfficePoint
        {
            x = -180.855f,
            y = .085f,
            z = -329.446f
        }

        : new OfficePoint
        {
            x = -267.751f,
            y = 4.188f,
            z = -374.327f
        };
        private static string OfficePlantZoneId(string kind) => "isb_cb_office_plant_" + (kind == "HACK" ? "hack" : kind == "UPLOAD" ? "upload" : "ssd");
        private static string OfficePlantItem(string kind) => kind == "HACK" ? "68e63a53371639e1e4004b6e" : kind == "UPLOAD" ? "e875fa2b6553908dc57716f6" : "590c392f86f77444754deb29";
        private static string OfficeText(string key, string fallback) => ISBLocalizedText.Get("ISB/Office/" + key, fallback);
        private static void OfficeNotice(string key, string fallback) => LabsTakeoverInfrastructure.ShowTerminalNotice(key, fallback);
        private Player OfficePlayer(string profile)
        {
            foreach (var player in _activeRaidWorld.AllAlivePlayersList)
                if (player != null && !player.IsAI && player.ProfileId == profile && player.HealthController?.IsAlive == true)
                    return player;
            return null;
        }

        private static Item OfficeInventoryItem(Player player, string item = null, string template = null)
        {
            if (player?.Profile?.Inventory == null)
                return null;
            foreach (var value in player.Profile.Inventory.GetPlayerItems(EPlayerItems.InRaidItems))
                if ((item == null || value.Id == item) && (template == null || value.TemplateId.ToString() == template))
                    return value;
            return null;
        }

        private void TickOfficeMap()
        {
            if (!OfficeMapAvailable || Time.unscaledTime < _officeMapTick)
                return;
            _officeMapTick = Time.unscaledTime + .2f;
            if (_officeFiles == null)
            {
                try
                {
                    _officeFiles = JsonConvert.DeserializeObject<OfficeTerminalFile[]>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Info.Location), "Office", "terminal_files.json")));
                    if (_officeFiles == null || _officeFiles.Length < 1 || _officeFiles.Length > 20)
                        throw new InvalidDataException("Terminal files must contain 1 to 20 entries.");
                    if (IsEventAuthority)
                        _officeAccess.operations.fileCount = _officeFiles.Length;
                }
                catch (Exception ex)
                {
                    Log.LogError("ISB: office download catalogue failed: " + ex.Message);
                    _officeFiles = new OfficeTerminalFile[0];
                }
            }

            var state = _officeAccess.operations;
            if (IsEventAuthority && state.Tick(SynchronizedTime))
                OfficeMapChanged();
            ApplyOfficeAwards();
            LabsKeycardLockdown.SetOfficeEvent(true);
            if (!CanPresentLocally)
                return;
            var local = _activeRaidWorld.MainPlayer;
            if (local == null || local.HealthController?.IsAlive != true)
                return;
            if (_officeStations.Count < OfficePlantKinds.Length && Time.unscaledTime >= _officeZoneSearchAt)
            {
                _officeZoneSearchAt = Time.unscaledTime + 2f;
                var zones = Resources.FindObjectsOfTypeAll<EFT.Interactive.PlaceItemTrigger>();
                foreach (string kind in OfficePlantKinds)
                {
                    if (_officeStations.ContainsKey(kind))
                        continue;
                    var zone = Array.Find(zones, value => value != null && value.gameObject.scene.IsValid() && value.gameObject.scene.isLoaded && value.Id == OfficePlantZoneId(kind));
                    if (zone == null)
                        continue;
                    if (kind == "UPLOAD")
                    {
                        zone.transform.position = LabsOfficeScene.Point(OfficePlantPoint(kind));
                        foreach (var box in zone.GetComponents<BoxCollider>())
                        {
                            box.center = new Vector3(0f, .9f, 0f);
                            box.size = new Vector3(4f, 3.6f, 4f);
                        }
                    }

                    _officeStations[kind] = zone;
                    string selected = kind;
                    LabsTakeoverInfrastructure.RegisterEmergencyPlantZone(zone, OfficeText("Plant" + kind, "Install " + kind), () => OfficePlantAvailable(selected), owner => BeginOfficePlant(owner, selected));
                }
            }

            EFT.Interactive.PlaceItemTrigger nearby = null;
            foreach (var pair in _officeStations)
            {
                if (pair.Value == null || !pair.Value.gameObject.activeInHierarchy)
                    continue;
                Vector3 delta = local.Position - LabsOfficeScene.Point(OfficePlantPoint(pair.Key));
                float reach = pair.Key == "UPLOAD" ? 2f : 1.2f;
                if (Mathf.Abs(delta.x) <= reach && Mathf.Abs(delta.z) <= reach && Mathf.Abs(delta.y) <= 1.8f && (OfficePlantAvailable(pair.Key) || _officePlant?.Kind == pair.Key))
                {
                    nearby = pair.Value;
                    break;
                }
            }

            if (local.PlaceItemZone != nearby && (local.PlaceItemZone == null || _officeStations.ContainsValue(local.PlaceItemZone)))
                local.OnPlaceItemTriggerChanged(nearby);
            if (_officePlant?.Consumed == true && !_officePlant.Busy && Time.unscaledTime >= _officePlant.RetryAt)
            {
                _officePlant.RetryAt = Time.unscaledTime + 2f;
                SendOfficePlant("COMMIT", _officePlant);
            }
            else if (_officePlant != null && !_officePlant.Started && Time.unscaledTime - _officePlant.RequestedAt > 10f)
            {
                SendOfficePlant("CANCEL", _officePlant);
                _officePlant = null;
                OfficeNotice("OperationFailed", "Operation failed. Please try again.");
            }

            int flags = state.Actions(local.ProfileId);
            if ((flags & OfficeRaidOperations.Hack) != 0)
            {
                OfficeVisit("isb_cb_office_hack");
            }

            if (state.codeUploaded)
                OfficeVisit("isb_cb_office_uploaded");
            if (LabsKeycardLockdown.OfficeAdminPowered)
                OfficeVisit("isb_cb_office_admin_reader");
            if ((flags & OfficeRaidOperations.Code) != 0)
                OfficeVisit("isb_cb_office_local_access");
            if ((flags & OfficeRaidOperations.Shutdown) != 0)
                OfficeVisit("isb_cb_office_local_shutdown");
            if ((flags & OfficeRaidOperations.Ssd) != 0)
                OfficeVisit("isb_cb_office_local_disk");
            if ((flags & OfficeRaidOperations.Download) != 0 && state.operators.TryGetValue(local.ProfileId, out var op) && _officeAwarded.Contains(op.rewardId))
                OfficeVisit("isb_cb_office_local_download");
        }

        private void OfficeVisit(string target)
        {
            if (_officeQuestEffects.Contains(target))
                return;
            _activeRaidWorld.MainPlayer.SpecialPlaceVisited(target, 0);
            _officeQuestEffects.Add(target);
        }

        private void OfficeMapChanged()
        {
            _officeAccess.revision++;
            BroadcastOfficeState();
        }

        private bool OfficePlantAvailable(string kind)
        {
            if (!OfficeMapAvailable || DeadManActive || _officePlant != null)
                return false;
            var player = _activeRaidWorld.MainPlayer;
            return _officeAccess.operations.CanPlant(kind, player?.ProfileId, OfficeInventoryItem(player, template: OfficePlantItem(kind)) != null, kind == "HACK" && player?.Profile?.QuestsData?.Any(q => q.Id == "ca7b2ef7bca84efe967f9b40" && q.Status == EQuestStatus.Started) == true);
        }

        private void BeginOfficePlant(GamePlayerOwner owner, string kind)
        {
            if (!OfficeMapAvailable || DeadManActive || _officePlant != null || owner?.Player == null || !(owner.Player.CurrentState is IdlePlayerState))
                return;
            var item = OfficeInventoryItem(owner.Player, template: OfficePlantItem(kind));
            if (item == null)
            {
                OfficeNotice("Missing" + kind, "Required item missing.");
                return;
            }

            _officePlant = new OfficePlant
            {
                Owner = owner,
                Item = item,
                Kind = kind,
                Token = Guid.NewGuid().ToString("N"),
                Generation = _officeGeneration,
                RequestedAt = Time.unscaledTime
            };
            SendOfficePlant("RESERVE", _officePlant);
        }

        private void SendOfficePlant(string command, OfficePlant pending)
        {
            string message = "OP|" + command + "|" + pending.Owner.Player.ProfileId + "|" + pending.Kind + "|" + pending.Token + "|" + pending.Item.Id;
            if (IsEventAuthority)
                ReceiveOfficeMapPacket(message.Split('|'));
            else
                NetworkBridge.SendToServer(message);
        }

        private void ReceiveOfficeMapPacket(string[] parts)
        {
            if (!OfficeMapAvailable)
                return;
            if (parts[0] == "OPR" && parts.Length == 4 && !IsEventAuthority)
            {
                OfficePlantReply(parts[1], parts[2], parts[3]);
                return;
            }

            if (!IsEventAuthority || parts.Length != 6 || parts[0] != "OP" || !OfficeRaidOperations.Kind(parts[3]) || !Guid.TryParseExact(parts[4], "N", out _) || parts[5].Length != 24)
                return;
            var player = OfficePlayer(parts[2]);
            if (player == null || (parts[1] == "RESERVE" && !OfficePlayerNear(parts[2], OfficePlantPoint(parts[3]))))
                return;
            var state = _officeAccess.operations;
            bool changed = false, accepted = false;
            if (parts[1] == "RESERVE" && !DeadManActive && OfficeInventoryItem(player, parts[5], OfficePlantItem(parts[3])) != null)
                changed = accepted = state.Reserve(parts[3], parts[2], parts[4], parts[5], SynchronizedTime);
            else if (parts[1] == "COMMIT")
            {
                string key = parts[2] + ":" + parts[4];
                if (_officeReplies.TryGetValue(key, out var reply) && reply == "PLANTED")
                    accepted = true;
                else if (state.Commit(parts[3], parts[2], parts[4], SynchronizedTime))
                {
                    accepted = changed = true;
                    _officeReplies[key] = "PLANTED";
                    _officeReplyOrder.Enqueue(key);
                    while (_officeReplyOrder.Count > 128)
                        _officeReplies.Remove(_officeReplyOrder.Dequeue());
                }
            }
            else if (parts[1] == "CANCEL")
                changed = accepted = state.Cancel(parts[3], parts[2], parts[4]);
            if (changed)
                OfficeMapChanged();
            string result = accepted ? parts[1] : "DENIED";
            if (parts[2] == _activeRaidWorld.MainPlayer?.ProfileId)
                OfficePlantReply(parts[2], parts[4], result);
            if (IsFikaServer)
                NetworkBridge.Broadcast("OPR|" + parts[2] + "|" + parts[4] + "|" + result);
        }

        private void OfficePlantReply(string profile, string token, string command)
        {
            var pending = _officePlant;
            if (pending == null || pending.Token != token || pending.Owner.Player.ProfileId != profile || pending.Generation != _officeGeneration)
                return;
            if (command == "COMMIT")
            {
                pending.Owner.Player.Profile.ItemDroppedAtPlace(OfficePlantItem(pending.Kind), OfficePlantZoneId(pending.Kind));
                pending.Owner.CloseObjectivesPanel();
                _officePlant = null;
                OfficeNotice("Installed", "Device installed.");
                return;
            }

            if (command == "DENIED")
            {
                if (!pending.Consumed)
                {
                    pending.Owner.CloseObjectivesPanel();
                    _officePlant = null;
                }

                OfficeNotice("OperationBusy", "Operation unavailable or already in use.");
                return;
            }

            if (command != "RESERVE" || pending.Started)
                return;
            if (!(pending.Owner.Player.CurrentState is IdlePlayerState))
            {
                SendOfficePlant("CANCEL", pending);
                _officePlant = null;
                return;
            }

            pending.Started = true;
            pending.Owner.ShowObjectivesPanel(OfficeText("InstallingDevice", "Installing device {0:F1}"), 10f);
            pending.Owner.Player.CurrentManagedState.Plant(true, false, 10f, success => FinishOfficePlant(pending, success));
        }

        private async void FinishOfficePlant(OfficePlant pending, bool success)
        {
            pending.Owner.CloseObjectivesPanel();
            if (pending.Generation != _officeGeneration || !ReferenceEquals(_officePlant, pending))
                return;
            if (!success || !OfficeMapAvailable || DeadManActive || pending.Owner.Player.HealthController?.IsAlive != true || !OfficePlayerNear(pending.Owner.Player.ProfileId, OfficePlantPoint(pending.Kind)))
            {
                SendOfficePlant("CANCEL", pending);
                _officePlant = null;
                return;
            }

            pending.Busy = true;
            try
            {
                var controller = pending.Owner.Player.InventoryController;
                var result = await controller.TryRunNetworkTransaction(ItemManipulator.Remove(pending.Item, controller, true));
                if (pending.Generation != _officeGeneration)
                    return;
                if (result?.Succeed != true)
                {
                    SendOfficePlant("CANCEL", pending);
                    _officePlant = null;
                    OfficeNotice("OperationFailed", "Operation failed. Please try again.");
                    return;
                }

                pending.Consumed = true;
                pending.RetryAt = Time.unscaledTime + 2f;
                SendOfficePlant("COMMIT", pending);
            }
            catch (Exception ex)
            {
                Log.LogError("ISB: office installation inventory transaction failed: " + ex.GetBaseException().Message);
                if (!pending.Consumed)
                {
                    SendOfficePlant("CANCEL", pending);
                    _officePlant = null;
                    OfficeNotice("OperationFailed", "Operation failed. Please try again.");
                }
            }
            finally
            {
                pending.Busy = false;
            }
        }

        private void ApplyOfficeAwards()
        {
            foreach (var award in _officeAccess.operations.awards)
            {
                if (_officeAwarded.Contains(award.Key) || (_officeAwardRetry.TryGetValue(award.Key, out float retry) && Time.unscaledTime < retry))
                    continue;
                _officeAwardRetry[award.Key] = Time.unscaledTime + 3f;
                var player = OfficePlayer(award.Value);
                if (player?.InventoryController?.Inventory?.Equipment == null)
                    continue;
                try
                {
                    if (OfficeInventoryItem(player, award.Key) == null)
                    {
                        var item = Singleton<ItemFactory>.Instance.CreateItem(award.Key, "02505e86dbddb393d4bae17f", null);
                        item.SpawnedInSession = true;
                        var controller = player.InventoryController;
                        if (!_officeAccess.operations.awardLocations.TryGetValue(award.Key, out string location))
                            continue;
                        var descriptor = JsonConvert.DeserializeObject<GridItemAddressDescriptor>(location);
                        var address = Player.ToItemAddress(descriptor);
                        if (address.Failed)
                            continue;
                        var destination = address.Value;
                        if (destination == null)
                            continue;
                        var added = ItemManipulator.Add(item, destination, controller, false);
                        if (added.Failed)
                            throw new InvalidOperationException(added.Error.ToString());
                        added.Value.RaiseEvents(controller, CommandStatus.Succeed);
                    }

                    _officeAwarded.Add(award.Key);
                    _officeAwardRetry.Remove(award.Key);
                    if (player == _activeRaidWorld.MainPlayer)
                        OfficeNotice("SsdEjected", "C.A.S.S.I.E. SSD received. Extract to keep the data.");
                }
                catch (Exception ex)
                {
                    if (!_officeGrantError)
                        Log.LogError("ISB: replicated office SSD inventory grant failed: " + ex.GetBaseException().Message);
                    _officeGrantError = true;
                }
            }
        }

        private bool OfficeDownloadCommand(string profile, string command)
        {
            var state = _officeAccess.operations;
            string[] args = command.Split(':');
            if (args.Length == 3 && args[0] == "DOWNLOAD" && int.TryParse(args[1], out int file) && int.TryParse(args[2], out int disk))
                return _officeFiles?.Length == state.fileCount && state.BeginDownload(profile, file, disk, SynchronizedTime);
            if (args.Length == 2 && args[0] == "EJECT" && int.TryParse(args[1], out int expected))
            {
                if (state.disk != expected || !state.CanEject(profile))
                    return false;
                var player = OfficePlayer(profile);
                if (player?.InventoryController == null)
                    return false;
                string id = Guid.NewGuid().ToString("N").Substring(0, 24);
                var item = Singleton<ItemFactory>.Instance.CreateItem(id, "02505e86dbddb393d4bae17f", null);
                var destination = InventoryEquipmentExtension.GetPrioritizedGridsForLoot(player.InventoryController.Inventory.Equipment, item).Select(grid => grid.FindLocationForItem(item)).FirstOrDefault(address => address != null && !ItemManipulator.Add(item, address, player.InventoryController, true).Failed);
                if (destination == null)
                    return false;
                string location = JsonConvert.SerializeObject(destination.ToDescriptor());
                if (!state.Eject(profile, expected, id, location))
                    return false;
                ApplyOfficeAwards();
                return true;
            }

            return false;
        }

        private void RecordOfficeExtraction(Player player, ExitStatus status)
        {
            if (!OfficeMapAvailable || player != _activeRaidWorld.MainPlayer || player.IsAI || (status != ExitStatus.Survived && status != ExitStatus.Runner))
                return;
            if (_officeAccess.operations.CanFinishHackRaid(player.ProfileId, (_authorizationQuestActions.Mask & 2048) != 0, status == ExitStatus.Survived))
                player.SpecialPlaceVisited("isb_cb_office_hack_extraction", 0);
        }

        private void ResetOfficeMap()
        {
            _officeGeneration++;
            _officePlant?.Owner?.CloseObjectivesPanel();
            _officePlant = null;
            foreach (var control in _officeStations.Values)
                if (control != null)
                {
                    if (_activeRaidWorld?.MainPlayer?.PlaceItemZone == control)
                        _activeRaidWorld.MainPlayer.OnPlaceItemTriggerChanged(null);
                    LabsTakeoverInfrastructure.UnregisterEmergencyPlantZone(control);
                }

            _officeZoneSearchAt = 0f;
            _officeStations.Clear();
            _officeAwardRetry.Clear();
            _officeQuestEffects.Clear();
            _officeAwarded.Clear();
            _officeFiles = null;
            _officeMapTick = 0f;
            _officeGrantError = false;
            LabsKeycardLockdown.SetOfficeEvent(false);
        }
    }
}
