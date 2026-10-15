using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private const string PortableRadioTpl = "6aa5ff857a930fba7793141b";
        private const float PortableRadioVolume = .06f;
        private const float PortableRadioMinDistance = .5f;
        private const float PortableRadioMaxDistance = 6f;
        private Coroutine _portableRadioLoop;
        private bool _normalRadioPlaying;
        private readonly HashSet<Player> _portableRadioSeen = new HashSet<Player>();
        private readonly List<Player> _portableRadioRetired = new List<Player>();
        private static readonly WaitForSeconds PortableRadioScanWait = new WaitForSeconds(1f);
        private static readonly WaitForSeconds PortableRadioChannelWait = new WaitForSeconds(.25f);
        private readonly Dictionary<Player, PortableRadioState> _portableRadios = new Dictionary<Player, PortableRadioState>();
        private sealed class PortableRadioState
        {
            internal readonly PortableRadioTiming Timing = new PortableRadioTiming();
            internal Coroutine Routine;
        }

        private static bool HasEquippedPortableRadio(Player player)
        {
            if (player == null || player.IsAI || player.HealthController == null || !player.HealthController.IsAlive || string.IsNullOrEmpty(player.ProfileId))
                return false;
            return ContainsSpecialSlotRadio(player.Profile?.Inventory?.Equipment, 0);
        }

        private static bool ContainsSpecialSlotRadio(Item item, int depth)
        {
            var container = item as CompoundItem;
            if (container?.Slots == null || depth > 4)
                return false;
            foreach (var slot in container.Slots)
            {
                var child = slot?.ContainedItem;
                if (child == null)
                    continue;
                if (PortableRadioTiming.IsSpecialSlot(slot.ID) && string.Equals(child.TemplateId.ToString(), PortableRadioTpl, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (ContainsSpecialSlotRadio(child, depth + 1))
                    return true;
            }

            return false;
        }

        private IEnumerator PortableRadioLoop()
        {
            while (_raidisActive && IsEventAuthority)
            {
                var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
                var seen = _portableRadioSeen;
                seen.Clear();
                bool hasOperators = false;
                bool whiteTusk = false;
                if (world?.AllAlivePlayersList != null)
                {
                    foreach (Player candidate in world.AllAlivePlayersList)
                    {
                        if (!IsEligibleISBBot(candidate, true))
                            continue;
                        hasOperators = true;
                        if (IsWhiteTuskBot(candidate))
                            whiteTusk = true;
                    }

                    var paths = GetAudioPathsForContext("normal radio chatter", whiteTusk);
                    if (paths.Count == 0 && whiteTusk)
                        paths = GetAudioPathsForContext("normal radio chatter", false);
                    foreach (Player player in world.AllAlivePlayersList)
                    {
                        if (!HasEquippedPortableRadio(player))
                            continue;
                        seen.Add(player);
                        PortableRadioState state;
                        if (!_portableRadios.TryGetValue(player, out state))
                        {
                            state = new PortableRadioState();
                            _portableRadios.Add(player, state);
                        }

                        float now = Time.time;
                        state.Timing.SetEquipped(true, now);
                        if (paths.Count == 0 || !state.Timing.IsDue(now, hasOperators))
                            continue;
                        string path = SelectRandomAudioPath(paths, "portable radio chatter");
                        if (string.IsNullOrEmpty(path))
                            continue;
                        string category = "P" + GetRadioCategoryForContext("normal radio chatter", IsAishiAudioPath(path), IsWhiteTuskAudioPath(path));
                        state.Timing.Started(now);
                        state.Routine = StartCoroutine(PlayPortableRadio(player, state, path, category));
                    }
                }

                var retired = _portableRadioRetired;
                retired.Clear();
                foreach (var entry in _portableRadios)
                {
                    if (seen.Contains(entry.Key))
                        continue;
                    entry.Value.Timing.SetEquipped(false, Time.time);
                    if (!entry.Value.Timing.Playing)
                        retired.Add(entry.Key);
                }

                foreach (Player player in retired)
                    _portableRadios.Remove(player);
                yield return PortableRadioScanWait;
            }

            _portableRadioLoop = null;
        }

        private IEnumerator PlayPortableRadio(Player player, PortableRadioState state, string path, string category)
        {
            bool ownsChannel = false;
            try
            {
                while (_normalRadioPlaying && _raidisActive && HasEquippedPortableRadio(player))
                    yield return PortableRadioChannelWait;
                if (!_raidisActive || !HasEquippedPortableRadio(player) || !IsRadioCategoryEnabled(category))
                    yield break;
                _normalRadioPlaying = true;
                ownsChannel = true;
                if (IsHeadless)
                {
                    BroadcastFikaRadio(Path.GetFileNameWithoutExtension(path), player, category);
                    yield return new WaitForSeconds(Mathf.Max(GetWavDurationSeconds(path), .05f));
                    yield break;
                }

                AudioClip clip;
                if (!_audioClipCache.TryGetValue(path, out clip) || !IsAudioClipReady(clip))
                {
                    clip = null;
                    yield return StartCoroutine(LoadAudioClip(path, value => clip = value));
                }

                if (!IsAudioClipReady(clip) || !_raidisActive || !HasEquippedPortableRadio(player) || !IsRadioCategoryEnabled(category))
                    yield break;
                Transform parent = player.PlayerBones?.RootJoint;
                if (parent == null)
                    yield break;
                BroadcastFikaRadio(clip.name, player, category);
                yield return PlayISBSoldiersRadio(clip, parent, Vector3.up * 1.2f, PortableRadioMinDistance, PortableRadioMaxDistance, PortableRadioVolume, _radioVolume, false, 0f, false, false, RADIO_SPATIAL_BLEND, RADIO_ROLLOFF_MODE, () => _raidisActive && HasEquippedPortableRadio(player) && IsRadioCategoryEnabled(category));
            }
            finally
            {
                if (ownsChannel)
                    _normalRadioPlaying = false;
                state.Timing.Finished();
                state.Routine = null;
            }
        }

        private void ResetPortableRadios()
        {
            StopRoutine(ref _portableRadioLoop);
            foreach (var state in _portableRadios.Values)
                StopRoutine(ref state.Routine);
            _portableRadios.Clear();
            _portableRadioSeen.Clear();
            _portableRadioRetired.Clear();
            _normalRadioPlaying = false;
        }
    }
}
