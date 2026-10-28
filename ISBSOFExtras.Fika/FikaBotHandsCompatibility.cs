using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Main.Players;
using Fika.Core.Main.ObservedClasses.HandsControllers;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Fika.Core.Networking.Packets.Player.Common;
using Fika.Core.Networking.Packets.Player.Common.SubPackets;
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
            _active = this;
        }

        private static void OnProceed(ObservedPlayer __instance)
        {
            _active?._pending.Remove(__instance.NetId);
        }

        private static bool Role(int role) => role >= 13701 && role <= 13716 || role >= 848420 && role <= 848424 || role == 848426;

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
            var proceed = new ProceedPacket(default, item.Value.Id, 0f, 0, EProceedType.Weapon, false);
            pending.Player.HandleProceedPacket(proceed);
        }

        internal void Reset()
        {
            _pending.Clear();
            _expired.Clear();
            _world = null;
            _nextScan = 0f;
        }

        public void Dispose()
        {
            _harmony.UnpatchSelf();
            if (ReferenceEquals(_active, this)) _active = null;
            Reset();
        }
    }
}
