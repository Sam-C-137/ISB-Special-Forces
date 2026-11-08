using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using ISB.Compatibility;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace ISBSpecialForces.Patches
{
    internal static class ISBEventNightVision
    {
        private sealed class State
        {
            internal WeakReference<BotNightVisionData> Data;
            internal WeakReference<BotOwner> Owner;
            internal Task Pending;
            internal float RetryAt;
            internal int Attempts, Revision;
            internal bool Desired, Controlled, Queued, CheckRequested;
        }

        private const string ExtrasGuid = "com.samc137.isbsofextras";
        private const float OperationInterval = .125f;
        private static ConditionalWeakTable<BotNightVisionData, State> States = new ConditionalWeakTable<BotNightVisionData, State>();
        private static readonly List<WeakReference<State>> Registry = new List<WeakReference<State>>();
        private static readonly List<State> Jobs = new List<State>();
        private static readonly Action<BotNightVisionData, bool> SetUsingNow = (Action<BotNightVisionData, bool>)Delegate.CreateDelegate(typeof(Action<BotNightVisionData, bool>), typeof(BotNightVisionData).GetProperty(nameof(BotNightVisionData.UsingNow)).GetSetMethod(true));
        private static readonly AccessTools.FieldRef<BotNightVisionData, bool> AtPocket = AccessTools.FieldRefAccess<BotNightVisionData, bool>("_nightVisionAtPocket");
        [ThreadStatic] private static BotNightVisionData _wearCapture;
        private static WeakReference<GameWorld> _world;
        private static WeakReference<ISBEventNightVisionDriver> _driver;
        private static Action _requestSnapshot;
        private static float _lastClock = -1f, _nextOperationAt;
        private static int _worldGeneration, _revision, _publishedState;
        private static bool _snapshotKnown, _snapshotRequested, _requestingSnapshot, _wearObserverAvailable, _wearObserverWarning;

        internal static bool IsRole(int role) => role >= 13701 && role <= 13716 || role == GhostBotVariant.ChaosValue || BlackDivisionCheckpointPolicy.IsRole(role);

        internal static bool Guard(BotNightVisionData data)
        {
            return data != null && _snapshotKnown && _publishedState != 0 && States.TryGetValue(data, out var state) && state.Controlled && state.Revision == _revision && ISBFikaAuthority.IsAiAuthority;
        }

        internal static bool Register(BotNightVisionData data, BotOwner owner)
        {
            if (data != null && States.TryGetValue(data, out _))
                return Guard(data);
            if (!TryGetCandidateContext(data, owner, out var world))
                return false;
            EnsureWorld(world);
            ObserveClock();
            var state = new State
            {
                Data = new WeakReference<BotNightVisionData>(data),
                Owner = new WeakReference<BotOwner>(owner),
                Revision = _revision,
                Desired = _publishedState == 2,
                Controlled = _snapshotKnown && _publishedState != 0
            };
            States.Add(data, state);
            Registry.Add(new WeakReference<State>(state));
            if (!_snapshotKnown)
                RequestSnapshot();
            if (state.Controlled)
            {
                state.CheckRequested = true;
                Enqueue(state);
            }
            return Guard(data);
        }

        internal static void Publish(GameWorld world, int state)
        {
            if (world == null || state < 0 || state > 2 || !ReferenceEquals(Singleton<GameWorld>.Instance, world))
                return;
            EnsureWorld(world);
            ObserveClock();
            if (_snapshotKnown && _publishedState == state)
                return;
            _snapshotKnown = true;
            _publishedState = state;
            _revision++;
            for (int index = Registry.Count - 1; index >= 0; index--)
            {
                if (!Registry[index].TryGetTarget(out var entry))
                {
                    Registry.RemoveAt(index);
                    continue;
                }
                entry.Revision = _revision;
                entry.Controlled = state != 0;
                entry.Desired = state == 2;
                entry.Attempts = 0;
                entry.RetryAt = 0f;
                entry.CheckRequested = entry.Controlled;
                if (entry.Controlled || entry.Pending != null)
                    Enqueue(entry);
            }
            SetDriverActive(Jobs.Count != 0);
        }

        internal static void AttachDriver(ISBEventNightVisionDriver driver)
        {
            _driver = new WeakReference<ISBEventNightVisionDriver>(driver);
            SetDriverActive(Jobs.Count != 0);
        }

        internal static void DetachDriver(ISBEventNightVisionDriver driver)
        {
            if (_driver != null && _driver.TryGetTarget(out var current) && ReferenceEquals(current, driver))
                _driver = null;
        }

        internal static void ClearWorld(int generation)
        {
            if (generation != _worldGeneration)
                return;
            _worldGeneration++;
            _revision++;
            _world = null;
            _publishedState = 0;
            _snapshotKnown = _snapshotRequested = false;
            _nextOperationAt = 0f;
            _lastClock = -1f;
            States = new ConditionalWeakTable<BotNightVisionData, State>();
            Registry.Clear();
            Jobs.Clear();
            SetDriverActive(false);
        }

        internal static void Pump()
        {
            if (_world == null || !_world.TryGetTarget(out var world) || !ReferenceEquals(Singleton<GameWorld>.Instance, world))
            {
                ClearWorld(_worldGeneration);
                return;
            }
            float now = ObserveClock();
            for (int index = 0; index < Jobs.Count;)
            {
                var state = Jobs[index];
                if (Process(state, world, now))
                {
                    index++;
                    continue;
                }
                state.Queued = false;
                Jobs.RemoveAt(index);
            }
            SetDriverActive(Jobs.Count != 0);
        }

        private static bool Process(State state, GameWorld world, float now)
        {
            if (state.Pending != null)
            {
                if (!state.Pending.IsCompleted)
                    return true;
                if (state.Pending.IsFaulted)
                    _ = state.Pending.Exception;
                state.Pending = null;
                state.CheckRequested = state.Controlled;
            }
            if (!state.Controlled || !_snapshotKnown || _publishedState == 0 || state.Revision != _revision)
                return false;
            if (state.CheckRequested)
            {
                state.CheckRequested = false;
                if (!TryGetOperationContext(state, world, out var initialData, out _, out _))
                {
                    state.Controlled = false;
                    return false;
                }
                if (IsAligned(initialData, state.Desired))
                    return false;
            }
            if (state.Attempts >= 3)
            {
                state.Controlled = false;
                return false;
            }
            if (now < state.RetryAt || now < _nextOperationAt)
                return true;
            if (!TryGetOperationContext(state, world, out var data, out _, out var player))
            {
                state.Controlled = false;
                return false;
            }
            if (IsAligned(data, state.Desired))
                return false;
            bool atPocket = AtPocket(data);
            if (state.Desired && atPocket && !_wearObserverAvailable)
            {
                state.Controlled = false;
                return false;
            }
            if (state.Revision != _revision || !state.Controlled || !ReferenceEquals(Singleton<GameWorld>.Instance, world) || !ISBFikaAuthority.IsAiAuthority || player.InventoryController == null)
                return true;
            var togglable = data.NightVisionItem?.Togglable;
            if (togglable == null)
            {
                state.Controlled = false;
                return false;
            }
            _nextOperationAt = now + OperationInterval;
            state.RetryAt = now + 2f;
            state.Attempts++;
            state.CheckRequested = true;
            if (state.Desired && atPocket)
            {
                var previous = _wearCapture;
                _wearCapture = data;
                try
                {
                    data.WearToHead();
                }
                finally
                {
                    _wearCapture = previous;
                }
                return true;
            }
            state.Pending = player.InventoryController.TryRunNetworkTransaction(togglable.Set(state.Desired, true, false));
            state.CheckRequested = state.Pending == null;
            UpdateUsingNow(data, !atPocket && togglable.On);
            return true;
        }

        private static bool IsAligned(BotNightVisionData data, bool desired)
        {
            if (data.NightVisionItem.Togglable.On != desired || desired && AtPocket(data))
                return false;
            UpdateUsingNow(data, desired);
            return true;
        }

        private static bool TryGetCandidateContext(BotNightVisionData data, BotOwner owner, out GameWorld world)
        {
            world = null;
            if (!TryGetBotContext(data, owner, out _))
                return false;
            world = Singleton<GameWorld>.Instance;
            if (world == null)
                return false;
            if (_snapshotKnown && _publishedState != 0 && _world != null && _world.TryGetTarget(out var current) && ReferenceEquals(world, current))
                return true;
            string location = world.LocationId;
            return string.IsNullOrEmpty(location) || string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) || location.Length == 24 && location.All(Uri.IsHexDigit);
        }

        private static bool TryGetBotContext(BotNightVisionData data, BotOwner owner, out Player player)
        {
            player = null;
            if (data == null || owner?.Profile?.Info?.Settings == null || !data.HaveNightVision || data.NightVisionItem?.Togglable == null)
                return false;
            player = owner.GetPlayer;
            return IsRole((int)owner.Profile.Info.Settings.Role) && player?.IsAI == true && !owner.IsDead && ISBFikaAuthority.IsAiAuthority;
        }

        private static bool TryGetOperationContext(State state, GameWorld world, out BotNightVisionData data, out BotOwner owner, out Player player)
        {
            data = null;
            owner = null;
            player = null;
            return state.Data.TryGetTarget(out data) && state.Owner.TryGetTarget(out owner) && TryGetBotContext(data, owner, out player) &&
                _snapshotKnown && _publishedState != 0 && _world != null && _world.TryGetTarget(out var current) && ReferenceEquals(current, world) &&
                ReferenceEquals(Singleton<GameWorld>.Instance, world) && player.InventoryController != null;
        }

        private static void EnsureWorld(GameWorld world)
        {
            if (_world != null && _world.TryGetTarget(out var previous) && ReferenceEquals(previous, world))
                return;
            ClearWorld(_worldGeneration);
            _world = new WeakReference<GameWorld>(world);
            var lifetime = world.gameObject.AddComponent<ISBEventNightVisionRaidLifetime>();
            lifetime.Generation = _worldGeneration;
        }

        private static float ObserveClock()
        {
            float now = Time.time;
            if (now < _lastClock)
            {
                _nextOperationAt = 0f;
                foreach (var state in Jobs)
                {
                    state.RetryAt = 0f;
                    state.CheckRequested = state.Controlled;
                }
            }
            _lastClock = now;
            return now;
        }

        private static void Enqueue(State state)
        {
            if (!state.Queued)
            {
                state.Queued = true;
                Jobs.Add(state);
            }
            SetDriverActive(true);
        }

        private static void SetDriverActive(bool active)
        {
            if (_driver != null && _driver.TryGetTarget(out var driver) && driver != null && driver.enabled != active)
                driver.enabled = active;
        }

        private static void RequestSnapshot()
        {
            if (_snapshotRequested || _requestingSnapshot)
                return;
            if (_requestSnapshot == null && Chainloader.PluginInfos.TryGetValue(ExtrasGuid, out var info) && info.Instance != null)
            {
                var method = AccessTools.Method(info.Instance.GetType(), "PublishEventNightVisionSnapshot", Type.EmptyTypes);
                if (method != null && method.ReturnType == typeof(void))
                    _requestSnapshot = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, method);
            }
            if (_requestSnapshot == null)
                return;
            _snapshotRequested = _requestingSnapshot = true;
            try
            {
                _requestSnapshot();
            }
            finally
            {
                _requestingSnapshot = false;
            }
        }

        private static void UpdateUsingNow(BotNightVisionData data, bool value)
        {
            if (data.UsingNow != value)
                SetUsingNow(data, value);
        }

        internal static void EnableWearTaskObserver()
        {
            _wearObserverAvailable = false;
            try
            {
                new ISBEventNightVisionWearTaskPatch().Enable();
            }
            catch (Exception)
            {
                SetWearObserverAvailable(false);
            }
        }

        internal static void SetWearObserverAvailable(bool available)
        {
            _wearObserverAvailable = available;
            if (available || _wearObserverWarning)
                return;
            _wearObserverWarning = true;
            Plugin.LogSource?.LogWarning("ISB NVG movement observer unavailable; pocket-to-head movement will use native behavior.");
        }

        internal static void TrackWearTask(Task<Comfort.Common.IResult> task, BotNightVisionData data)
        {
            if (task != null && ReferenceEquals(_wearCapture, data) && States.TryGetValue(data, out var state))
            {
                state.Pending = task;
                state.CheckRequested = false;
            }
        }
    }

    internal sealed class ISBEventNightVisionDriver : MonoBehaviour
    {
        private void Awake() => ISBEventNightVision.AttachDriver(this);
        private void Update() => ISBEventNightVision.Pump();
        private void OnDestroy() => ISBEventNightVision.DetachDriver(this);
    }

    internal sealed class ISBEventNightVisionRaidLifetime : MonoBehaviour
    {
        internal int Generation;
        private void OnDestroy() => ISBEventNightVision.ClearWorld(Generation);
    }
    internal sealed class ISBEventNightVisionWearTaskPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.WearToHead));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var calls = new List<int>();
            for (int index = 0; index < code.Count; index++)
            {
                if (code[index].opcode != OpCodes.Callvirt || !(code[index].operand is MethodInfo method) || method.DeclaringType != typeof(EFT.InventoryLogic.ItemController) || method.Name != "TryRunNetworkTransaction")
                    continue;
                var parameters = method.GetParameters();
                if (method.ReturnType != typeof(Task<Comfort.Common.IResult>) || parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(Diz.LanguageExtensions.OperationResult) || parameters[1].ParameterType != typeof(Comfort.Common.Callback) ||
                    index + 1 >= code.Count || code[index + 1].opcode != OpCodes.Pop)
                {
                    ISBEventNightVision.SetWearObserverAvailable(false);
                    return code;
                }
                calls.Add(index);
            }
            if (calls.Count != 2)
            {
                ISBEventNightVision.SetWearObserverAvailable(false);
                return code;
            }
            var observer = AccessTools.Method(typeof(ISBEventNightVision), nameof(ISBEventNightVision.TrackWearTask));
            for (int index = calls.Count - 1; index >= 0; index--)
                code.InsertRange(calls[index] + 1, new[] { new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, observer) });
            ISBEventNightVision.SetWearObserverAvailable(true);
            return code;
        }
    }

    internal sealed class ISBEventNightVisionUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.ManualUpdate));
        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BotNightVisionData __instance) => !ISBEventNightVision.Guard(__instance);
    }

    internal sealed class ISBEventNightVisionDecisionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.CheckWhatIWant));
        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BotNightVisionData __instance, BotOwner ____owner) => !ISBEventNightVision.Register(__instance, ____owner);
    }

    internal sealed class ISBEventNightVisionStowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.MoveToHeadPocket));
        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BotNightVisionData __instance) => !ISBEventNightVision.Guard(__instance);
    }
}