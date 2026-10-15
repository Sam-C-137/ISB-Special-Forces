using EFT;
using System;
using BepInEx.Logging;
using HarmonyLib;
using EFT.Interactive;
using UnityEngine;

namespace ISBSSOFExtra
{
    public sealed class TakeoverFlameTrigger : FlameDamageTrigger
    {
        private bool _ready;
        private static Harmony _harmony;
        internal static bool EventActive { get; set; }

        internal static void EnableImmunity(ManualLogSource log)
        {
            if (_harmony != null)
                return;
            try
            {
                var target = AccessTools.Method(typeof(FlameDamageTrigger), nameof(ProceedDamage), new[] { typeof(IPlayerOwner), typeof(BodyPartCollider) });
                if (target == null)
                    throw new MissingMethodException(nameof(FlameDamageTrigger.ProceedDamage));
                _harmony = new Harmony("com.samc137.isbsofextras.event-fire");
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(TakeoverFlameTrigger), nameof(CanDamage)));
                _harmony.Patch(AccessTools.Method(typeof(BarbedWire), nameof(BarbedWire.ProceedDamage)), prefix: new HarmonyMethod(typeof(TakeoverFlameTrigger), nameof(CanWireDamage)));
                _harmony.Patch(AccessTools.Method(typeof(BarbedWire), nameof(BarbedWire.AddPenalty)), prefix: new HarmonyMethod(typeof(TakeoverFlameTrigger), nameof(CanWireDamage)));
            }
            catch (Exception ex)
            {
                DisableImmunity();
                log?.LogError("ISB SOF: Event fire protection unavailable: " + ex.GetBaseException().Message);
            }
        }

        internal static void DisableImmunity()
        {
            EventActive = false;
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static bool CanDamage(IPlayerOwner __0)
        {
            if (!EventActive)
                return true;
            if (LabsSpawnSafety.FireDamageSuspended)
                return false;
            return __0?.iPlayer?.IsAI != true;
        }

        internal static bool CanWireDamage(IPlayerOwner __0)
        {
            if (!EventActive || __0?.iPlayer?.IsAI != true)
                return true;
            var settings = __0.iPlayer.Profile?.Info?.Settings;
            return settings == null || !ProtectedWireRole((int)settings.Role);
        }

        internal static bool ProtectedWireRole(int role) => role >= 13701 && role <= 13716 || role >= 848420 && role <= 848424 || role == 848426;
        public override bool IsStatic => false;
        private void OnTriggerStay(Collider other) { }

        internal void PrepareForActivation()
        {
            _ready = false;
            method_0();
            var box = GetComponent<BoxCollider>();
            if (box == null || !box.enabled || !isActiveAndEnabled || gameObject.layer != LayerMask.NameToLayer("Triggers"))
                throw new System.InvalidOperationException("Native flame trigger failed activation checks.");
            _ready = true;
        }

        public override void ProceedDamage(IPlayerOwner player, BodyPartCollider bodyPart)
        {
            if (!_ready || !isActiveAndEnabled || !CanDamage(player))
                return;
            base.ProceedDamage(player, bodyPart);
        }
    }
}
