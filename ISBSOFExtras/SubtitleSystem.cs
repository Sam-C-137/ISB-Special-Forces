using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ISBSSOFExtra
{
    [Serializable]
    internal sealed class ISBSubtitleDatabase
    {
        public ISBSubtitleTrack[] tracks { get; set; } = new ISBSubtitleTrack[0];
    }

    [Serializable]
    internal sealed class ISBSubtitleTrack
    {
        public string clip { get; set; }
        public string speakerKey { get; set; }
        public string speakerFallback { get; set; }
        public float timingOffset { get; set; }
        public ISBSubtitleCue[] cues { get; set; } = new ISBSubtitleCue[0];
    }

    [Serializable]
    internal sealed class ISBSubtitleCue
    {
        public float start { get; set; }
        public float end { get; set; } = -1f;
        public string key { get; set; }
        public string fallback { get; set; }
    }

    [Serializable]
    internal sealed class ISBSubtitleLocaleFile
    {
        public ISBSubtitleLocaleEntry[] entries { get; set; } = new ISBSubtitleLocaleEntry[0];
    }

    [Serializable]
    internal sealed class ISBSubtitleLocaleEntry
    {
        public string key { get; set; }
        public string text { get; set; }
    }

    internal sealed class ISBNativeTerminalSubtitleRenderer
    {
        private const float SearchRetrySeconds = 2.5f;
        private GameObject _nativeInstance;
        private bool _foreground, _savedOverride;
        private int _savedOrder;
        private Canvas _foregroundCanvas;
        internal void SetForeground(bool value)
        {
            _foreground = value;
            ApplyForeground();
        }

        private void ApplyForeground()
        {
            if (_nativeInstance == null)
                return;
            if (_foreground && _foregroundCanvas == null)
            {
                _foregroundCanvas = _nativeInstance.GetComponent<Canvas>() ?? _nativeInstance.AddComponent<Canvas>();
                _savedOverride = _foregroundCanvas.overrideSorting;
                _savedOrder = _foregroundCanvas.sortingOrder;
            }

            if (_foregroundCanvas == null)
                return;
            _foregroundCanvas.overrideSorting = _foreground || _savedOverride;
            _foregroundCanvas.sortingOrder = _foreground ? 32761 : _savedOrder;
        }

        private NativeTextTarget _bodyTarget;
        private NativeTextTarget _speakerTarget;
        private readonly List<NativeTextTarget> _allTextTargets = new List<NativeTextTarget>();
        private float _nextSearchTime;
        private bool _visualPrepared;
        private string _lastText;
        public void ResetForRaid()
        {
            DestroyNativeInstance();
            _nextSearchTime = 0f;
        }

        public bool Prepare()
        {
            if (!EnsureNativeInstance())
                return false;
            if (!_visualPrepared)
                PrepareVisualInstance();
            ApplyForeground();
            return _nativeInstance != null && _bodyTarget != null && _visualPrepared;
        }

        public bool Show(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!Prepare())
                return false;
            if (string.Equals(_lastText, text, StringComparison.Ordinal) && _nativeInstance.activeSelf)
            {
                return true;
            }

            try
            {
                ApplyText(text);
                MakeVisible();
                _lastText = text;
                return true;
            }
            catch
            {
                DestroyNativeInstance();
                return false;
            }
        }

        public void Hide()
        {
            if (_nativeInstance == null)
                return;
            try
            {
                _nativeInstance.SetActive(false);
            }
            catch
            {
            }

            _lastText = null;
        }

        public void Dispose()
        {
            DestroyNativeInstance();
        }

        private bool EnsureNativeInstance()
        {
            if (_nativeInstance != null && _bodyTarget != null)
                return true;
            if (Time.realtimeSinceStartup < _nextSearchTime)
                return false;
            _nextSearchTime = Time.realtimeSinceStartup + SearchRetrySeconds;
            GameObject templateRoot = FindKnownSubtitleTemplate();
            return templateRoot != null && CreateNativeInstance(templateRoot);
        }

        private static GameObject FindKnownSubtitleTemplate()
        {
            try
            {
                Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
                for (int i = 0; i < canvases.Length; i++)
                {
                    Canvas canvas = canvases[i];
                    if (canvas == null || canvas.gameObject == null || IsOurObject(canvas.gameObject) || !canvas.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    Transform root = canvas.transform;
                    Transform found = null;
                    if (string.Equals(root.gameObject.name, "Common UI", StringComparison.OrdinalIgnoreCase))
                    {
                        found = root.Find("Common UI/Trader Dialog Screen/SubsContainer");
                        if (found == null)
                        {
                            found = root.Find("Trader Dialog Screen/SubsContainer");
                        }
                    }

                    if (found == null)
                    {
                        found = root.Find("Common UI/Common UI/Trader Dialog Screen/SubsContainer");
                    }

                    if (found == null || found.gameObject == null || IsOurObject(found.gameObject) || CountTextTargets(found.gameObject) == 0)
                    {
                        continue;
                    }

                    return found.gameObject;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool CreateNativeInstance(GameObject templateRoot)
        {
            try
            {
                Transform parent = ResolveNativeParent(templateRoot);
                if (parent == null)
                    return false;
                _nativeInstance = UnityEngine.Object.Instantiate(templateRoot, parent, false);
                _nativeInstance.name = "ISB_NativeTerminalSubtitle";
                SanitizeNativeInstance(_nativeInstance);
                RefreshTextTargets();
                if (_bodyTarget == null)
                {
                    DestroyNativeInstance();
                    return false;
                }

                PrepareVisualInstance();
                _nativeInstance.SetActive(false);
                return true;
            }
            catch
            {
                DestroyNativeInstance();
                return false;
            }
        }

        private Transform ResolveNativeParent(GameObject templateRoot)
        {
            Canvas templateCanvas = templateRoot.GetComponentInParent<Canvas>();
            if (templateCanvas != null && templateCanvas.renderMode != RenderMode.WorldSpace && templateCanvas.gameObject.scene.IsValid() && templateCanvas.gameObject.activeInHierarchy)
            {
                return templateCanvas.transform;
            }

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            Canvas best = canvases.Where(canvas => canvas != null && canvas.gameObject.scene.IsValid() && canvas.gameObject.activeInHierarchy && canvas.renderMode != RenderMode.WorldSpace && !IsOurObject(canvas.gameObject)).OrderByDescending(canvas => CanvasScore(canvas)).ThenByDescending(canvas => canvas.sortingOrder).FirstOrDefault();
            return best != null ? best.transform : null;
        }

        private static int CanvasScore(Canvas canvas)
        {
            if (canvas == null)
                return int.MinValue;
            int score = 0;
            string path = BuildHierarchyPath(canvas.transform).ToLowerInvariant();
            if (canvas.isRootCanvas)
                score += 40;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                score += 30;
            else if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                score += 20;
            if (ContainsAny(path, "battle", "game ui", "common ui", "hud", "screen"))
                score += 25;
            if (ContainsAny(path, "preloader", "menu", "inventory", "trader", "handbook"))
                score -= 20;
            score += Mathf.Clamp(canvas.sortingOrder, -10, 50);
            return score;
        }

        private void SanitizeNativeInstance(GameObject instance)
        {
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            Assembly eftAssembly = typeof(Player).Assembly;
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || IsRequiredUiBehaviour(behaviour))
                    continue;
                Type type = behaviour.GetType();
                string typeNamespace = type.Namespace ?? string.Empty;
                if (type.Assembly == eftAssembly || typeNamespace.StartsWith("EFT", StringComparison.Ordinal))
                {
                    behaviour.enabled = false;
                }
            }

            Animator[] animators = instance.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null)
                    animators[i].enabled = false;
            }

            Animation[] animations = instance.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++)
            {
                if (animations[i] != null)
                    animations[i].enabled = false;
            }

            Selectable[] selectables = instance.GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < selectables.Length; i++)
            {
                if (selectables[i] != null)
                    selectables[i].interactable = false;
            }

            CanvasGroup[] groups = instance.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                    continue;
                group.alpha = 1f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform transform = transforms[i];
                if (transform == null)
                    continue;
                Vector3 scale = transform.localScale;
                if (Mathf.Abs(scale.x) < 0.01f)
                    scale.x = 1f;
                if (Mathf.Abs(scale.y) < 0.01f)
                    scale.y = 1f;
                if (Mathf.Abs(scale.z) < 0.01f)
                    scale.z = 1f;
                transform.localScale = scale;
            }

            Graphic[] graphics = instance.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                    continue;
                graphic.enabled = true;
                graphic.raycastTarget = false;
                Color color = graphic.color;
                if (color.a < 0.05f)
                {
                    color.a = 1f;
                    graphic.color = color;
                }
            }
        }

        private static bool IsRequiredUiBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour is Graphic || behaviour is Selectable || behaviour is LayoutGroup || behaviour is ContentSizeFitter || behaviour is AspectRatioFitter)
            {
                return true;
            }

            string fullName = behaviour.GetType().FullName ?? string.Empty;
            return fullName.StartsWith("TMPro.", StringComparison.Ordinal) || fullName.StartsWith("UnityEngine.UI.", StringComparison.Ordinal);
        }

        private void RefreshTextTargets()
        {
            _allTextTargets.Clear();
            CollectTextTargets(_nativeInstance, _allTextTargets);
            for (int i = 0; i < _allTextTargets.Count; i++)
                _allTextTargets[i].SetText(string.Empty);
            _bodyTarget = _allTextTargets.OrderByDescending(target => ScoreBodyTarget(target)).FirstOrDefault();
            _speakerTarget = _allTextTargets.Where(target => target != _bodyTarget).OrderByDescending(target => ScoreSpeakerTarget(target)).FirstOrDefault();
        }

        private static int ScoreBodyTarget(NativeTextTarget target)
        {
            int score = Mathf.RoundToInt(target.FontSize * 3f);
            score += Mathf.RoundToInt(Mathf.Min(50000f, target.Area) / 1000f);
            string value = target.Path.ToLowerInvariant();
            if (ContainsAny(value, "body", "message", "subtitle", "caption", "description", "text"))
                score += 30;
            if (ContainsAny(value, "title", "speaker", "name", "source"))
                score -= 15;
            return score;
        }

        private static int ScoreSpeakerTarget(NativeTextTarget target)
        {
            int score = 0;
            string value = target.Path.ToLowerInvariant();
            if (ContainsAny(value, "title", "speaker", "name", "source", "header"))
                score += 50;
            score += Mathf.RoundToInt(Mathf.Max(0f, 40f - target.FontSize));
            return score;
        }

        private void ApplyText(string displayText)
        {
            string speaker = null;
            string body = displayText;
            int newline = displayText.IndexOf('\n');
            if (newline > 0)
            {
                speaker = displayText.Substring(0, newline).Trim();
                body = displayText.Substring(newline + 1).Trim();
            }

            if (_speakerTarget != null)
                _speakerTarget.SetText(string.Empty);
            if (_speakerTarget != null && !string.IsNullOrWhiteSpace(speaker))
            {
                _speakerTarget.SetText(speaker.Trim('[', ']'));
                _bodyTarget.SetText(body);
            }
            else
            {
                _bodyTarget.SetText(displayText);
            }
        }

        private void PrepareVisualInstance()
        {
            if (_nativeInstance == null || _bodyTarget == null || _visualPrepared)
                return;
            _nativeInstance.SetActive(true);
            SetHierarchyActive(_bodyTarget.GameObject, _nativeInstance.transform);
            SetHierarchyActive(_speakerTarget != null ? _speakerTarget.GameObject : null, _nativeInstance.transform);
            for (int i = 0; i < _allTextTargets.Count; i++)
                _allTextTargets[i].EnsureVisible();
            CanvasGroup[] groups = _nativeInstance.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null)
                    groups[i].alpha = 1f;
            }

            RectTransform rect = _nativeInstance.GetComponent<RectTransform>();
            if (rect != null)
            {
                ForceOnScreen(rect);
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }

            Canvas.ForceUpdateCanvases();
            _nativeInstance.SetActive(false);
            _visualPrepared = true;
        }

        private void MakeVisible()
        {
            if (!_visualPrepared)
                PrepareVisualInstance();
            _nativeInstance.SetActive(true);
            _nativeInstance.transform.SetAsLastSibling();
        }

        private static void SetHierarchyActive(GameObject target, Transform stopAt)
        {
            if (target == null)
                return;
            Transform current = target.transform;
            while (current != null)
            {
                current.gameObject.SetActive(true);
                if (current == stopAt)
                    break;
                current = current.parent;
            }
        }

        private static void ForceOnScreen(RectTransform rect)
        {
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 150f);
            rect.localRotation = Quaternion.identity;
            Vector2 size = rect.sizeDelta;
            if (Mathf.Abs(size.x) < 320f)
                size.x = 1100f;
            if (Mathf.Abs(size.y) < 60f)
                size.y = 160f;
            rect.sizeDelta = size;
        }

        private static void CollectTextTargets(GameObject root, List<NativeTextTarget> targets)
        {
            Text[] unityTexts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < unityTexts.Length; i++)
            {
                Text text = unityTexts[i];
                if (text == null)
                    continue;
                targets.Add(NativeTextTarget.FromUnityText(text));
            }

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour is Text)
                    continue;
                if (!IsTextComponentType(behaviour.GetType()))
                    continue;
                NativeTextTarget target = NativeTextTarget.FromReflectedComponent(behaviour);
                if (target != null)
                    targets.Add(target);
            }
        }

        private static int CountTextTargets(GameObject root)
        {
            var targets = new List<NativeTextTarget>();
            CollectTextTargets(root, targets);
            return targets.Count;
        }

        private static bool IsTextComponentType(Type type)
        {
            if (type == null)
                return false;
            if (typeof(Text).IsAssignableFrom(type))
                return true;
            string fullName = type.FullName ?? string.Empty;
            if (fullName.StartsWith("TMPro.", StringComparison.Ordinal) || fullName.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return property != null && property.PropertyType == typeof(string) && property.CanWrite;
            }

            return false;
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsOurObject(GameObject gameObject)
        {
            return gameObject != null && gameObject.name.StartsWith("ISB_NativeTerminalSubtitle", StringComparison.Ordinal);
        }

        private void DestroyNativeInstance()
        {
            if (_nativeInstance != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_nativeInstance);
                }
                catch
                {
                }
            }

            _nativeInstance = null;
            _foregroundCanvas = null;
            _bodyTarget = null;
            _speakerTarget = null;
            _allTextTargets.Clear();
            _visualPrepared = false;
            _lastText = null;
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";
            var names = new Stack<string>();
            Transform current = transform;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private sealed class NativeTextTarget
        {
            private readonly Component _component;
            private readonly Text _unityText;
            private readonly PropertyInfo _textProperty;
            private readonly PropertyInfo _fontSizeProperty;
            public string Path { get; private set; }
            public float FontSize { get; private set; }
            public float Area { get; private set; }

            private NativeTextTarget(Component component, Text unityText, PropertyInfo textProperty, PropertyInfo fontSizeProperty)
            {
                _component = component;
                _unityText = unityText;
                _textProperty = textProperty;
                _fontSizeProperty = fontSizeProperty;
                Path = BuildHierarchyPath(component.transform);
                FontSize = ReadFontSize();
                RectTransform rect = component.GetComponent<RectTransform>();
                Area = rect != null ? Mathf.Abs(rect.rect.width * rect.rect.height) : 0f;
            }

            public static NativeTextTarget FromUnityText(Text text)
            {
                return new NativeTextTarget(text, text, null, null);
            }

            public static NativeTextTarget FromReflectedComponent(Component component)
            {
                Type type = component.GetType();
                PropertyInfo textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
                {
                    return null;
                }

                PropertyInfo fontSizeProperty = type.GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return new NativeTextTarget(component, null, textProperty, fontSizeProperty);
            }

            public GameObject GameObject
            {
                get
                {
                    return _component != null ? _component.gameObject : null;
                }
            }

            public void EnsureVisible()
            {
                try
                {
                    if (_component == null)
                        return;
                    Behaviour behaviour = _component as Behaviour;
                    if (behaviour != null)
                        behaviour.enabled = true;
                    Graphic graphic = _component as Graphic;
                    if (graphic != null)
                    {
                        graphic.enabled = true;
                        Color color = graphic.color;
                        if (color.a < 0.05f)
                        {
                            color.a = 1f;
                            graphic.color = color;
                        }
                    }
                    else
                    {
                        PropertyInfo colorProperty = _component.GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (colorProperty != null && colorProperty.CanRead && colorProperty.CanWrite && colorProperty.PropertyType == typeof(Color))
                        {
                            Color color = (Color)colorProperty.GetValue(_component, null);
                            if (color.a < 0.05f)
                            {
                                color.a = 1f;
                                colorProperty.SetValue(_component, color, null);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            public void SetText(string value)
            {
                try
                {
                    if (_unityText != null)
                    {
                        _unityText.text = value ?? string.Empty;
                        return;
                    }

                    if (_textProperty != null)
                        _textProperty.SetValue(_component, value ?? string.Empty, null);
                }
                catch
                {
                }
            }

            private float ReadFontSize()
            {
                try
                {
                    if (_unityText != null)
                        return _unityText.fontSize;
                    if (_fontSizeProperty != null)
                    {
                        object value = _fontSizeProperty.GetValue(_component, null);
                        if (value != null)
                            return Convert.ToSingle(value);
                    }
                }
                catch
                {
                }

                return 20f;
            }
        }
    }

    internal sealed class ISBSubtitleSystem
    {
        private const string LocaleMarkerKey = "isb_subtitle_locale_code";
        private const string AutomaticLanguage = "Auto";
        private readonly MonoBehaviour _host;
        private readonly ManualLogSource _log;
        private readonly string _subtitlesFolder;
        private readonly string _localesFolder;
        private readonly ConfigEntry<bool> _enabled;
        private readonly ConfigEntry<string> _language;
        private readonly Dictionary<string, ISBSubtitleTrack> _tracks = new Dictionary<string, ISBSubtitleTrack>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _localeEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _englishEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ISBNativeTerminalSubtitleRenderer _nativeRenderer;
        internal void SetForeground(bool value) => _nativeRenderer.SetForeground(value);
        private Coroutine _playbackRoutine;
        private Coroutine _prewarmRoutine;
        private string _loadedLocale;
        private MethodInfo _localizedMethod;
        private bool _localizedMethodSearched;
        public ISBSubtitleSystem(MonoBehaviour host, ManualLogSource log, string dataFolder, ConfigEntry<bool> enabled, ConfigEntry<string> language)
        {
            _host = host;
            _log = log;
            _subtitlesFolder = Path.Combine(dataFolder, "Subtitles");
            _localesFolder = Path.Combine(_subtitlesFolder, "locales");
            _enabled = enabled;
            _language = language;
            _nativeRenderer = new ISBNativeTerminalSubtitleRenderer();
            _enabled.SettingChanged += OnEnabledSettingChanged;
            _language.SettingChanged += OnLanguageSettingChanged;
            Reload();
        }

        public void Reload()
        {
            StopPrewarmRoutine();
            Stop();
            _nativeRenderer.ResetForRaid();
            LoadTracks();
            LoadLocale(ResolveRequestedLocale());
        }

        public void Prewarm()
        {
            StopPrewarmRoutine();
            if (!_enabled.Value)
                return;
            _prewarmRoutine = _host.StartCoroutine(PrewarmRoutine());
        }

        public void Play(AudioSource source, string clipName)
        {
            if (!_enabled.Value || source == null || string.IsNullOrEmpty(clipName))
                return;
            EnsureCurrentLocaleLoaded();
            string normalizedClipName = Path.GetFileNameWithoutExtension(clipName);
            ISBSubtitleTrack track;
            if (!_tracks.TryGetValue(normalizedClipName, out track) || track == null || track.cues == null || track.cues.Length == 0)
            {
                return;
            }

            StopPlaybackRoutine();
            _playbackRoutine = _host.StartCoroutine(PlaybackRoutine(source, track));
        }

        public void Stop()
        {
            StopPlaybackRoutine();
            _nativeRenderer.Hide();
        }

        public void Dispose()
        {
            _enabled.SettingChanged -= OnEnabledSettingChanged;
            _language.SettingChanged -= OnLanguageSettingChanged;
            StopPrewarmRoutine();
            Stop();
            _nativeRenderer.Dispose();
        }

        private void OnEnabledSettingChanged(object sender, EventArgs args)
        {
            if (!_enabled.Value)
            {
                StopPrewarmRoutine();
                Stop();
            }
            else
            {
                Prewarm();
            }
        }

        private void OnLanguageSettingChanged(object sender, EventArgs args)
        {
            LoadLocale(ResolveRequestedLocale());
        }

        private IEnumerator PrewarmRoutine()
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(1f);
            float timeoutAt = Time.realtimeSinceStartup + 12f;
            while (_enabled.Value && Time.realtimeSinceStartup < timeoutAt)
            {
                if (_nativeRenderer.Prepare())
                {
                    if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                    {
                        _log.LogInfo("ISB SOF Audio Plugin: Native subtitle renderer prewarmed; " + "announcement display will reuse the prepared panel.");
                    }

                    _prewarmRoutine = null;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.75f);
            }

            _log.LogWarning("ISB SOF Audio Plugin: Native subtitle renderer prewarm did not finish before timeout. " + "It will retry when an announcement starts.");
            _prewarmRoutine = null;
        }

        private IEnumerator PlaybackRoutine(AudioSource source, ISBSubtitleTrack track)
        {
            ISBSubtitleCue[] orderedCues = track.cues.Where(cue => cue != null).OrderBy(cue => Mathf.Max(0f, cue.start)).ToArray();
            float startupTimeout = Time.realtimeSinceStartup + 1f;
            while (source != null && !source.isPlaying && Time.realtimeSinceStartup < startupTimeout)
                yield return null;
            for (int i = 0; i < orderedCues.Length; i++)
            {
                if (!IsSourcePlaying(source))
                    break;
                ISBSubtitleCue cue = orderedCues[i];
                float timingOffset = track.timingOffset;
                float start = Mathf.Max(0f, cue.start + timingOffset);
                float clipLength = GetClipLength(source, start + 1f);
                float nextStart = -1f;
                if (i + 1 < orderedCues.Length)
                    nextStart = Mathf.Max(0f, orderedCues[i + 1].start + timingOffset);
                float explicitEnd = cue.end > cue.start ? cue.end + timingOffset : -1f;
                float end = explicitEnd > start ? explicitEnd : nextStart > start ? nextStart : Mathf.Max(clipLength, start + 0.05f);
                end = Mathf.Min(end, Mathf.Max(clipLength, start + 0.05f));
                while (_enabled.Value && IsSourcePlaying(source) && GetSourceTime(source) < start)
                    yield return null;
                if (!_enabled.Value || !IsSourcePlaying(source))
                    break;
                string displayText = ResolveText(cue.key, cue.fallback);
                if (string.IsNullOrWhiteSpace(displayText))
                    continue;
                bool visible = false;
                while (_enabled.Value && IsSourcePlaying(source) && GetSourceTime(source) <= end)
                {
                    bool shouldDisplay = ShouldDisplayForSource(source);
                    if (shouldDisplay && !visible)
                        visible = _nativeRenderer.Show(displayText);
                    else if (!shouldDisplay && visible)
                    {
                        _nativeRenderer.Hide();
                        visible = false;
                    }

                    yield return null;
                }

                bool nextCueIsContiguous = i + 1 < orderedCues.Length && nextStart > start && Mathf.Abs(nextStart - end) <= 0.08f;
                if (!nextCueIsContiguous)
                    _nativeRenderer.Hide();
            }

            _nativeRenderer.Hide();
            _playbackRoutine = null;
        }

        private static bool IsSourcePlaying(AudioSource source)
        {
            try
            {
                return source != null && source.isPlaying;
            }
            catch
            {
                return false;
            }
        }

        private static float GetSourceTime(AudioSource source)
        {
            try
            {
                return source != null ? source.time : float.MaxValue;
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static float GetClipLength(AudioSource source, float fallback)
        {
            try
            {
                return source != null && source.clip != null ? source.clip.length : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ShouldDisplayForSource(AudioSource source)
        {
            try
            {
                return source != null && source.volume > 0.001f;
            }
            catch
            {
                return false;
            }
        }

        private void StopPrewarmRoutine()
        {
            if (_prewarmRoutine == null)
                return;
            try
            {
                _host.StopCoroutine(_prewarmRoutine);
            }
            catch
            {
            }

            _prewarmRoutine = null;
        }

        private void StopPlaybackRoutine()
        {
            if (_playbackRoutine == null)
                return;
            try
            {
                _host.StopCoroutine(_playbackRoutine);
            }
            catch
            {
            }

            _playbackRoutine = null;
        }

        private void LoadTracks()
        {
            _tracks.Clear();
            string cuesPath = Path.Combine(_subtitlesFolder, "cues.json");
            try
            {
                if (!File.Exists(cuesPath))
                {
                    _log.LogWarning("ISB SOF Audio Plugin: Subtitle cue file was not found at '" + cuesPath + "'.");
                    return;
                }

                string json = File.ReadAllText(cuesPath, Encoding.UTF8);
                ISBSubtitleDatabase database = JsonConvert.DeserializeObject<ISBSubtitleDatabase>(json);
                if (database == null)
                {
                    _log.LogWarning("ISB SOF Audio Plugin: cues.json returned a null subtitle database.");
                    return;
                }

                if (database.tracks == null)
                {
                    _log.LogWarning("ISB SOF Audio Plugin: cues.json does not contain a valid 'tracks' array.");
                    return;
                }

                foreach (ISBSubtitleTrack track in database.tracks)
                {
                    if (track == null || string.IsNullOrWhiteSpace(track.clip))
                        continue;
                    string clipName = Path.GetFileNameWithoutExtension(track.clip.Trim());
                    _tracks[clipName] = track;
                }

                if (_tracks.Count == 0)
                {
                    _log.LogWarning("ISB SOF Audio Plugin: cues.json was read successfully, but no usable " + "subtitle tracks were found. Each track needs a non-empty 'clip' field.");
                }

                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    _log.LogInfo("ISB SOF Audio Plugin: Loaded " + _tracks.Count + " subtitle track definitions from '" + cuesPath + "'.");
                }
            }
            catch (JsonException ex)
            {
                _log.LogWarning("ISB SOF Audio Plugin: Invalid subtitle cue JSON at '" + cuesPath + "': " + ex.Message);
            }
            catch (Exception)
            {
            }
        }

        private void EnsureCurrentLocaleLoaded()
        {
            string requestedLocale = ResolveRequestedLocale();
            if (!string.Equals(requestedLocale, _loadedLocale, StringComparison.OrdinalIgnoreCase))
                LoadLocale(requestedLocale);
        }

        private void LoadLocale(string localeCode)
        {
            _localeEntries.Clear();
            _englishEntries.Clear();
            LoadLocaleFile("en", _englishEntries);
            string selected = FindAvailableLocale(localeCode);
            if (!string.Equals(selected, "en", StringComparison.OrdinalIgnoreCase))
                LoadLocaleFile(selected, _localeEntries);
            else
            {
                foreach (KeyValuePair<string, string> pair in _englishEntries)
                    _localeEntries[pair.Key] = pair.Value;
            }

            _loadedLocale = selected;
            if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
            {
                _log.LogInfo("ISB SOF Audio Plugin: Subtitle locale loaded: " + _loadedLocale + ".");
            }
        }

        private void LoadLocaleFile(string localeCode, Dictionary<string, string> target)
        {
            string path = Path.Combine(_localesFolder, localeCode + ".json");
            try
            {
                if (!File.Exists(path))
                {
                    _log.LogWarning("ISB SOF Audio Plugin: Subtitle locale file was not found at '" + path + "'.");
                    return;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                ISBSubtitleLocaleFile locale = JsonConvert.DeserializeObject<ISBSubtitleLocaleFile>(json);
                if (locale == null)
                {
                    _log.LogWarning("ISB SOF Audio Plugin: Locale file '" + localeCode + "' returned a null object.");
                    return;
                }

                if (locale.entries == null)
                {
                    _log.LogWarning("ISB SOF Audio Plugin: Locale file '" + localeCode + "' does not contain a valid 'entries' array.");
                    return;
                }

                int loadedEntries = 0;
                foreach (ISBSubtitleLocaleEntry entry in locale.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                        continue;
                    target[entry.key.Trim()] = entry.text ?? string.Empty;
                    loadedEntries++;
                }

                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    _log.LogInfo("ISB SOF Audio Plugin: Loaded " + loadedEntries + " subtitle locale entries from '" + path + "'.");
                }
            }
            catch (JsonException ex)
            {
                _log.LogWarning("ISB SOF Audio Plugin: Invalid subtitle locale JSON at '" + path + "': " + ex.Message);
            }
            catch (Exception)
            {
            }
        }

        private string ResolveText(string key, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                string text;
                if (_localeEntries.TryGetValue(key, out text) && !string.IsNullOrWhiteSpace(text))
                    return text;
                if (_englishEntries.TryGetValue(key, out text) && !string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return fallback ?? string.Empty;
        }

        private string ResolveRequestedLocale()
        {
            string configured = _language.Value;
            if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured.Trim(), AutomaticLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeLocale(configured);
            }

            string gameLocale = ResolveGameLocaleMarker();
            if (!string.IsNullOrWhiteSpace(gameLocale))
                return NormalizeLocale(gameLocale);
            return NormalizeSystemLanguage(Application.systemLanguage);
        }

        private string ResolveGameLocaleMarker()
        {
            try
            {
                MethodInfo method = GetLocalizedMethod();
                if (method == null)
                    return null;
                const string missing = "__isb_subtitle_locale_missing__";
                string value = method.Invoke(null, new object[] { LocaleMarkerKey, missing }) as string;
                return string.IsNullOrWhiteSpace(value) || string.Equals(value, missing, StringComparison.Ordinal) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        private MethodInfo GetLocalizedMethod()
        {
            if (_localizedMethodSearched)
                return _localizedMethod;
            _localizedMethodSearched = true;
            Type[] types;
            try
            {
                types = typeof(Player).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray();
            }

            _localizedMethod = types.SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)).FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "Localized", StringComparison.Ordinal) || method.ReturnType != typeof(string))
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string);
            });
            return _localizedMethod;
        }

        private string FindAvailableLocale(string requested)
        {
            string normalized = NormalizeLocale(requested);
            string exactPath = Path.Combine(_localesFolder, normalized + ".json");
            if (File.Exists(exactPath))
                return normalized;
            int separator = normalized.IndexOf('-');
            if (separator > 0)
            {
                string shortCode = normalized.Substring(0, separator);
                if (File.Exists(Path.Combine(_localesFolder, shortCode + ".json")))
                    return shortCode;
            }

            return "en";
        }

        private static string NormalizeLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return "en";
            string normalized = locale.Trim().Replace('_', '-').ToLowerInvariant();
            if (normalized == "english")
                return "en";
            if (normalized == "br" || normalized == "po")
                return "pt";
            if (normalized == "portuguese" || normalized == "português")
                return "pt";
            return normalized;
        }

        private static string NormalizeSystemLanguage(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Portuguese:
                    return "po";
                default:
                    return "en";
            }
        }
    }
}
