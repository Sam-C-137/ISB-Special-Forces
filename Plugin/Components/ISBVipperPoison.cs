using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using ISB.Compatibility;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
#if ISB_SPT41
using Hit = EFT.Ballistics.DamageInfo;
#else
using Hit = DamageInfoStruct;
#endif

namespace ISBSpecialForces.Components
{
    internal sealed class ISBVipperPoison : MonoBehaviour
    {
        private sealed class Mark { internal bool Applied; }
        private sealed class Pending
        {
            internal ActiveHealthController Health;
            internal GameWorld World;
            internal float At;
        }
        private static readonly ConditionalWeakTable<object, Mark> Accelerated = new ConditionalWeakTable<object, Mark>();
        private static readonly List<Pending> Queue = new List<Pending>();
        [ThreadStatic] private static bool Applying;
        internal static readonly Type EffectType = typeof(ActiveHealthController).GetNestedType("Intoxication", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo Interval = EffectType.GetField(
#if ISB_SPT41
            "float_16"
#else
            "Float_16"
#endif
            , BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Rate = EffectType.GetField(
#if ISB_SPT41
            "float_18"
#else
            "Float_18"
#endif
            , BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo SetRate = EffectType.GetMethod("SetHealthRatesPerSecond", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        internal static void Initialize(Plugin plugin)
        {
            if (EffectType == null || Interval == null || Rate == null || SetRate == null)
                throw new MissingMemberException("Native intoxication timing is unavailable.");
            new DamagePatch().Enable();
            new StartedPatch().Enable();
            new ResiduePatch().Enable();
            new RemovedPatch().Enable();
            plugin.gameObject.AddComponent<ISBVipperPoison>();
        }
        internal static void Hit(ActiveHealthController health, string source)
        {
            if (!VipperPoisonIdentity.IsPoisoned(source) || !health.IsAlive || !Singleton<GameWorld>.Instantiated) return;
            foreach (var pending in Queue) if (ReferenceEquals(pending.Health, health)) return;
            Queue.Add(new Pending { Health = health, World = Singleton<GameWorld>.Instance, At = Time.time + 6f });
        }
        private void Update()
        {
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                var pending = Queue[i];
                if (!Singleton<GameWorld>.Instantiated || !ReferenceEquals(pending.World, Singleton<GameWorld>.Instance) || !pending.Health.IsAlive) { Queue.RemoveAt(i); continue; }
                if (Time.time < pending.At) continue;
                Queue.RemoveAt(i);
                Apply(pending.Health);
            }
        }
        internal static object Apply(ActiveHealthController health)
        {
            Applying = true;
            try
            {
                object effect = health.DoIntoxication();
                if (effect != null) { Accelerated.GetValue(effect, _ => new Mark()); Speed(effect); }
                return effect;
            }
            finally { Applying = false; }
        }
        private static void Speed(object effect)
        {
            if (!Accelerated.TryGetValue(effect, out var mark))
            {
                if (!Applying) return;
                mark = Accelerated.GetValue(effect, _ => new Mark());
            }
            float interval = (float)Interval.GetValue(effect);
            if (mark.Applied || interval <= 0f) return;
            Interval.SetValue(effect, interval * .5f);
            SetRate.Invoke(effect, new object[] { (float)Rate.GetValue(effect) * 2f, 0f, 0f, 0f });
            mark.Applied = true;
        }
        private void OnDestroy() => Queue.Clear();
        private sealed class DamagePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod() => typeof(ActiveHealthController).GetMethod("ApplyDamage", BindingFlags.Public | BindingFlags.Instance);
            [PatchPostfix] private static void Postfix(ActiveHealthController __instance, Hit damageInfo) => Hit(__instance, damageInfo.SourceId);
        }
        private sealed class StartedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod() => EffectType.GetMethod("Started", BindingFlags.Public | BindingFlags.Instance);
            [PatchPostfix] private static void Postfix(object __instance)
            {
                if (Accelerated.TryGetValue(__instance, out var mark)) mark.Applied = false;
                Speed(__instance);
            }
        }
        private sealed class ResiduePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod() => EffectType.GetMethod("Residue", BindingFlags.Public | BindingFlags.Instance);
            [PatchPostfix] private static void Postfix(object __instance) => Accelerated.Remove(__instance);
        }
        private sealed class RemovedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod() => EffectType.GetMethod("Removed", BindingFlags.Public | BindingFlags.Instance);
            [PatchPostfix] private static void Postfix(object __instance) => Accelerated.Remove(__instance);
        }
    }
}
