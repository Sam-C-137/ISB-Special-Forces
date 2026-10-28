using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;
using ISBSpecialForces.Audio;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    internal sealed class ISBChaosVoice : MonoBehaviour
    {
        private struct Speech { internal TaggedClip Clip; internal float Time; }
        private sealed class Rendered
        {
            internal AudioClip Clip;
            internal Task<float[]> Work;
            internal int Frames, Channels, Rate, Bytes;
            internal float Used;
        }
        private static ISBChaosVoice _instance;
        private readonly Dictionary<string, bool> _known = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<Player> _pending = new HashSet<Player>();
        private readonly Dictionary<Player, Speech> _speech = new Dictionary<Player, Speech>();
        private readonly Dictionary<int, Rendered> _clips = new Dictionary<int, Rendered>();
        private readonly Dictionary<Player, Coroutine> _waiting = new Dictionary<Player, Coroutine>();
        private readonly HashSet<int> _failed = new HashSet<int>();
        private static readonly System.Threading.SemaphoreSlim Workers = new System.Threading.SemaphoreSlim(2, 2);
        private int _cachedBytes;
        private const int CacheLimit = 32 * 1024 * 1024;
        private Coroutine _refresh;
        private int _generation;
        private bool _failureLogged;
        private AssetBundle _readableSources;
        private AssetBundleCreateRequest _sourceLoad;
        private bool _sourcesUnavailable;
        private readonly Dictionary<int, int> _sourceReaders = new Dictionary<int, int>();

        internal static void Initialize(Plugin plugin)
        {
            _instance = plugin.gameObject.AddComponent<ISBChaosVoice>();
            var harmony = new Harmony("com.samc137.isbspecialforces.chaosvoice");
            harmony.Patch(AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer)), postfix: new HarmonyMethod(typeof(ISBChaosVoice), nameof(PlayerRegistered)));
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.PlaySpeechFromTime)), prefix: new HarmonyMethod(typeof(ISBChaosVoice), nameof(BeforeSpeech)));
            GameWorld.OnDispose += _instance.ClearRaid;
        }

        internal static void Remember(IEnumerable<Profile> profiles, string variant)
        {
            var instance = _instance;
            if (instance == null || !ISBFikaAuthority.CanPresentLocally) return;
            bool chaos = string.Equals(variant, ISB.Compatibility.GhostBotVariant.ChaosName, StringComparison.OrdinalIgnoreCase);
            foreach (var profile in profiles)
                if (!string.IsNullOrEmpty(profile?.Id)) instance._known[profile.Id] = chaos;
        }

        private static bool Eligible(Player player) => player != null && !player.IsYourPlayer && (int?)player.Profile?.Info?.Settings?.Role == 848421;
        private static void PlayerRegistered(IPlayer iPlayer)
        {
            var player = iPlayer as Player;
            if (Eligible(player) && ISBFikaAuthority.CanPresentLocally) _instance.Queue(player);
        }

        private void Queue(Player player)
        {
            if (_known.ContainsKey(player.ProfileId)) return;
            _pending.Add(player);
            if (_refresh == null) _refresh = StartCoroutine(ResolveBatch());
        }

        private static bool BeforeSpeech(Player __instance, ref TaggedClip clip, float time)
        {
            if (_instance == null || !ISBFikaAuthority.CanPresentLocally) return true;
            if (!Eligible(__instance) || clip?.Clip == null) return true;
            if (_instance._known.TryGetValue(__instance.ProfileId, out bool chaos))
            {
                if (!chaos || _instance._failed.Contains(clip.Clip.GetInstanceID())) return true;
                return _instance.Prepare(__instance, ref clip, time);
            }
            _instance._speech[__instance] = new Speech { Clip = clip, Time = time };
            _instance.Queue(__instance);
            return false;
        }

        private bool Prepare(Player player, ref TaggedClip clip, float time)
        {
            int id = clip.Clip.GetInstanceID();
            if (_clips.TryGetValue(id, out var rendered) && rendered.Clip != null)
            {
                rendered.Used = Time.realtimeSinceStartup;
                clip = new TaggedClip { Clip = rendered.Clip, Mask = clip.Mask, Volume = clip.Volume, Falloff = clip.Falloff, NetId = clip.NetId, Length = clip.Length, Exclude = clip.Exclude };
                return true;
            }
            if (_waiting.TryGetValue(player, out var old)) StopCoroutine(old);
            var speech = new Speech { Clip = clip, Time = time };
            _waiting[player] = StartCoroutine(RenderSpeech(player, speech));
            return false;
        }

        private IEnumerator RenderSpeech(Player player, Speech speech)
        {
            yield return null;
            int generation = _generation;
            var original = speech.Clip.Clip;
            if (original == null) { _waiting.Remove(player); yield break; }
            int id = original.GetInstanceID();
            if (!_clips.TryGetValue(id, out var rendered))
            {
                float loadDeadline = Time.realtimeSinceStartup + .5f;
                if (original.loadState == AudioDataLoadState.Unloaded) original.LoadAudioData();
                while (original.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < loadDeadline) yield return null;
                if (generation != _generation) yield break;
                if (_clips.TryGetValue(id, out rendered)) goto WaitForRender;
                if (original.channels < 1 || original.channels > 2 || original.samples > original.frequency * 30 || original.frequency < 8000 || original.frequency > 192000)
                {
                    Failed(id); _waiting.Remove(player); Replay(player, speech); yield break;
                }
                var readable = original;
                if (original.loadType == AudioClipLoadType.Streaming)
                {
                    if (!_sourcesUnavailable && _readableSources == null)
                    {
                        if (_sourceLoad == null)
                        {
                            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Assets", "isb-ci-readable-speech.bundle");
                            if (System.IO.File.Exists(path))
                            {
                                var load = AssetBundle.LoadFromFileAsync(path);
                                _sourceLoad = load;
                                load.completed += _ => { if (generation != _generation && load.assetBundle != null) load.assetBundle.Unload(true); };
                            }
                            else _sourcesUnavailable = true;
                        }
                        var request = _sourceLoad;
                        if (request != null)
                        {
                            while (!request.isDone && generation == _generation) yield return null;
                            if (generation != _generation) yield break;
                            _readableSources = request.assetBundle;
                            _sourcesUnavailable = _readableSources == null;
                        }
                    }
                    if (_readableSources != null)
                    {
                        _sourceReaders.TryGetValue(id, out int readers);
                        _sourceReaders[id] = readers + 1;
                        var request = _readableSources.LoadAssetAsync<AudioClip>("ci-source/" + original.name.ToLowerInvariant());
                        while (!request.isDone && generation == _generation) yield return null;
                        if (generation != _generation) yield break;
                        readable = request.asset as AudioClip;
                    }
                    else readable = null;
                    if (readable == null || readable.channels != original.channels || readable.frequency != original.frequency || readable.samples != original.samples)
                    {
                        ReleaseSource(id, readable);
                        Failed(id); _waiting.Remove(player); Replay(player, speech); yield break;
                    }
                    if (_clips.TryGetValue(id, out rendered)) { ReleaseSource(id, readable); goto WaitForRender; }
                }
                var samples = new float[original.samples * original.channels];
                bool read = readable.GetData(samples, 0);
                if (readable != original) ReleaseSource(id, readable);
                if (!read)
                {
                    Failed(id); _waiting.Remove(player); Replay(player, speech); yield break;
                }
                rendered = new Rendered { Frames = original.samples, Channels = original.channels, Rate = original.frequency, Bytes = samples.Length * 4 };
                int channels = rendered.Channels, rate = rendered.Rate;
                rendered.Work = Task.Run(async () =>
                {
                    await Workers.WaitAsync().ConfigureAwait(false);
                    try { return generation == System.Threading.Volatile.Read(ref _generation) ? ChaosVoiceDsp.Render(samples, channels, rate) : Array.Empty<float>(); }
                    finally { Workers.Release(); }
                });
                _clips[id] = rendered;
            }
            WaitForRender:
            while (rendered.Work != null && !rendered.Work.IsCompleted && generation == _generation) yield return null;
            if (generation != _generation) yield break;
            if (rendered.Clip == null)
            {
                if (rendered.Work.Status != TaskStatus.RanToCompletion)
                {
                    var error = rendered.Work.Exception;
                    _clips.Remove(id); Failed(id);
                }
                else
                {
                    TrimCache(rendered.Bytes);
                    rendered.Clip = AudioClip.Create("CI mask F: " + original.name, rendered.Frames, rendered.Channels, rendered.Rate, false);
                    rendered.Clip.hideFlags = HideFlags.HideAndDontSave;
                    rendered.Clip.SetData(rendered.Work.Result, 0);
                    rendered.Used = Time.realtimeSinceStartup;
                    rendered.Work = null;
                    _cachedBytes += rendered.Bytes;
                }
            }
            _waiting.Remove(player);
            Replay(player, speech);
        }

        private static void Replay(Player player, Speech speech)
        {
            if (player == null || speech.Clip?.Clip == null) return;
            var speaker = player.Speaker;
            if (speaker != null && speaker.Speaking && ReferenceEquals(speaker.Clip, speech.Clip))
            {
                float end = Time.time + Math.Max(0, speech.Clip.Clip.length - speech.Time) + .1f;
                float delay = Math.Max(0, end - speaker.ReleaseTime);
                speaker.ReleaseTime += delay;
                if (speaker.QueuedEvent != EPhraseTrigger.PhraseNone) speaker.QueuedDelay += delay;
            }
            player.PlaySpeechFromTime(speech.Clip, speech.Time);
        }

        private void ReleaseSource(int id, AudioClip clip)
        {
            if (_sourceReaders.TryGetValue(id, out int readers) && readers > 1) { _sourceReaders[id] = readers - 1; return; }
            _sourceReaders.Remove(id);
            if (clip != null) Resources.UnloadAsset(clip);
        }

        private void TrimCache(int incoming)
        {
            while (_cachedBytes + incoming > CacheLimit)
            {
                float now = Time.realtimeSinceStartup;
                var oldest = _clips.Where(p => p.Value.Clip != null && now - p.Value.Used > p.Value.Clip.length + .5f).OrderBy(p => p.Value.Used).FirstOrDefault();
                if (oldest.Value == null) break;
                _cachedBytes -= oldest.Value.Bytes;
                Destroy(oldest.Value.Clip);
                _clips.Remove(oldest.Key);
            }
        }

        private void Failed(int id)
        {
            _failed.Add(id);
            if (_failureLogged) return;
            _failureLogged = true;
            Plugin.LogSource.LogError("ISB CI speech processing failed; original speech is preserved.");
        }

        private IEnumerator ResolveBatch()
        {
            int generation = _generation;
            yield return null;
            while (_pending.Count > 0 && generation == _generation)
            {
                var players = _pending.ToArray();
                _pending.Clear();
                Task<Dictionary<string, string>> task = GhostBotSpawnBridge.FetchVoiceIdentities();
                float deadline = Time.realtimeSinceStartup + 3f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline && generation == _generation) yield return null;
                if (generation != _generation) yield break;
                Dictionary<string, string> names = null;
                if (task.Status == TaskStatus.RanToCompletion) names = task.Result;
                else
                {
                    _ = task.ContinueWith(failed => { var error = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    if (!_failureLogged)
                    {
                        _failureLogged = true;
                        Plugin.LogSource.LogError("ISB CI voice identity lookup failed; original speech is preserved.");
                    }
                }
                foreach (var player in players)
                {
                    if (player == null || string.IsNullOrEmpty(player.ProfileId)) continue;
                    if (!_known.ContainsKey(player.ProfileId))
                        _known[player.ProfileId] = names != null && names.TryGetValue("profile|" + player.ProfileId, out string role) && role == "ScavRole/chaosInsurgency";
                    if (!_speech.TryGetValue(player, out var speech)) continue;
                    _speech.Remove(player);
                    if (speech.Clip?.Clip != null) Replay(player, speech);
                }
                if (_pending.Count > 0) yield return null;
            }
            _refresh = null;
        }

        private void ClearRaid()
        {
            _generation++;
            if (_refresh != null) StopCoroutine(_refresh);
            _refresh = null;
            _known.Clear(); _pending.Clear(); _speech.Clear();
            foreach (var waiting in _waiting.Values) if (waiting != null) StopCoroutine(waiting);
            _waiting.Clear();
            foreach (var rendered in _clips.Values)
            {
                if (rendered.Clip != null) Destroy(rendered.Clip);
                if (rendered.Work != null) _ = rendered.Work.ContinueWith(failed => { var error = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            _clips.Clear(); _failed.Clear(); _cachedBytes = 0; _failureLogged = false;
            if (_readableSources != null) _readableSources.Unload(true);
            _readableSources = null; _sourceLoad = null; _sourcesUnavailable = false;
            _sourceReaders.Clear();
        }

        private void OnDestroy()
        {
            GameWorld.OnDispose -= ClearRaid;
            ClearRaid();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }

}
