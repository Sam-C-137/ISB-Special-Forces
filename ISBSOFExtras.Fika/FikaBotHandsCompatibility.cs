using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Main.Players;
using Fika.Core.Main.ObservedClasses.HandsControllers;
using Fika.Core.Main.Components;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Fika.Core.Networking.Packets.Player.Common;
using Fika.Core.Networking.Packets.Player.Common.SubPackets;
using Fika.Core.Networking.Packets.FirearmController;
using Fika.Core.Networking.Packets.Generic.SubPackets;
using HarmonyLib;
using UnityEngine;

namespace ISBSSOFExtra.Fika
{
    public struct ISBBotHandsPacket : INetSerializable
    {
        public int NetId, RequestId;
        public bool Request, Stationary;
        public string ItemId;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutUnmanaged(NetId);
            writer.PutUnmanaged(RequestId);
            writer.PutUnmanaged((byte)(Request ? 1 : 0));
            writer.PutUnmanaged((byte)(Stationary ? 1 : 0));
            writer.Put(ItemId ?? string.Empty, 24);
        }

        public void Deserialize(NetDataReader reader)
        {
            NetId = reader.GetInt();
            RequestId = reader.GetInt();
            Request = reader.GetBool();
            Stationary = reader.GetBool();
            ItemId = reader.GetString(24);
        }
    }

    internal sealed class FikaBotHandsCompatibility : IDisposable
    {
        private sealed class Pending
        {
            internal ObservedPlayer Player;
            internal float NextRequest;
            internal int RequestId;
        }

        private static FikaBotHandsCompatibility _active;
        private sealed class Buffered
        {
            internal FikaClient Client;
            internal float Until;
            internal int Bytes;
            internal readonly List<KeyValuePair<int, byte[]>> Packets = new List<KeyValuePair<int, byte[]>>();
        }
        private static readonly MethodInfo CommonHandler = AccessTools.Method(typeof(FikaClient), "OnCommonPlayerPacketReceived");
        private static readonly MethodInfo WeaponHandler = AccessTools.Method(typeof(FikaClient), "OnWeaponPacketReceived");
        private static readonly MethodInfo InventoryHandler = AccessTools.Method(typeof(FikaClient), "OnInventoryPacketReceived");
        private static readonly AccessTools.FieldRef<FikaClient, CoopHandler> Handler = AccessTools.FieldRefAccess<FikaClient, CoopHandler>("_coopHandler");
        private readonly HashSet<int> _expected = new HashSet<int>();
        private readonly Dictionary<int, Buffered> _buffered = new Dictionary<int, Buffered>();
        private bool _replaying;
        private int _replayFrame = -1, _replayedThisFrame;
        private long _replayStarted;
        private readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        private readonly List<int> _expired = new List<int>();
        private readonly Harmony _harmony = new Harmony("com.samc137.isbsofextras.fika.bot-hands");
        private GameWorld _world;
        private float _nextScan;
        private int _requestId;

        internal void Initialize()
        {
            _harmony.Patch(AccessTools.Method(typeof(ObservedPlayer), nameof(ObservedPlayer.HandleProceedPacket)),
                prefix: new HarmonyMethod(typeof(FikaBotHandsCompatibility), nameof(OnProceed)));
            _harmony.Patch(AccessTools.Method(typeof(CoopHandler), nameof(CoopHandler.QueueProfile)), prefix: new HarmonyMethod(typeof(FikaBotHandsCompatibility), nameof(OnProfile)));
            _harmony.Patch(AccessTools.Method(typeof(ObservedPlayer), nameof(ObservedPlayer.InitObservedPlayer)), postfix: new HarmonyMethod(typeof(FikaBotHandsCompatibility), nameof(OnReady)));
            _harmony.Patch(CommonHandler, prefix: new HarmonyMethod(typeof(FikaBotHandsCompatibility), nameof(OnCommon)));
            _harmony.Patch(WeaponHandler, prefix: new HarmonyMethod(typeof(FikaBotHandsCompatibility), nameof(OnWeapon)));
            _harmony.Patch(InventoryHandler, prefix: new HarmonyMethod(typeof(FikaBotHandsCompatibility), nameof(OnInventory)));
            _active = this;
        }

        private static void OnProceed(ObservedPlayer __instance)
        {
            _active?._pending.Remove(__instance.NetId);
        }

        private static bool Role(int role) => role >= 13701 && role <= 13716 || role >= 848420 && role <= 848424 || role == 848426;

        private static void OnReady(ObservedPlayer __instance)
        {
            if (_active != null && !_active._buffered.ContainsKey(__instance.NetId))
                _active._expected.Remove(__instance.NetId);
        }

        private static void OnProfile(object[] __args)
        {
            if (_active != null && _active._expected.Count < 128 && __args.Length > 3 && __args[0] is Profile profile && __args[3] is int id && Role((int)profile.Info.Settings.Role))
                _active._expected.Add(id);
        }

        private static bool OnCommon(FikaClient __instance, CommonPlayerPacket __0) => !Buffer(__instance, __0.NetId, 0, __0.Serialize);
        private static bool OnWeapon(FikaClient __instance, WeaponPacket __0) => !Buffer(__instance, __0.NetId, 1, __0.Serialize);
        private static bool OnInventory(FikaClient __instance, InventoryPacket __0) => !Buffer(__instance, __0.NetId, 2, __0.Serialize);

        private static bool Buffer(FikaClient client, int id, int kind, Action<NetDataWriter> serialize)
        {
            var active = _active;
            if (active == null || active._replaying) return false;
            var handler = Handler(client);
            bool registered = handler != null && handler.Players.TryGetValue(id, out var actor);
            bool waitingForWeapon = registered && kind != 0 && handler.Players[id] is ObservedPlayer observed && WaitingForHands(observed);
            if (!active._expected.Contains(id) && !waitingForWeapon) return false;
            if (registered && !waitingForWeapon)
            {
                active.FlushBuffered();
                return false;
            }
            if (active._expected.Count >= 128 && !active._expected.Contains(id)) return false;
            active._expected.Add(id);
            if (!active._buffered.TryGetValue(id, out Buffered buffered))
                active._buffered.Add(id, buffered = new Buffered { Client = client, Until = Time.unscaledTime + 30f });
            var writer = new NetDataWriter();
            serialize(writer);
            var bytes = writer.CopyData();
            if (buffered.Packets.Count >= 256 || buffered.Bytes + bytes.Length > 524288) return false;
            buffered.Packets.Add(new KeyValuePair<int, byte[]>(kind, bytes));
            buffered.Bytes += bytes.Length;
            return true;
        }

        private void FlushBuffered()
        {
            if (_replaying) return;
            if (_replayFrame != Time.frameCount)
            {
                _replayFrame = Time.frameCount;
                _replayedThisFrame = 0;
                _replayStarted = Stopwatch.GetTimestamp();
            }
            _replaying = true;
            try
            {
                _expired.Clear();
                foreach (var pair in _buffered)
                {
                    var buffered = pair.Value;
                    var handler = Handler(buffered.Client);
                    if (handler != null && handler.Players.ContainsKey(pair.Key))
                    {
                        while (buffered.Packets.Count > 0)
                        {
                            if (_replayedThisFrame >= 8 || _replayedThisFrame > 0 && Stopwatch.GetTimestamp() - _replayStarted >= Stopwatch.Frequency / 1000)
                                break;
                            int index = 0;
                            if (handler.Players[pair.Key] is ObservedPlayer observed && WaitingForHands(observed) && buffered.Packets[0].Key != 0)
                            {
                                index = buffered.Packets.FindIndex(packet => packet.Key == 0);
                                if (index < 0)
                                    break;
                            }
                            var saved = buffered.Packets[index];
                            buffered.Packets.RemoveAt(index);
                            _replayedThisFrame++;
                            buffered.Bytes -= saved.Value.Length;
                            var reader = new NetDataReader(saved.Value);
                            if (saved.Key == 0)
                            {
                                var packet = new CommonPlayerPacket(); packet.Deserialize(reader);
                                try { CommonHandler.Invoke(buffered.Client, new object[] { packet }); }
                                finally { packet.Clear(); }
                            }
                            else if (saved.Key == 1)
                            {
                                var packet = new WeaponPacket(); packet.Deserialize(reader);
                                try { WeaponHandler.Invoke(buffered.Client, new object[] { packet }); }
                                finally { packet.Clear(); }
                            }
                            else
                            {
                                var packet = InventoryPacket.CreateInstance(); packet.Deserialize(reader);
                                try { InventoryHandler.Invoke(buffered.Client, new object[] { packet }); }
                                finally { packet.Dispose(); }
                            }
                        }
                        if (buffered.Packets.Count == 0 || Time.unscaledTime > buffered.Until) _expired.Add(pair.Key);
                    }
                    else if (Time.unscaledTime > buffered.Until) _expired.Add(pair.Key);
                }
                foreach (int id in _expired) { _buffered.Remove(id); _expected.Remove(id); }
            }
            finally { _replaying = false; }
        }

        private static bool Eligible(FikaPlayer player)
        {
            if (player?.Profile?.Info?.Settings == null || player.HealthController?.IsAlive != true)
                return false;
            int role = (int)player.Profile.Info.Settings.Role;
            return Role(role);
        }

        private static bool WaitingForHands(ObservedPlayer player)
        {
            return Empty(player) || Eligible(player) && player.HandsController is ObservedFirearmController firearm && firearm.WeaponManager == null;
        }

        private static bool Empty(ObservedPlayer player)
        {
            return Eligible(player) && (player.HandsController == null || player.HandsController is Player.EmptyHandsController);
        }

        internal void Tick(IFikaNetworkManager manager, bool server)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (_world != world)
            {
                Reset();
                _world = world;
            }
            if (server || manager == null || world == null)
                return;
            FlushBuffered();
            if (Time.unscaledTime < _nextScan)
                return;
            float now = Time.unscaledTime;
            _nextScan = now + 0.5f;
            _expired.Clear();
            foreach (var pair in _pending)
                if (!WaitingForHands(pair.Value.Player)) _expired.Add(pair.Key);
            foreach (int id in _expired) _pending.Remove(id);
            int sent = 0;
            foreach (var actor in world.AllAlivePlayersList)
            {
                if (!(actor is ObservedPlayer player) || !WaitingForHands(player))
                    continue;
                if (!_pending.TryGetValue(player.NetId, out Pending pending))
                {
                    if (_pending.Count >= 128) continue;
                    _pending.Add(player.NetId, new Pending { Player = player, NextRequest = now + 2f });
                    continue;
                }
                if (now < pending.NextRequest || sent >= 4)
                    continue;
                pending.NextRequest = now + 5f;
                pending.RequestId = ++_requestId;
                var packet = new ISBBotHandsPacket { NetId = player.NetId, RequestId = pending.RequestId, Request = true };
                manager.SendData(ref packet, DeliveryMethod.ReliableOrdered);
                sent++;
            }
        }

        internal void Receive(ISBBotHandsPacket packet, NetPeer peer, IFikaNetworkManager manager, bool server)
        {
            if (packet.Request != server || packet.RequestId == 0 || !Singleton<GameWorld>.Instantiated)
                return;
            if (server)
            {
                if (!(manager is FikaServer host) || peer == null) return;
                foreach (var actor in Singleton<GameWorld>.Instance.AllAlivePlayersList)
                {
                    if (!(actor is FikaBot bot) || bot.NetId != packet.NetId || !Eligible(bot)) continue;
                    var weapon = (bot.HandsController as Player.FirearmController)?.Item;
                    packet.Request = false;
                    packet.ItemId = weapon?.Id.ToString() ?? string.Empty;
                    packet.Stationary = weapon?.IsStationaryWeapon == true;
                    host.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
                    return;
                }
                return;
            }
            if (!_pending.TryGetValue(packet.NetId, out Pending pending) || pending.RequestId != packet.RequestId)
                return;
            pending.RequestId = 0;
            if (!WaitingForHands(pending.Player) || string.IsNullOrEmpty(packet.ItemId) || packet.Stationary)
                return;
            var item = pending.Player.FindItemById(new MongoID(packet.ItemId), false, false);
            if (!item.Succeeded || !(item.Value is Weapon))
                return;
            var proceed = ProceedPacket.CreateInstance();
            proceed.ItemId = item.Value.Id;
            proceed.ProceedType = EProceedType.Weapon;
            pending.Player.HandleProceedPacket(proceed);
        }

        internal void Reset()
        {
            _buffered.Clear();
            _expected.Clear();
            _pending.Clear();
            _expired.Clear();
            _world = null;
            _nextScan = 0f;
            _replayFrame = -1;
            _replayedThisFrame = 0;
        }

        public void Dispose()
        {
            _harmony.UnpatchSelf();
            if (ReferenceEquals(_active, this)) _active = null;
            Reset();
        }
    }
}
