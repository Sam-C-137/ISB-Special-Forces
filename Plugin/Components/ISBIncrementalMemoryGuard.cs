using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace ISBSpecialForces.Components
{
    internal sealed class ISBIncrementalMemoryGuard : MonoBehaviour
    {
        private GameWorld _world;
        private long _baseline;
        private float _pollAt, _readyAt, _retryAt;
        private bool _ownsCycle;
        private bool _faulted;
        internal static int CompletedCycles { get; private set; }
        internal static bool CycleActive { get; private set; }

        internal static bool ShouldCollect(long used, long baseline, bool incremental, bool disabled, bool ready)
        {
            return incremental && disabled && ready && used >= 4096L * 1048576 && used - baseline >= 768L * 1048576;
        }

        private void Update()
        {
            if (_faulted)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception error)
            {
                Release();
                _faulted = true;
                Plugin.LogSource?.LogError("[ISB] Incremental memory budget disabled after a native GC error: " + error);
            }
        }

        private void Tick()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world is HideoutGameWorld)
                world = null;
            if (!ReferenceEquals(world, _world))
            {
                Release();
                _world = world;
                _baseline = Profiler.GetMonoUsedSizeLong();
                _readyAt = Time.realtimeSinceStartup + 90f;
                _retryAt = _readyAt;
                _pollAt = 0f;
                CompletedCycles = 0;
            }

            if (world == null || !Plugin.IncrementalMemoryBudgetEnabled)
            {
                Release();
                return;
            }

            if (_ownsCycle)
            {
                if (GarbageCollector.GCMode != GarbageCollector.Mode.Manual)
                {
                    _ownsCycle = CycleActive = false;
                    return;
                }

                if (GarbageCollector.CollectIncremental(500000UL))
                    return;
                CompletedCycles++;
                Release();
                _baseline = Profiler.GetMonoUsedSizeLong();
                _retryAt = Time.realtimeSinceStartup + 60f;
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (now < _pollAt)
                return;
            _pollAt = now + 5f;
            var used = Profiler.GetMonoUsedSizeLong();
            if (now < _readyAt)
            {
                _baseline = used;
                return;
            }

            if (ShouldCollect(used, _baseline, GarbageCollector.isIncremental, GarbageCollector.GCMode == GarbageCollector.Mode.Disabled, now >= _retryAt))
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Manual;
                _ownsCycle = CycleActive = true;
            }
        }

        private void Release()
        {
            if (_ownsCycle && GarbageCollector.GCMode == GarbageCollector.Mode.Manual)
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            _ownsCycle = CycleActive = false;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
