using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;
using BatteryDamageInfo = EFT.Ballistics.DamageInfo;

namespace ISBSSOFExtra
{
    internal static partial class LabsKeycardLockdown
    {
        private const string BatteryTemplate = "5e2aedd986f7746d404f3aa4";
        private const string ModifiedBatteryTemplate = "4d57eab7724cdd35474d84eb";
        private const string ToolsetTemplate = "590c2e1186f77425357b6124";
        private const int BatteryCharges = 5;
        private const float InstallSeconds = 8f;
        private sealed class BatteryInstallation
        {
            internal GamePlayerOwner Owner;
            internal WorldInteractiveObject Door;
            internal Item Battery;
            internal string Token;
            internal float RequestedAt, PlantingStartedAt;
            internal int Generation;
            internal bool Planting, Consuming, Modified, ArcRequested;
        }

        private sealed class Reservation
        {
            internal string Token, Player;
            internal float Expires, Started;
            internal bool ArcStarted;
        }

        private static readonly Dictionary<string, KeycardDoor> Readers = new Dictionary<string, KeycardDoor>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Reservation> Reservations = new Dictionary<string, Reservation>(StringComparer.Ordinal);
        private static readonly HashSet<string> PoweredReaders = new HashSet<string>(StringComparer.Ordinal);
        private static Func<bool> _authority;
        private static Action<string> _sendBattery, _broadcastBattery;
        private static Action _readerRepaired;
        private static BatteryInstallation _installation;
        private static int _batteryGeneration;
        internal static void ConfigureBatteryNetwork(Func<bool> authority, Action<string> send, Action<string> broadcast, Action readerRepaired)
        {
            _authority = authority;
            _sendBattery = send;
            _broadcastBattery = broadcast;
            _readerRepaired = readerRepaired;
        }

        private static void IndexBatteryReaders()
        {
            Readers.Clear();
            foreach (var door in Doors)
                RememberReader(door);
        }

        private static void RememberReader(WorldInteractiveObject door)
        {
            if (door is KeycardDoor reader && reader != null && !string.IsNullOrEmpty(reader.Id) && !reader.Id.Contains("|") && reader.Id.Length < 160)
                Readers[reader.Id] = reader;
        }

        private static Item FindBattery(Player player, bool modified = false, bool office = false) => player?.Profile?.Inventory?.GetPlayerItems(EPlayerItems.Equipment).FirstOrDefault(item => item.TemplateId.ToString() == (office ? OfficeBatteryTemplate : modified ? ModifiedBatteryTemplate : BatteryTemplate) && item.StackObjectsCount > 0 && (!modified || HasCharge(item)));
        private static bool HasCharge(Item item)
        {
            var key = item?.GetItemComponent<KeyComponent>();
            return item != null && item.StackObjectsCount > 0 && key != null && key.NumberOfUsages >= 0 && key.NumberOfUsages < (item.TemplateId.ToString() == OfficeBatteryTemplate ? 2 : BatteryCharges);
        }

        private static bool HasToolset(Player player) => player?.Profile?.Inventory?.GetPlayerItems(EPlayerItems.Equipment).Any(item => item.TemplateId.ToString() == ToolsetTemplate) == true;
        private static bool CanUseBattery(Player player, Item battery, WorldInteractiveObject door) => battery != null && battery.StackObjectsCount > 0 && player?.Profile?.Inventory?.GetPlayerItems(EPlayerItems.Equipment).Any(item => ReferenceEquals(item, battery)) == true && (IsOfficeReader(door) ? battery.TemplateId.ToString() == OfficeBatteryTemplate && HasCharge(battery) && HasToolset(player) : battery.TemplateId.ToString() == BatteryTemplate || battery.TemplateId.ToString() == ModifiedBatteryTemplate && HasCharge(battery) && HasToolset(player));
        private static void InstallBattery(GamePlayerOwner owner, WorldInteractiveObject door, bool modified = false)
        {
            if (_installation != null || owner?.Player == null || !IsBlocked(door) || !Readers.ContainsKey(door.Id))
                return;
            if (!(owner.Player.CurrentState is IdlePlayerState))
                return;
            var battery = FindBattery(owner.Player, modified, IsOfficeReader(door));
            if (!CanUseBattery(owner.Player, battery, door))
                return;
            _installation = new BatteryInstallation
            {
                Owner = owner,
                Door = door,
                Battery = battery,
                Modified = modified,
                Token = Guid.NewGuid().ToString("N"),
                RequestedAt = Time.realtimeSinceStartup,
                Generation = _batteryGeneration
            };
            SendBattery("RESERVE", door.Id, _installation.Token);
        }

        private static void SendBattery(string command, string id, string token)
        {
            string payload = "W|" + command + "|" + id + "|" + token;
            if (_authority?.Invoke() == true)
                ReceiveBattery(payload.Split('|'));
            else
                _sendBattery?.Invoke(payload);
        }

        private static void PublishBattery(string command, string id, string token)
        {
            string payload = "W|" + command + "|" + id + "|" + token;
            ApplyBatteryReply(command, id, token);
            _broadcastBattery?.Invoke(payload);
        }

        internal static void ReceiveBattery(string[] parts)
        {
            if (parts == null || (parts.Length != 4 && parts.Length != 7 && parts.Length != 8) || parts[2].Length > 160 || parts[3].Length != 32 || !Guid.TryParseExact(parts[3], "N", out _) || !Readers.TryGetValue(parts[2], out var door))
                return;
            string command = parts[1], id = parts[2], token = parts[3];
            if (_authority?.Invoke() != true)
            {
                ApplyBatteryCharge(parts);
                ApplyBatteryReply(command, id, token);
                return;
            }

            string profile = ExtrasNetworkBridgeRegistry.Current.ReceivedPlayerId;
            Player actor = string.IsNullOrEmpty(profile) ? Singleton<GameWorld>.Instance?.MainPlayer : Singleton<GameWorld>.Instance?.AllAlivePlayersList.FirstOrDefault(player => player.ProfileId == profile);
            Reservations.TryGetValue(id, out var reservation);
            string owner = string.IsNullOrEmpty(profile) ? actor?.ProfileId : profile;
            if (command == "CANCEL")
            {
                if (reservation?.Token == token && reservation.Player == owner)
                {
                    Reservations.Remove(id);
                    PublishBattery("CANCELLED", id, token);
                }
                return;
            }
            if (actor == null || actor.IsAI || actor.HealthController?.IsAlive != true || (actor.Position - door.transform.position).sqrMagnitude > 16f) return;
            if (command != "RESERVE" && (reservation == null || reservation.Player != actor.ProfileId)) return;
            if (command == "RESERVE")
            {
                if (reservation?.Token == token && reservation.Player == actor.ProfileId && reservation.Expires > Time.realtimeSinceStartup)
                {
                    PublishBattery("GRANTED", id, token);
                    return;
                }
                if ((!CanUseBattery(actor, FindBattery(actor, false, IsOfficeReader(door)), door) && !CanUseBattery(actor, FindBattery(actor, true, IsOfficeReader(door)), door)) || !IsBlocked(door) || (reservation != null && reservation.Expires > Time.realtimeSinceStartup && reservation.Token != token))
                {
                    PublishBattery("DENIED", id, token);
                    return;
                }

                Reservations[id] = new Reservation
                {
                    Token = token,
                    Player = actor.ProfileId,
                    Started = Time.realtimeSinceStartup,
                    Expires = Time.realtimeSinceStartup + 90f
                };
                PublishBattery("GRANTED", id, token);
            }
            else if ((command == "ARC" || command == "ARC_SHORT") && parts.Length == 4 && reservation?.Token == token && reservation.Expires > Time.realtimeSinceStartup && !reservation.ArcStarted)
            {
                reservation.ArcStarted = true;
                PublishBattery(command, id, token);
            }
            else if ((command == "COMMIT" || command == "COMMIT_MODIFIED") && (!IsOfficeReader(door) || command == "COMMIT_MODIFIED") && reservation?.Token == token && reservation.Expires > Time.realtimeSinceStartup && !PoweredReaders.Contains(id) && Time.realtimeSinceStartup - reservation.Started >= InstallSeconds - .5f)
            {
                Reservations.Remove(id);
                ApplyBatteryCharge(parts);
                parts[1] = command == "COMMIT_MODIFIED" || parts.Length == 7 ? "POWERED_MODIFIED" : "POWERED";
                ApplyBatteryReply(parts[1], id, token);
                _readerRepaired?.Invoke();
                _broadcastBattery?.Invoke(string.Join("|", parts));
            }
            else if (command == "CANCEL" && reservation?.Token == token)
            {
                Reservations.Remove(id);
                PublishBattery("CANCELLED", id, token);
            }
        }

        private static void ApplyBatteryReply(string command, string id, string token)
        {
            if ((command == "ARC" || command == "ARC_SHORT") && Readers.TryGetValue(id, out var arcReader) && !PoweredReaders.Contains(id))
                ReaderBatteryEffects.Play(arcReader, command == "ARC", token);
            if (command == "CANCELLED" || command == "POWERED_MODIFIED")
                ReaderBatteryEffects.Stop(token);
            bool powered = command == "POWERED" || command == "POWERED_MODIFIED";
            if (powered)
            {
                if (PoweredReaders.Add(id) && command == "POWERED" && Readers.TryGetValue(id, out var reader))
                    ReaderBatteryEffects.Play(reader, false, token, 1f);
                Singleton<GameWorld>.Instance?.MainPlayer?.ForceInteractionsChanged();
            }

            var pending = _installation;
            if (pending == null || pending.Generation != _batteryGeneration || pending.Token != token || pending.Door?.Id != id)
                return;
            if (command == "DENIED")
            {
                _installation = null;
                pending.Owner.DisplayPreloaderUiNotification(Text("isb_reader_unavailable", "Card Reader unavailable."));
            }
            else if (powered)
            {
                _installation = null;
                if (!pending.Modified)
                    ApplyBatteryShock(pending.Owner?.Player);
                pending.Owner.DisplayPreloaderUiNotification(Text("isb_reader_powered", "Card Reader activated."));
            }
            else if (command == "GRANTED" && !pending.Planting && !pending.Consuming)
            {
                if (!IsBlocked(pending.Door) || !CanUseBattery(pending.Owner?.Player, pending.Battery, pending.Door) || pending.Owner?.Player == null || !(pending.Owner.Player.CurrentState is IdlePlayerState))
                {
                    _installation = null;
                    SendBattery("CANCEL", id, token);
                    return;
                }

                pending.Planting = true;
                pending.PlantingStartedAt = Time.realtimeSinceStartup;
                pending.Owner.ShowObjectivesPanel(Text("isb_reader_installing", "Installing battery {0:F1}"), InstallSeconds);
                pending.Owner.Player.CurrentManagedState.Plant(true, false, InstallSeconds, success => FinishBattery(pending, success));
            }
        }

        private static void FinishBattery(BatteryInstallation pending, bool success)
        {
            pending.Owner?.CloseObjectivesPanel();
            if (pending.Generation != _batteryGeneration || !ReferenceEquals(_installation, pending))
                return;
            pending.Planting = false;
            if (!success || !IsBlocked(pending.Door) || pending.Owner?.Player?.HealthController?.IsAlive != true)
            {
                _installation = null;
                SendBattery("CANCEL", pending.Door.Id, pending.Token);
                return;
            }

            pending.Consuming = true;
            _ = ConsumeBattery(pending);
        }

        private static async Task ConsumeBattery(BatteryInstallation pending)
        {
            bool consumed = false, stackConsumed = false;
            try
            {
                var controller = pending.Owner.Player.InventoryController;
                if (!CanUseBattery(pending.Owner.Player, pending.Battery, pending.Door) || !IsBlocked(pending.Door))
                    return;
                var charge = pending.Modified ? pending.Battery.GetItemComponent<KeyComponent>() : null;
                bool retained = charge != null && charge.NumberOfUsages < (pending.Battery.TemplateId.ToString() == OfficeBatteryTemplate ? 2 : BatteryCharges) - 1;
                if (retained)
                {
                    charge.NumberOfUsages++;
                    consumed = true;
                    RefreshBattery(pending.Owner.Player, pending.Battery);
                }
                else
                {
                    if (pending.Battery.StackObjectsCount > 1)
                    {
                        consumed = stackConsumed = ConsumeBatteryStack(pending.Battery);
                        if (consumed)
                            RefreshBattery(pending.Owner.Player, pending.Battery);
                    }
                    else
                    {
                        var result = await controller.TryRunNetworkTransaction(ItemManipulator.Remove(pending.Battery, controller, true));
                        consumed = result != null && result.Succeed;
                    }
                }

                if (pending.Generation != _batteryGeneration)
                    return;
                if (consumed && (retained || stackConsumed))
                    CommitBatteryState(pending, charge?.NumberOfUsages ?? 0);
                else if (consumed)
                    SendBattery(pending.Modified ? "COMMIT_MODIFIED" : "COMMIT", pending.Door.Id, pending.Token);
                else
                    pending.Owner.DisplayPreloaderUiNotification(Text("isb_reader_battery_failed", "Battery installation failed."));
            }
            catch (Exception ex)
            {
                _batteryLog?.LogError("ISB SOF: Battery inventory transaction failed: " + ex.GetBaseException().Message);
            }
            finally
            {
                if (!consumed && pending.Generation == _batteryGeneration)
                {
                    _installation = null;
                    SendBattery("CANCEL", pending.Door.Id, pending.Token);
                }
            }
        }

        private static void ApplyBatteryShock(Player player)
        {
            if (player == null || player.IsAI || player.HealthController?.IsAlive != true || player.ActiveHealthController == null)
                return;
            try
            {
                var info = new BatteryDamageInfo
                {
                    DamageType = EDamageType.Explosion,
                    Damage = 10f
                };
                player.ActiveHealthController.ApplyDamage(EBodyPart.LeftArm, 10f, info);
                if (player.HealthController.IsAlive)
                    player.ActiveHealthController.ApplyDamage(EBodyPart.RightArm, 10f, info);
            }
            catch (Exception ex)
            {
                _batteryLog?.LogError("ISB SOF: Battery short-circuit damage failed: " + ex.GetBaseException().Message);
            }
        }

        private static void RefreshBattery(Player player, Item item)
        {
            try
            {
                var controller = player.InventoryController;
                controller.RaiseEvent(new RefreshItemEventArgs(item, controller, true, false));
            }
            catch (Exception ex)
            {
                _batteryLog?.LogWarning("ISB SOF: Battery inventory refresh failed: " + ex.GetBaseException().Message);
            }
        }

        private static void CommitBatteryState(BatteryInstallation pending, int used)
        {
            string payload = "W|" + (pending.Modified ? "COMMIT_MODIFIED" : "COMMIT") + "|" + pending.Door.Id + "|" + pending.Token + "|" + pending.Owner.Player.ProfileId + "|" + pending.Battery.Id + "|" + used + "|" + pending.Battery.StackObjectsCount;
            if (_authority?.Invoke() == true)
                ReceiveBattery(payload.Split('|'));
            else
                _sendBattery?.Invoke(payload);
        }

        private static int BatteryChargeLimit(Item item) => item?.TemplateId.ToString() == OfficeBatteryTemplate ? 2 : item?.TemplateId.ToString() == ModifiedBatteryTemplate ? BatteryCharges : item?.TemplateId.ToString() == BatteryTemplate ? 1 : 0;
        private static bool ConsumeBatteryStack(Item item)
        {
            int limit = BatteryChargeLimit(item);
            var key = item?.GetItemComponent<KeyComponent>();
            if (limit == 0 || item.StackObjectsCount <= 1 || limit > 1 && (key == null || key.NumberOfUsages != limit - 1))
                return false;
            item.StackObjectsCount--;
            if (key != null)
                key.NumberOfUsages = 0;
            return true;
        }

        private static bool ApplyBatteryInventoryState(Item item, int used, int count)
        {
            int limit = BatteryChargeLimit(item);
            var key = item?.GetItemComponent<KeyComponent>();
            if (limit == 0 || count < 1 || count > item.StackObjectsCount || used < 0 || used >= limit || limit > 1 && key == null)
                return false;
            if (count == item.StackObjectsCount && (key == null || used <= key.NumberOfUsages))
                return false;
            item.StackObjectsCount = count;
            if (key != null)
                key.NumberOfUsages = used;
            return true;
        }

        private static void ApplyBatteryCharge(string[] parts)
        {
            if ((parts.Length != 7 && parts.Length != 8) || (parts[1] != "COMMIT" && parts[1] != "COMMIT_MODIFIED" && parts[1] != "POWERED" && parts[1] != "POWERED_MODIFIED") || parts[4].Length > 64 || parts[5].Length > 64 || !int.TryParse(parts[6], out int used) || used < 0 || used >= BatteryCharges || parts.Length == 7 && used == 0)
                return;
            int count = 0;
            if (parts.Length == 8 && (!int.TryParse(parts[7], out count) || count < 1))
                return;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.ProfileId != parts[4])
                    continue;
                var item = player.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment).FirstOrDefault(value => value.Id.ToString() == parts[5] && BatteryChargeLimit(value) > 0);
                if (ApplyBatteryInventoryState(item, used, parts.Length == 8 ? count : item?.StackObjectsCount ?? 0))
                    RefreshBattery(player, item);
                return;
            }
        }

        private static string Text(string key, string fallback) => ISB.Compatibility.ISBLocalizedText.Get(key, fallback);
        private static BepInEx.Logging.ManualLogSource _batteryLog;
        internal static void TickBattery()
        {
            ReaderBatteryEffects.Tick();
            var pending = _installation;
            if (pending?.Planting == true && !pending.ArcRequested && pending.Generation == _batteryGeneration && Time.realtimeSinceStartup - pending.PlantingStartedAt >= InstallSeconds - 2f && pending.Owner?.Player?.HealthController?.IsAlive == true && IsBlocked(pending.Door))
            {
                pending.ArcRequested = true;
                SendBattery(pending.Modified ? "ARC" : "ARC_SHORT", pending.Door.Id, pending.Token);
            }

            if (pending == null || pending.Planting || pending.Consuming || Time.realtimeSinceStartup - pending.RequestedAt < 10f)
                return;
            _installation = null;
            SendBattery("CANCEL", pending.Door.Id, pending.Token);
            pending.Owner?.DisplayPreloaderUiNotification(Text("isb_reader_unavailable", "Card Reader unavailable."));
        }

        internal static string BatterySnapshot() => JsonConvert.SerializeObject(PoweredReaders);
        internal static void ApplyBatterySnapshot(string json)
        {
            if (_authority?.Invoke() == true || string.IsNullOrEmpty(json) || json.Length > 32768)
                return;
            try
            {
                foreach (string id in JsonConvert.DeserializeObject<string[]>(json) ?? Array.Empty<string>())
                    if (id != null && Readers.ContainsKey(id))
                        PoweredReaders.Add(id);
                Singleton<GameWorld>.Instance?.MainPlayer?.ForceInteractionsChanged();
            }
            catch (JsonException)
            {
            }
        }

        private static void ResetBattery()
        {
            _batteryGeneration++;
            ReaderBatteryEffects.Reset();
            var pending = _installation;
            _installation = null;
            if (pending?.Planting == true && pending.Owner?.Player != null)
            {
                pending.Owner.Player.CurrentManagedState.Plant(false, false, 0f, null);
                pending.Owner.CloseObjectivesPanel();
            }

            Readers.Clear();
            Reservations.Clear();
            PoweredReaders.Clear();
        }
    }
}
