using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private readonly Dictionary<string, HashSet<string>> _fikaAudioReady = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, bool> _fikaAudioRequests = new Dictionary<string, bool>();
        private readonly Queue<string> _fikaAudioRequestOrder = new Queue<string>();

        private IEnumerator PrepareFikaAudio(string clipName, string category = "")
        {
            if (!IsFikaNetworkSession || !IsFikaServer || !_raidisActive)
                yield break;
            int generation = _audioGeneration;
            string token = Guid.NewGuid().ToString("N");
            var ready = new HashSet<string>();
            _fikaAudioReady.Add(token, ready);
            string payload = "AP|" + token + "|" + category + "|" + EncodeNetworkString(clipName);
            float deadline = Time.realtimeSinceStartup + 2f;
            float nextRequest = 0f;
            try
            {
                while (_raidisActive && generation == _audioGeneration && !AudioPeersReady(ready) && Time.realtimeSinceStartup < deadline)
                {
                    if (Time.realtimeSinceStartup >= nextRequest)
                    {
                        nextRequest = Time.realtimeSinceStartup + .5f;
                        NetworkBridge.Broadcast(payload);
                    }
                    yield return null;
                }
            }
            finally
            {
                _fikaAudioReady.Remove(token);
            }
        }

        private bool AudioPeersReady(HashSet<string> ready)
        {
            var players = _activeRaidWorld?.AllAlivePlayersList;
            if (players == null)
                return true;
            foreach (var player in players)
                if (player != null && !player.IsAI && !player.IsYourPlayer && player.HealthController?.IsAlive == true && _fikaReadyPlayers.Contains(player.ProfileId) && !ready.Contains(player.ProfileId))
                    return false;
            return true;
        }

        private void HandleAudioPreparation(string[] parts)
        {
            if (parts[0] == "AR")
            {
                if (IsFikaServer && parts.Length == 2 && _fikaAudioReady.TryGetValue(parts[1], out var ready) && !string.IsNullOrEmpty(NetworkBridge.ReceivedPlayerId))
                    ready.Add(NetworkBridge.ReceivedPlayerId);
                return;
            }
            if (IsEventAuthority || !_raidisActive || parts.Length != 4 || parts[1].Length != 32 || !Guid.TryParseExact(parts[1], "N", out _) || parts[2].Length > 4)
                return;
            string name = DecodeNetworkString(parts[3]);
            if (string.IsNullOrEmpty(name) || name.Length > 180 || Path.GetFileName(name) != name)
                return;
            if (_fikaAudioRequests.TryGetValue(parts[1], out bool prepared))
            {
                if (prepared)
                    NetworkBridge.SendToServer("AR|" + parts[1]);
                return;
            }
            _fikaAudioRequests.Add(parts[1], false);
            _fikaAudioRequestOrder.Enqueue(parts[1]);
            while (_fikaAudioRequestOrder.Count > 128)
                _fikaAudioRequests.Remove(_fikaAudioRequestOrder.Dequeue());
            StartCoroutine(PrepareNetworkAudio(parts[1], name, parts[2], _audioGeneration));
        }

        private IEnumerator PrepareNetworkAudio(string token, string clipName, string category, int generation)
        {
            AudioClip clip = null;
            yield return ResolveNetworkClip(clipName, category, value => clip = value);
            if (!_raidisActive || generation != _audioGeneration || !_fikaAudioRequests.ContainsKey(token))
                yield break;
            if (!IsAudioClipReady(clip))
            {
                _fikaAudioRequests.Remove(token);
                yield break;
            }
            _fikaAudioRequests[token] = true;
            NetworkBridge.SendToServer("AR|" + token);
        }

        private void WarmAnnouncement(string fileName)
        {
            string clipName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(clipName))
                return;
            if (IsFikaServer)
                NetworkBridge.Broadcast("AL||" + EncodeNetworkString(clipName));
            if (CanPresentLocally)
                StartCoroutine(ResolveNetworkClip(clipName, null, null));
        }

        private void HandleAudioWarmup(string[] parts)
        {
            if (IsEventAuthority || !_raidisActive || parts.Length != 3 || parts[1].Length > 4)
                return;
            string name = DecodeNetworkString(parts[2]);
            if (!string.IsNullOrEmpty(name) && name.Length <= 180 && Path.GetFileName(name) == name)
                StartCoroutine(ResolveNetworkClip(name, parts[1], null));
        }

        private IEnumerator PreparePreBlackoutAudio(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                yield break;
            if (CanPresentLocally)
                yield return ResolveNetworkClip(Path.GetFileNameWithoutExtension(fileName), null, null);
            yield return PrepareFikaAudio(Path.GetFileNameWithoutExtension(fileName));
        }

        private void ResetFikaAudioPreparation()
        {
            _fikaAudioReady.Clear();
            _fikaAudioRequests.Clear();
            _fikaAudioRequestOrder.Clear();
        }
    }
}
