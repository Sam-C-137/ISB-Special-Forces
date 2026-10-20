
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using ISBSSOFExtra.Fika;
using UnityEngine;

public static class Tests
{
    public static int Passed;
    public static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        Passed++;
    }
}

public static class Program
{
    public static void Main()
    {
        var clock = new FikaAuthorityClock();
        clock.Receive(FikaAuthorityClock.Stamp("R|radio", 100), 1000.025, out var payload);
        Tests.Check(clock.Ready && payload == "R|radio", "authoritative clock handshake");
        Tests.Check(Math.Abs(clock.Time(1000.025) - 100) < .000001, "process uptime compensation");
        ClockTests.Run();
        QueueTests();
        Tests.Check(ISBSpecialForces.Behavior.ISBNavigationBudget.TryBeginSearch(10), "first navigation search");
        Tests.Check(ISBSpecialForces.Behavior.ISBNavigationBudget.TryBeginSearch(10), "second navigation search");
        Tests.Check(!ISBSpecialForces.Behavior.ISBNavigationBudget.TryBeginSearch(10), "navigation frame budget");
        Tests.Check(ISBSpecialForces.Behavior.ISBNavigationBudget.TryBeginSearch(11), "navigation next frame");
        CoverTests.Run();
        var battery = BatteryProbe.Run();
        var hands = HandsProbe.Run();
        var audio = ISBSSOFExtra.ISBSpecialForcesAudio.RunPreparationTests();
        Console.WriteLine(JsonSerializer.Serialize(new { passed = Tests.Passed, battery, hands, audio, testKind = "Original queue/clock/navigation/audio sources and extracted battery/hands methods with controlled runtime fixtures" }));
    }

    private static void QueueTests()
    {
        var queue = new FikaEventQueue<string>();
        for (int i = 0; i < 600; i++) queue.Enqueue("ACK" + i, null);
        for (int i = 0; i < 600; i++)
            Tests.Check(queue.TryDequeue(out var ack) && ack == "ACK" + i, "critical event order " + i);
        Tests.Check(!queue.TryDequeue(out _), "critical queue emptied");
        for (int i = 0; i < 1000; i++) queue.Enqueue("S" + i, "scenery");
        Tests.Check(queue.TryDequeue(out var latest) && latest == "S999" && !queue.TryDequeue(out _), "replaceable state coalesced");
        queue.Enqueue("S1000", "scenery");
        Tests.Check(queue.TryDequeue(out latest) && latest == "S1000", "dequeued coalescing node removed");
        queue.Enqueue("before", "scenery");
        queue.Enqueue("transaction", null);
        queue.Enqueue("after", "scenery");
        Tests.Check(queue.TryDequeue(out latest) && latest == "before", "state before transaction retained");
        Tests.Check(queue.TryDequeue(out latest) && latest == "transaction", "transaction stays between states");
        Tests.Check(queue.TryDequeue(out latest) && latest == "after", "state after transaction retained");
        Tests.Check(FikaEventQueue<string>.StateKey("SC173|S|{\"config\":null,\"victim\":null,\"sound\":null}", null) == "entity", "entity position can be coalesced");
        Tests.Check(FikaEventQueue<string>.StateKey("SC173|S|{\"config\":{},\"victim\":null,\"sound\":null}", null) == null, "initial entity configuration retained");
        Tests.Check(FikaEventQueue<string>.StateKey("SC173|S|{\"config\":null,\"victim\":\"human\",\"sound\":null}", null) == null, "entity attack retained");
        Tests.Check(FikaEventQueue<string>.StateKey("SC173|S|{\"config\":null,\"victim\":null,\"sound\":\"alarm\"}", null) == null, "entity sound retained");
        Tests.Check(FikaEventQueue<string>.StateKey("SC173|O|{}", "one") != FikaEventQueue<string>.StateKey("SC173|O|{}", "two"), "observer identities separated");
        queue.Enqueue("old", null); queue.Clear();
        Tests.Check(!queue.TryDequeue(out _), "raid reset clears pending messages");
    }
}

public static class BatteryProbe {
    sealed class Owner { public Player Player=new Player(); public void DisplayPreloaderUiNotification(string _){} }
    sealed class Player { public Health HealthController=new Health(); }
    sealed class Health { public bool IsAlive=true; }
    sealed class Door { public string Id="reader"; }
    sealed class BatteryInstallation {
        public bool Planting,Consuming,ArcRequested,Modified;
        public int Generation=1;
        public float PlantingStartedAt,RequestedAt,RetryAt;
        public Owner Owner=new Owner(); public Door Door=new Door(); public string Token="token",CommitPayload;
    }
    static BatteryInstallation _installation;
    static int _batteryGeneration=1;
    const float InstallSeconds=8;
    static Func<bool> _authority=()=>false;
    static Dictionary<string,object> Readers=new Dictionary<string,object>{{"reader",new object()}};
    static HashSet<string> PoweredReaders=new HashSet<string>();
    static bool IsBlocked(Door _) => true;
    static void SendBattery(string a,string b,string c) { Sends++; }
    static int Sends;
    static Action<string> _sendBattery = payload => Sends++;
    static void ReceiveBattery(string[] _) {}
    static void ApplyBatteryReply(string command, string id, string token) { if (_installation?.Token == token) _installation=null; }
    static string Text(string a,string b)=>b;
    public static object Run()
    {
        Time.realtimeSinceStartup=1000;
        _installation=new BatteryInstallation { Consuming=true,CommitPayload="W|COMMIT|reader|token" };
        TickBattery();
        Tests.Check(Sends==1 && _installation!=null, "lost battery ack retries commit");
        TickBattery();
        Tests.Check(Sends==1, "battery retry is rate limited");
        Time.realtimeSinceStartup+=2;
        TickBattery();
        Tests.Check(Sends==2, "battery retries until confirmation");
        ApplyBatterySnapshot("[\"reader\"]");
        Tests.Check(PoweredReaders.Contains("reader") && _installation==null, "snapshot completes pending battery installation");
        TickBattery();
        Tests.Check(Sends==2, "completed installation cannot retry or consume twice");
        PoweredReaders.Clear();
        _installation=new BatteryInstallation { Consuming=true };
        TickBattery();
        Tests.Check(Sends==2 && _installation!=null, "pending inventory transaction is not committed early");
        _installation=new BatteryInstallation();
        TickBattery();
        Tests.Check(_installation==null, "ungranted reservation still times out");
        return new { commitRetries=2,snapshotClearsPending=true,noEarlyCommit=true };
    }
    internal static void TickBattery()
        {
            ReaderBatteryEffects.Tick();
            ReconcileBatteryInstallation();
            var pending = _installation;
            if (pending?.Consuming == true && pending.CommitPayload != null && Time.realtimeSinceStartup >= pending.RetryAt)
            {
                SendBatteryCommit(pending, pending.CommitPayload);
                return;
            }
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
private static void SendBatteryCommit(BatteryInstallation pending, string payload)
        {
            pending.CommitPayload = payload;
            pending.RetryAt = Time.realtimeSinceStartup + 2f;
            if (_authority?.Invoke() == true)
                ReceiveBattery(payload.Split('|'));
            else
                _sendBattery?.Invoke(payload);
        }
private static void ReconcileBatteryInstallation()
        {
            var pending = _installation;
            if (pending?.Consuming == true && pending.CommitPayload != null && pending.Door != null && PoweredReaders.Contains(pending.Door.Id))
                ApplyBatteryReply(pending.Modified ? "POWERED_MODIFIED" : "POWERED", pending.Door.Id, pending.Token);
        }
    internal static void ApplyBatterySnapshot(string json)
        {
            if (_authority?.Invoke() == true || string.IsNullOrEmpty(json) || json.Length > 32768)
                return;
            try
            {
                foreach (string id in JsonConvert.DeserializeObject<string[]>(json) ?? Array.Empty<string>())
                    if (id != null && Readers.ContainsKey(id))
                        PoweredReaders.Add(id);
                ReconcileBatteryInstallation();
                Singleton<GameWorld>.Instance?.MainPlayer?.ForceInteractionsChanged();
            }
            catch (JsonException)
            {
            }
        }
}
static class ReaderBatteryEffects { public static void Tick(){} }
static class Singleton<T> where T:new() { public static T Instance=new T(); }
class GameWorld { public MainPlayer MainPlayer=new MainPlayer(); }
class MainPlayer { public void ForceInteractionsChanged(){} }

public class HandsProbe {
    class Buffered {
        internal FikaClient Client;
        internal float Until;
        internal int Bytes;
        internal readonly List<KeyValuePair<int,byte[]>> Packets=new List<KeyValuePair<int,byte[]>>();
    }
    readonly Dictionary<int,Buffered> _buffered=new Dictionary<int,Buffered>();
    readonly HashSet<int> _expected=new HashSet<int>();
    readonly List<int> _expired=new List<int>();
    bool _replaying;
    int _replayFrame=-1,_replayedThisFrame;
    long _replayStarted;
    static readonly MethodInfo CommonHandler=typeof(FikaClient).GetMethod(nameof(FikaClient.Common));
    static readonly MethodInfo WeaponHandler=typeof(FikaClient).GetMethod(nameof(FikaClient.Weapon));
    static readonly MethodInfo InventoryHandler=typeof(FikaClient).GetMethod(nameof(FikaClient.Inventory));
    static HandlerState Handler(FikaClient client)=>client.State;
    static bool WaitingForHands(ObservedPlayer player)=>player.WaitingForWeaponManager;
    public static object Run()
    {
        Time.unscaledTime=0;Time.frameCount=0;
        var c=new HandsProbe();var client=new FikaClient();
        client.State.Players.Add(7,new ObservedPlayer());
        var b=new Buffered {Client=client,Until=30};
        b.Packets.Add(new KeyValuePair<int,byte[]>(1,new byte[]{1}));
        b.Packets.Add(new KeyValuePair<int,byte[]>(0,new byte[]{0}));
        c._buffered.Add(7,b);c._expected.Add(7);
        c.FlushBuffered();
        Tests.Check(client.Commons==1, "control packet passes blocked weapon");
        for(int i=0;i<4 && c._buffered.Count>0;i++){Time.frameCount++;c.FlushBuffered();}
        Tests.Check(client.Weapons==1 && c._buffered.Count==0, "weapon resumes after control");
        b=new Buffered {Client=client,Until=30};
        for(int i=0;i<40;i++)b.Packets.Add(new KeyValuePair<int,byte[]>(1,new byte[]{1}));
        c._buffered.Add(7,b);
        int frames=0;
        while(c._buffered.Count>0 && frames<60)
        {
            int before=client.Weapons;Time.frameCount++;c.FlushBuffered();
            Tests.Check(client.Weapons-before<=8, "hands replay frame budget");frames++;
        }
        Tests.Check(client.Weapons==41 && c._buffered.Count==0, "all buffered weapons eventually replayed");
        return new { controlsUnblockWeapon=true,replayedWeapons=client.Weapons,maxPacketsPerFrame=8 };
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
}
public class ObservedPlayer { public bool WaitingForWeaponManager=true; }
public class HandlerState { public Dictionary<int,object> Players=new Dictionary<int,object>(); }
public class FikaClient {
    public HandlerState State=new HandlerState();public int Commons,Weapons;
    public void Common(CommonPlayerPacket _) { Commons++;((ObservedPlayer)State.Players[7]).WaitingForWeaponManager=false; }
    public void Weapon(WeaponPacket _){Weapons++;}
    public void Inventory(InventoryPacket _){}
}
public class NetDataReader { public NetDataReader(byte[] _){} }
public class CommonPlayerPacket {public void Deserialize(NetDataReader _){} public void Clear(){}}
public class WeaponPacket {public void Deserialize(NetDataReader _){} public void Clear(){}}
public class InventoryPacket {public static InventoryPacket CreateInstance()=>new InventoryPacket();public void Deserialize(NetDataReader _){} public void Dispose(){}}
namespace UnityEngine { public static class Time {public static float unscaledTime,realtimeSinceStartup,time;public static int frameCount;} }
public class JsonException : Exception {}
public static class JsonConvert {public static T DeserializeObject<T>(string s)=>JsonSerializer.Deserialize<T>(s);}

namespace UnityEngine { public class AudioClip { public bool Ready; } }

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private sealed class AudioHealth { public bool IsAlive = true; }
        private sealed class AudioPlayer { public bool IsAI, IsYourPlayer; public string ProfileId = "human"; public AudioHealth HealthController = new AudioHealth(); }
        private sealed class AudioWorld { public List<AudioPlayer> AllAlivePlayersList = new List<AudioPlayer> { new AudioPlayer() }; }
        private sealed class AudioNetwork
        {
            public bool IsServer = true;
            public string ReceivedPlayerId = "human";
            public readonly List<string> Sent = new List<string>();
            public bool Broadcast(string p) { Sent.Add(p); return true; }
            public bool SendToServer(string p) { Sent.Add(p); return true; }
        }
        private bool _raidisActive = true;
        private bool IsFikaNetworkSession => true;
        private bool IsFikaServer => NetworkBridge.IsServer;
        private bool IsEventAuthority => NetworkBridge.IsServer;
        private bool CanPresentLocally => !NetworkBridge.IsServer;
        private int _audioGeneration = 1, _resolves;
        private readonly AudioNetwork NetworkBridge = new AudioNetwork();
        private readonly AudioWorld _activeRaidWorld = new AudioWorld();
        private readonly HashSet<string> _fikaReadyPlayers = new HashSet<string> { "human" };
        private readonly List<System.Collections.IEnumerator> _routines = new List<System.Collections.IEnumerator>();
        private bool _resolveSuccess = true;
        private object StartCoroutine(System.Collections.IEnumerator routine) { _routines.Add(routine); return routine; }
        private static string EncodeNetworkString(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        private static string DecodeNetworkString(string s) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
        private static bool IsAudioClipReady(AudioClip clip) => clip != null && clip.Ready;
        private System.Collections.IEnumerator ResolveNetworkClip(string name, string category, Action<AudioClip> resolved)
        {
            _resolves++;
            yield return null;
            resolved?.Invoke(_resolveSuccess ? new AudioClip { Ready = true } : null);
        }
        private void FinishRoutines()
        {
            foreach (var routine in _routines.ToArray()) Drain(routine);
            _routines.Clear();
        }
        private static void Drain(System.Collections.IEnumerator routine)
        {
            while (routine.MoveNext())
                if (routine.Current is System.Collections.IEnumerator nested) Drain(nested);
        }
        public static object RunPreparationTests()
        {
            Time.realtimeSinceStartup = 0;
            var host = new ISBSpecialForcesAudio();
            var prepare = host.PrepareFikaAudio("radio", "N");
            Tests.Check(prepare.MoveNext() && host.NetworkBridge.Sent.Count == 1, "host prepares before playback");
            var request = host.NetworkBridge.Sent[0].Split('|');
            host.HandleAudioPreparation(new[] { "AR", request[1] });
            Tests.Check(!prepare.MoveNext() && host._fikaAudioReady.Count == 0, "ready peer releases playback");
            var timeout = host.PrepareFikaAudio("radio", "N");
            Tests.Check(timeout.MoveNext(), "missing ack initially waits");
            Time.realtimeSinceStartup = 2.1f;
            Tests.Check(!timeout.MoveNext(), "audio prep timeout is bounded");
            var obsolete = host.PrepareFikaAudio("radio", "N");
            Tests.Check(obsolete.MoveNext(), "second preparation started");
            host._audioGeneration++;
            Tests.Check(!obsolete.MoveNext(), "preparation stops on raid replacement");
            var client = new ISBSpecialForcesAudio(); client.NetworkBridge.IsServer = false;
            string token = Guid.NewGuid().ToString("N");
            var parts = new[] { "AP", token, "N", EncodeNetworkString("radio") };
            client.HandleAudioPreparation(parts); client.HandleAudioPreparation(parts);
            Tests.Check(client._routines.Count == 1 && client.NetworkBridge.Sent.Count == 0, "duplicate prep neither decodes twice nor acknowledges early");
            client.FinishRoutines();
            Tests.Check(client._resolves == 1 && client.NetworkBridge.Sent.Count == 1 && client.NetworkBridge.Sent[0] == "AR|" + token, "ack sent after clip ready");
            client.HandleAudioPreparation(parts);
            Tests.Check(client._resolves == 1 && client.NetworkBridge.Sent.Count == 2, "lost ack can be repeated without redecoding");
            parts[1] = Guid.NewGuid().ToString("N"); client._resolveSuccess = false;
            client.HandleAudioPreparation(parts); client.FinishRoutines();
            Tests.Check(!client._fikaAudioRequests.ContainsKey(parts[1]), "decode failure can be retried");
            client._resolveSuccess = true;
            client.HandleAudioPreparation(parts); client.FinishRoutines();
            Tests.Check(client._fikaAudioRequests[parts[1]], "retry prepares failed clip");
            parts[1] = Guid.NewGuid().ToString("N");
            client.HandleAudioPreparation(parts); int sent = client.NetworkBridge.Sent.Count; client._audioGeneration++;
            client.FinishRoutines();
            Tests.Check(client.NetworkBridge.Sent.Count == sent, "old raid cannot acknowledge new raid");
            client.ResetFikaAudioPreparation();
            Tests.Check(client._fikaAudioReady.Count == 0 && client._fikaAudioRequests.Count == 0 && client._fikaAudioRequestOrder.Count == 0, "audio reset clears request state");
            return new { acknowledgedAfterDecode = true, retriesWithoutDuplicateDecode = true, timeoutSeconds = 2, oldRaidRejected = true };
        }
    }
}
