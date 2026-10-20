using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Text;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private bool IsEligibleISBBot(Player player, bool requireAlive)
        {
            if (player == null)
                return false;
            if (!player.IsAI)
                return false;
            if (player.HealthController == null)
                return false;
            if (requireAlive && !player.HealthController.IsAlive)
                return false;
            if (player.Profile == null || player.Profile.Info == null)
                return false;
            return ISBEnums.Contains((int)player.Profile.Info.Settings.Role);
        }

        private void CacheEligibleBots(GameWorld gameWorld)
        {
            _listofactiveBots.Clear();
            if (gameWorld == null)
                return;
            if (gameWorld.AllAlivePlayersList == null)
                return;
            foreach (Player player in gameWorld.AllAlivePlayersList)
            {
                if (!IsEligibleISBBot(player, true))
                    continue;
                _listofactiveBots.Add(player);
            }

            _lastBotCacheTime = Time.time;
            if (IsEventAuthority)
                SubscribeToDeaths(_listofactiveBots);
        }

        private void CacheEligibleBotsIfNeeded()
        {
            if (Time.time - _lastBotCacheTime < BOT_CACHE_INTERVAL)
                return;
            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return;
            CacheEligibleBots(Singleton<GameWorld>.Instance);
        }

        private void SubscribeToDeaths(List<Player> bots)
        {
            foreach (Player bot in bots)
            {
                if (bot == null)
                    continue;
                if (bot.HealthController == null)
                    continue;
                if (_deathSubscribedBots.Contains(bot))
                    continue;
                _deathSubscribedBots.Add(bot);
                Player captured = bot;
                captured.HealthController.DiedEvent += delegate (EDamageType dmg)
                {
                    OnISBBotDied(captured);
                };
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo($"ISB SOF Audio Plugin: ISB operator registered with Enum {(int)bot.Profile.Info.Settings.Role}.");
                }
            }
        }

        private void OnISBBotDied(Player bot)
        {
            if (!IsEventAuthority || !_raidisActive)
                return;
            if (_heavyDamagePending)
            {
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Audio Plugin: Operator died before heavy damage reaction fired - suppressing heavy damage comms.");
                }

                _heavyDamagePending = false;
                _heavyDamageTarget = null;
                if (_heavyDamageRout != null)
                {
                    StopCoroutine(_heavyDamageRout);
                    _heavyDamageRout = null;
                }
            }

            if (!_enableDeathChatter.Value && !_enableAishiDeathChatter.Value)
                return;
            if (_deathChatterOnCooldown)
                return;
            if (!HasAvailableAudioForContext("death radio chatter"))
            {
                Log.LogWarning("ISB SOF Audio Plugin: ISB operator KIA detected, but KIA comms clips are not loaded yet.");
                return;
            }

            if (!IsEligibleISBBot(bot, false))
                return;
            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return;
            CacheEligibleBots(Singleton<GameWorld>.Instance);
            if (_listofactiveBots.Count == 0)
            {
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Audio Plugin: ISB operator KIA detected, but no alive ISB operators remains to answer.");
                }

                return;
            }

            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Audio Plugin: ISB operator KIA detected - triggering KIA comms.");
            }

            _deathChatterOnCooldown = true;
            if (_deathRout != null)
                StopCoroutine(_deathRout);
            _deathRout = StartCoroutine(DeathChatterRadioRoutine(IsWhiteTuskBot(bot)));
        }

        private IEnumerator DeathChatterRadioRoutine(bool whiteTuskContext)
        {
            yield return new WaitForSeconds(3f);
            yield return PlayRandomISBRadioFromList("death radio chatter", whiteTuskContext);
            float cooldown = UnityEngine.Random.Range(8f, 14f);
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo($"ISB SOF Audio Plugin: KIA comms cooldown {cooldown:F1}s.");
            }

            yield return new WaitForSeconds(cooldown);
            _deathChatterOnCooldown = false;
            _deathRout = null;
        }

        private IEnumerator HeavyDamageWatcherLoop()
        {
            yield return new WaitForSeconds(3f);
            while (_raidisActive)
            {
                if (_enableHeavyDamageChatter.Value || _enableAishiHeavyDamageChatter.Value)
                    CheckHeavyDamage();
                yield return new WaitForSeconds(HEAVY_DAMAGE_CHECK_INTERVAL);
            }

            _heavyDamageWatcher = null;
        }

        private void CheckHeavyDamage()
        {
            if (!IsEventAuthority || !_raidisActive)
                return;
            CacheEligibleBotsIfNeeded();
            foreach (Player bot in _listofactiveBots)
            {
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;
                float currentHealth = GetTotalHealth(bot);
                float previous;
                if (_lastKnownHealth.TryGetValue(bot, out previous))
                {
                    float lost = previous - currentHealth;
                    if (lost >= HEAVY_DAMAGE_THRESHOLD && !_heavyDamageChatterOnCooldown && !_heavyDamagePending && HasAvailableAudioForContext("heavy damage radio chatter"))
                    {
                        if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                        {
                            Log.LogInfo($"ISB SOF Audio Plugin: ISB Operator Heavy damage received detected | ({lost:F0} HP Lost) - scheduling reaction in {HEAVY_DAMAGE_REACTION_DELAY:F1}s.");
                        }

                        _heavyDamagePending = true;
                        _heavyDamageTarget = bot;
                        if (_heavyDamageRout != null)
                            StopCoroutine(_heavyDamageRout);
                        _heavyDamageRout = StartCoroutine(HeavyDamageRadioRoutine());
                    }
                }

                _lastKnownHealth[bot] = currentHealth;
            }
        }

        private float GetTotalHealth(Player bot)
        {
            float total = 0f;
            for (int i = 0; i < TrackedBodyParts.Length; i++)
            {
                EBodyPart part = TrackedBodyParts[i];
                if (part == EBodyPart.Common)
                    continue;
                try
                {
                    var hp = bot.HealthController.GetBodyPartHealth(part);
                    total += hp.Current;
                }
                catch
                {
                }
            }

            return total;
        }

        private IEnumerator HeavyDamageRadioRoutine()
        {
            yield return new WaitForSeconds(HEAVY_DAMAGE_REACTION_DELAY);
            if (!_raidisActive || !_heavyDamagePending || _heavyDamageTarget == null || _heavyDamageTarget.HealthController == null || !_heavyDamageTarget.HealthController.IsAlive || !IsEligibleISBBot(_heavyDamageTarget, true))
            {
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Audio Plugin: Heavy damage reaction aborted (operator no longer valid/alive).");
                }

                _heavyDamagePending = false;
                _heavyDamageTarget = null;
                _heavyDamageRout = null;
                yield break;
            }

            bool whiteTuskContext = IsWhiteTuskBot(_heavyDamageTarget);
            _heavyDamagePending = false;
            _heavyDamageChatterOnCooldown = true;
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null)
                CacheEligibleBotsIfNeeded();
            yield return PlayRandomISBRadioFromList("heavy damage radio chatter", whiteTuskContext);
            _heavyDamageTarget = null;
            float cooldown = UnityEngine.Random.Range(12f, 20f);
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo($"ISB SOF Audio Plugin: Heavy damage comms cooldown {cooldown:F1}s.");
            }

            yield return new WaitForSeconds(cooldown);
            _heavyDamageChatterOnCooldown = false;
            _heavyDamageRout = null;
        }

        private static bool IsWhiteTuskBot(Player bot)
        {
            if (bot == null || bot.Profile == null || bot.Profile.Info == null || bot.Profile.Info.Settings == null)
                return false;
            int role = (int)bot.Profile.Info.Settings.Role;
            return role == 13702 || role == 13703 || role == 13704 || role == 13705 || role == 13706 || role == 13715 || role == 13716;
        }

        private IEnumerator ISBSpecialForces_RadiosLoop()
        {
            if (!IsEventAuthority)
                yield break;
            yield return new WaitForSeconds(RADIO_INITIAL_DELAY);
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo("ISB SOF Audio Plugin: Normal radio chatter scheduler is active; " + "first playback window opened after " + RADIO_INITIAL_DELAY.ToString("F0", CultureInfo.InvariantCulture) + "s.");
            }

            while (_raidisActive)
            {
                if (!_enableRadioChatter.Value && !_enableAishiRadioChatter.Value)
                {
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                CacheEligibleBotsIfNeeded();
                if (_listofactiveBots.Count > 0 && HasAvailableAudioForContext("normal radio chatter"))
                {
                    yield return PlayRandomISBRadioFromList("normal radio chatter");
                }

                yield return new WaitForSeconds(UnityEngine.Random.Range(15f, 30f));
            }

            _radiorout = null;
        }

        private IEnumerator PlayRandomISBRadioFromList(string contextLog, bool? whiteTuskContext = null)
        {
            bool normal = contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) < 0 && contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) < 0;
            if (normal && _normalRadioPlaying)
                yield break;
            if (normal)
                _normalRadioPlaying = true;
            try
            {
                yield return PlayRandomISBRadioFromListCore(contextLog, whiteTuskContext);
            }
            finally
            {
                if (normal)
                    _normalRadioPlaying = false;
            }
        }

        private IEnumerator PlayRandomISBRadioFromListCore(string contextLog, bool? whiteTuskContext = null)
        {
            if (!_raidisActive)
                yield break;
            CacheEligibleBotsIfNeeded();
            for (int i = _listofactiveBots.Count - 1; i >= 0; i--)
            {
                Player candidateBot = _listofactiveBots[i];
                if (candidateBot == null || candidateBot.HealthController == null || !candidateBot.HealthController.IsAlive || !IsEligibleISBBot(candidateBot, true))
                {
                    _listofactiveBots.RemoveAt(i);
                }
            }

            if (_listofactiveBots.Count == 0)
            {
                Log.LogWarning($"ISB SOF Audio Plugin: No active ISB operators available for {contextLog}.");
                yield break;
            }

            Player bot = _listofactiveBots[UnityEngine.Random.Range(0, _listofactiveBots.Count)];
            if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive || (!IsHeadless && (bot.PlayerBones == null || bot.PlayerBones.RootJoint == null)))
            {
                Log.LogWarning($"ISB SOF Audio Plugin: Invalid ISB operator selected for {contextLog}.");
                yield break;
            }

            bool whiteTusk = whiteTuskContext ?? IsWhiteTuskBot(bot);
            List<string> paths = GetAudioPathsForContext(contextLog, whiteTusk);
            List<AudioClip> clips = GetAudioClipsForContext(contextLog, whiteTusk);
            if (!HasAvailableAudio(clips, paths))
            {
                yield break;
            }

            string categoryKey = GetRadioCategoryForContext(contextLog, false, whiteTusk);
            string selectedPath = SelectRandomAudioPath(paths, categoryKey);
            bool selectedAishi = !string.IsNullOrEmpty(selectedPath) && IsAishiAudioPath(selectedPath);
            bool selectedWhiteTusk = !string.IsNullOrEmpty(selectedPath) && IsWhiteTuskAudioPath(selectedPath);
            if (IsHeadless)
            {
                if (string.IsNullOrEmpty(selectedPath))
                {
                    Log.LogWarning($"ISB SOF Extras: Headless could not select an audio path for {contextLog}.");
                    yield break;
                }

                string clipName = Path.GetFileNameWithoutExtension(selectedPath);
                float clipLength = GetWavDurationSeconds(selectedPath);
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo($"ISB SOF Extras: Headless authority selected {contextLog}: {clipName} | " + $"pool {paths.Count} | length {clipLength:F2}s | bot enum {(int)bot.Profile.Info.Settings.Role}");
                }

                string networkCategory = GetRadioCategoryForContext(contextLog, selectedAishi, selectedWhiteTusk);
                BroadcastFikaRadio(clipName, bot, networkCategory);
                yield return new WaitForSeconds(Mathf.Max(clipLength, 0.05f));
                yield break;
            }

            clips.RemoveAll(clip => !IsAudioClipReady(clip));
            AudioClip selectedClip = null;
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (!_audioClipCache.TryGetValue(selectedPath, out selectedClip) || !IsAudioClipReady(selectedClip))
                {
                    selectedClip = null;
                    yield return StartCoroutine(LoadAudioClip(selectedPath, delegate (AudioClip value)
                    {
                        selectedClip = value;
                    }));
                }

                if (IsAudioClipReady(selectedClip) && !clips.Contains(selectedClip))
                    clips.Add(selectedClip);
            }
            else if (clips.Count > 0)
            {
                selectedClip = clips[UnityEngine.Random.Range(0, clips.Count)];
            }

            if (!IsAudioClipReady(selectedClip) || !_raidisActive || !IsEligibleISBBot(bot, true) || bot.PlayerBones == null || bot.PlayerBones.RootJoint == null)
            {
                Log.LogWarning($"ISB SOF Audio Plugin: Could not resolve the selected path for {contextLog}. " + $"path='{selectedPath ?? string.Empty}', indexed={paths?.Count ?? 0}, cached={clips.Count}.");
                yield break;
            }

            if (string.IsNullOrEmpty(selectedPath))
            {
                selectedAishi = IsAishiAudioClip(selectedClip);
                selectedWhiteTusk = IsWhiteTuskAudioClip(selectedClip);
            }

            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo($"ISB SOF Audio Plugin: Playing {contextLog}: {selectedClip.name} | " + $"pool {paths?.Count ?? 0} | length {selectedClip.length:F2}s | " + $"bot enum {(int)bot.Profile.Info.Settings.Role}");
            }

            string selectedCategory = GetRadioCategoryForContext(contextLog, selectedAishi, selectedWhiteTusk);
            BroadcastFikaRadio(selectedClip.name, bot, selectedCategory);
            yield return PlayISBSoldiersRadio(selectedClip, bot.PlayerBones.RootJoint, Vector3.up * 1.5f, RADIO_MIN_DISTANCE, RADIO_MAX_DISTANCE, RADIO_BASE_VOLUME, _radioVolume, false, 0f, false, false, RADIO_SPATIAL_BLEND, RADIO_ROLLOFF_MODE);
        }

        private string SelectRandomAudioPath(List<string> paths, string category)
        {
            if (paths == null || paths.Count == 0)
                return null;
            int selectedIndex = UnityEngine.Random.Range(0, paths.Count);
            string lastPath;
            if (paths.Count > 1 && _lastRadioPathByCategory.TryGetValue(category, out lastPath) && string.Equals(paths[selectedIndex], lastPath, StringComparison.OrdinalIgnoreCase))
            {
                int offset = UnityEngine.Random.Range(1, paths.Count);
                selectedIndex = (selectedIndex + offset) % paths.Count;
            }

            string selectedPath = paths[selectedIndex];
            _lastRadioPathByCategory[category] = selectedPath;
            return selectedPath;
        }

        private static string GetRadioCategoryForContext(string contextLog, bool Aishi, bool whiteTusk)
        {
            string prefix = whiteTusk ? "W" : string.Empty;
            if (!string.IsNullOrEmpty(contextLog) && contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return prefix + (Aishi ? "DF" : "D");
            }

            if (!string.IsNullOrEmpty(contextLog) && contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return prefix + (Aishi ? "HF" : "H");
            }

            return prefix + (Aishi ? "NF" : "N");
        }

        private List<string> GetAudioPathsForContext(string contextLog, bool whiteTusk)
        {
            List<string> paths = new List<string>();
            if (whiteTusk)
            {
                if (contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_enableWhiteTusksCommon.Value && _enableDeathChatter.Value)
                        paths.AddRange(_whiteTusksCommonDeathAudioPaths);
                    if (_enableWhiteTuskAishi.Value && _enableAishiDeathChatter.Value)
                        paths.AddRange(_whiteTuskAishiDeathAudioPaths);
                }
                else if (contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_enableWhiteTusksCommon.Value && _enableHeavyDamageChatter.Value)
                        paths.AddRange(_whiteTusksCommonHeavyDamageAudioPaths);
                    if (_enableWhiteTuskAishi.Value && _enableAishiHeavyDamageChatter.Value)
                        paths.AddRange(_whiteTuskAishiHeavyDamageAudioPaths);
                }
                else
                {
                    if (_enableWhiteTusksCommon.Value && _enableRadioChatter.Value)
                        paths.AddRange(_whiteTusksCommonBotAudioPaths);
                    if (_enableWhiteTuskAishi.Value && _enableAishiRadioChatter.Value)
                        paths.AddRange(_whiteTuskAishiBotAudioPaths);
                }

                if (paths.Count > 0)
                    return paths;
            }

            if (contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableDeathChatter.Value)
                    paths.AddRange(_deathAudioPaths);
                if (_enableAishiDeathChatter.Value)
                    paths.AddRange(_AishiDeathAudioPaths);
                return paths;
            }

            if (contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableHeavyDamageChatter.Value)
                    paths.AddRange(_heavyDamageAudioPaths);
                if (_enableAishiHeavyDamageChatter.Value)
                    paths.AddRange(_AishiHeavyDamageAudioPaths);
                return paths;
            }

            if (_enableRadioChatter.Value)
                paths.AddRange(_botAudioPaths);
            if (_enableAishiRadioChatter.Value)
                paths.AddRange(_AishiBotAudioPaths);
            return paths;
        }

        private List<AudioClip> GetAudioClipsForContext(string contextLog, bool whiteTusk)
        {
            List<AudioClip> clips = new List<AudioClip>();
            if (whiteTusk)
            {
                if (contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_enableWhiteTusksCommon.Value && _enableDeathChatter.Value)
                        clips.AddRange(_wavsofWhiteTusksCommonDeaths);
                    if (_enableWhiteTuskAishi.Value && _enableAishiDeathChatter.Value)
                        clips.AddRange(_wavsofWhiteTuskAishiDeaths);
                }
                else if (contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_enableWhiteTusksCommon.Value && _enableHeavyDamageChatter.Value)
                        clips.AddRange(_wavsofWhiteTusksCommonHeavyDamage);
                    if (_enableWhiteTuskAishi.Value && _enableAishiHeavyDamageChatter.Value)
                        clips.AddRange(_wavsofWhiteTuskAishiHeavyDamage);
                }
                else
                {
                    if (_enableWhiteTusksCommon.Value && _enableRadioChatter.Value)
                        clips.AddRange(_wavsofWhiteTusksCommonBots);
                    if (_enableWhiteTuskAishi.Value && _enableAishiRadioChatter.Value)
                        clips.AddRange(_wavsofWhiteTuskAishiBots);
                }

                if (clips.Count > 0)
                    return clips;
            }

            if (contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableDeathChatter.Value)
                    clips.AddRange(_wavsofDeaths);
                if (_enableAishiDeathChatter.Value)
                    clips.AddRange(_wavsofAishiDeaths);
                return clips;
            }

            if (contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableHeavyDamageChatter.Value)
                    clips.AddRange(_wavsofHeavyDamage);
                if (_enableAishiHeavyDamageChatter.Value)
                    clips.AddRange(_wavsofAishiHeavyDamage);
                return clips;
            }

            if (_enableRadioChatter.Value)
                clips.AddRange(_wavsofBots);
            if (_enableAishiRadioChatter.Value)
                clips.AddRange(_wavsofAishiBots);
            return clips;
        }

        private bool HasAvailableAudioForContext(string contextLog)
        {
            List<string> normalPaths = GetAudioPathsForContext(contextLog, false);
            List<AudioClip> normalClips = GetAudioClipsForContext(contextLog, false);
            if (HasAvailableAudio(normalClips, normalPaths))
                return true;
            return HasAvailableAudio(GetAudioClipsForContext(contextLog, true), GetAudioPathsForContext(contextLog, true));
        }

        private bool IsAishiAudioPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            return ContainsAudioPath(_AishiBotAudioPaths, path) || ContainsAudioPath(_AishiDeathAudioPaths, path) || ContainsAudioPath(_AishiHeavyDamageAudioPaths, path) || ContainsAudioPath(_whiteTuskAishiBotAudioPaths, path) || ContainsAudioPath(_whiteTuskAishiDeathAudioPaths, path) || ContainsAudioPath(_whiteTuskAishiHeavyDamageAudioPaths, path);
        }

        private bool IsAishiAudioClip(AudioClip clip)
        {
            if (!IsAudioClipReady(clip))
                return false;
            return _wavsofAishiBots.Contains(clip) || _wavsofAishiDeaths.Contains(clip) || _wavsofAishiHeavyDamage.Contains(clip) || _wavsofWhiteTuskAishiBots.Contains(clip) || _wavsofWhiteTuskAishiDeaths.Contains(clip) || _wavsofWhiteTuskAishiHeavyDamage.Contains(clip);
        }

        private bool IsWhiteTuskAudioPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            return ContainsAudioPath(_whiteTusksCommonBotAudioPaths, path) || ContainsAudioPath(_whiteTusksCommonDeathAudioPaths, path) || ContainsAudioPath(_whiteTusksCommonHeavyDamageAudioPaths, path) || ContainsAudioPath(_whiteTuskAishiBotAudioPaths, path) || ContainsAudioPath(_whiteTuskAishiDeathAudioPaths, path) || ContainsAudioPath(_whiteTuskAishiHeavyDamageAudioPaths, path);
        }

        private bool IsWhiteTuskAudioClip(AudioClip clip)
        {
            if (!IsAudioClipReady(clip))
                return false;
            return _wavsofWhiteTusksCommonBots.Contains(clip) || _wavsofWhiteTusksCommonDeaths.Contains(clip) || _wavsofWhiteTusksCommonHeavyDamage.Contains(clip) || _wavsofWhiteTuskAishiBots.Contains(clip) || _wavsofWhiteTuskAishiDeaths.Contains(clip) || _wavsofWhiteTuskAishiHeavyDamage.Contains(clip);
        }

        private static bool ContainsAudioPath(List<string> paths, string path)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool HasAvailableAudio(List<AudioClip> clips, List<string> paths)
        {
            if (paths != null && paths.Count > 0)
                return true;
            if (clips == null)
                return false;
            clips.RemoveAll(clip => !IsAudioClipReady(clip));
            return clips.Count > 0;
        }

        private IEnumerator PlayISBSoldiersRadio(AudioClip clip, Transform parent, Vector3 position, float minDistance, float maxDistance, float baseVolume, ConfigEntry<float> volumeEntry, bool playMapAnnouncementSubtitle = false, float startOffsetSeconds = 0f, bool allowGateBTrigger = true, bool force2D = false, float spatialBlendOverride = -1f, AudioRolloffMode rolloffModeOverride = AudioRolloffMode.Logarithmic, Func<bool> keepPlaying = null)
        {
            if (keepPlaying != null && !keepPlaying())
                yield break;
            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning("ISB SOF Audio Plugin: Refusing to play an invalid audio clip" + (clip != null ? ": " + clip.name : "."));
                yield break;
            }

            _audioLastUsed[clip] = Time.realtimeSinceStartup;
            startOffsetSeconds = Mathf.Clamp(startOffsetSeconds, 0f, Mathf.Max(0f, clip.length - 0.05f));
            if (!CanPresentLocally)
            {
                float remaining = Mathf.Max(clip.length - startOffsetSeconds, 0.05f);
                yield return new WaitForSeconds(remaining);
                yield break;
            }

            GameObject obj = new GameObject("ISB_Audio");
            if (parent != null)
            {
                obj.transform.SetParent(parent);
                obj.transform.localPosition = position;
            }
            else
            {
                obj.transform.position = position;
            }

            if (IsRussianSpecialAnnouncement(clip.name) && ReferenceEquals(volumeEntry, _announcementsVolume))
            {
                baseVolume *= .6f;
                minDistance = Mathf.Min(minDistance, 4f);
                maxDistance = Mathf.Min(maxDistance, 220f);
            }
            float effectiveVolume = baseVolume * GetVolumeFactor(volumeEntry);
            AudioSource source = obj.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = force2D ? 0f : spatialBlendOverride >= 0f ? Mathf.Clamp01(spatialBlendOverride) : 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = force2D ? AudioRolloffMode.Logarithmic : rolloffModeOverride;
            source.dopplerLevel = 0f;
            source.spread = force2D || playMapAnnouncementSubtitle ? 0f : 55f;
            source.priority = 48;
            source.volume = effectiveVolume;
            source.loop = false;
            source.playOnAwake = false;
            ActiveISBAudio activeEntry = new ActiveISBAudio
            {
                Obj = obj,
                Source = source,
                BaseVolume = baseVolume,
                VolumeEntry = volumeEntry
            };
            _activeAudioObjects.Add(activeEntry);
            if (startOffsetSeconds > 0f)
                source.time = startOffsetSeconds;
            if (playMapAnnouncementSubtitle || ReferenceEquals(volumeEntry, _announcementsVolume) || ReferenceEquals(volumeEntry, _preBlackoutVolume))
                AnnouncementFade.Begin(source, clip, effectiveVolume, startOffsetSeconds);
            source.Play();
            if (playMapAnnouncementSubtitle && _subtitleSystem != null)
                _subtitleSystem.Play(source, clip.name);
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                Log.LogInfo($"ISB SOF Audio Plugin: AudioSource started | clip: {clip.name} | " + $"length: {clip.length:F2}s | " + $"volume: {effectiveVolume:F2} (base {baseVolume:F2} x slider {GetVolumeFactor(volumeEntry) * 100f:F0}%) | " + $"minDistance: {minDistance:F1} | " + $"maxDistance: {maxDistance:F1} | " + $"mode: {(force2D ? "2D" : source.spatialBlend < 0.999f ? "Hybrid " + source.spatialBlend.ToString("F2", CultureInfo.InvariantCulture) : "3D")} | " + $"rolloff: {source.rolloffMode}");
            }

            float destroyDelay = Mathf.Max((clip.length - startOffsetSeconds) + 0.25f, 0.25f);
            if (keepPlaying == null)
                yield return WaitAnnouncement(clip.name, destroyDelay, playMapAnnouncementSubtitle || ReferenceEquals(volumeEntry, _announcementsVolume), source);
            else
            {
                float stopAt = Time.time + destroyDelay;
                while (obj != null && Time.time < stopAt && keepPlaying())
                    yield return new WaitForSeconds(.25f);
            }

            if (obj != null)
                Destroy(obj);
            _activeAudioObjects.Remove(activeEntry);
        }
    }
}
