using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace ISBSSOFExtra
{
    public sealed class TakeoverBarbedWire : BarbedWire
    {
        private static readonly Dictionary<IPlayerOwner, int> Penalties = new Dictionary<IPlayerOwner, int>();
        private readonly HashSet<IPlayerOwner> _occupants = new HashSet<IPlayerOwner>();
        private bool _ready;
        private bool _hasSound;
        private Vector3 _position, _scale;
        private Quaternion _rotation;
        public override bool IsStatic => false;
        private void OnTriggerStay(Collider other) { }

        internal void PrepareForActivation()
        {
            method_0();
            var field = AccessTools.Field(typeof(BarbedWire), "_soundBank");
            if (field != null)
            {
                foreach (var donor in Resources.FindObjectsOfTypeAll<BarbedWire>())
                {
                    if (donor == null || donor is TakeoverBarbedWire)
                        continue;
                    var bank = field.GetValue(donor) as SoundBank;
                    if (bank == null)
                        continue;
                    field.SetValue(this, bank);
                    _hasSound = true;
                    break;
                }
            }

            _position = transform.position;
            _rotation = transform.rotation;
            _scale = transform.lossyScale;
            _ready = true;
        }

        private void LateUpdate()
        {
            if (!_ready || (_position == transform.position && _rotation == transform.rotation && _scale == transform.lossyScale))
                return;
            method_0();
            _position = transform.position;
            _rotation = transform.rotation;
            _scale = transform.lossyScale;
        }

        public override void ProceedDamage(IPlayerOwner player, BodyPartCollider bodyPart)
        {
            if (!_ready || !isActiveAndEnabled || player?.iPlayer == null || bodyPart == null || !TakeoverFlameTrigger.CanWireDamage(player))
                return;
            base.ProceedDamage(player, bodyPart);
        }

        public override void AddPenalty(IPlayerOwner player)
        {
            if (!_ready || player?.iPlayer == null || !TakeoverFlameTrigger.CanWireDamage(player))
                return;
            if (!_occupants.Add(player))
                return;
            Penalties.TryGetValue(player, out var count);
            Penalties[player] = count + 1;
            base.AddPenalty(player);
        }

        public override void RemovePenalty(IPlayerOwner player)
        {
            if (player == null || !_occupants.Remove(player))
                return;
            ReleasePenalty(player);
        }

        public override void PlaySound(bool useOcclusion = false)
        {
            if (_ready && _hasSound)
                base.PlaySound(useOcclusion);
        }

        private void OnDisable()
        {
            _ready = false;
            foreach (var player in _occupants)
                ReleasePenalty(player);
            _occupants.Clear();
        }

        private void ReleasePenalty(IPlayerOwner player)
        {
            if (!Penalties.TryGetValue(player, out var count))
                return;
            if (count > 1)
            {
                Penalties[player] = count - 1;
                return;
            }

            Penalties.Remove(player);
            if (Singleton<GameWorld>.Instance != null && player?.iPlayer != null)
                base.RemovePenalty(player);
        }
    }
}
