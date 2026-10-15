using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class DecontaminationScreens : IDisposable
    {
        private sealed class Surface
        {
            internal Material Source;
            internal Material Material;
            internal Rect Area;
            internal float Aspect;
            internal DecontaminationScreenFrame Frame;
            internal bool Office, Terminal;
        }

        private sealed class Binding
        {
            internal Renderer Renderer;
            internal GameObject Owner;
            internal Material[] Original;
            internal Material[] Assigned;
            internal Texture[] MainTextures, EmissionTextures;
            internal Vector4[] TextureTransforms;
            internal DecontaminationScreenFrame[] Frames;
            internal MaterialPropertyBlock[] SavedBlocks;
            internal List<GameObject> Activated;
            internal bool EnabledRenderer;
            internal void EnsureOfficeVisible()
            {
                if (Activated == null)
                    return;
                foreach (var obj in Activated)
                    if (obj != null && !obj.activeSelf)
                        obj.SetActive(true);
                if (Renderer != null && !Renderer.enabled)
                    Renderer.enabled = true;
            }

            internal void Refresh(List<Material> current, MaterialPropertyBlock block)
            {
                if (Renderer == null)
                    return;
                EnsureOfficeVisible();
                Renderer.GetSharedMaterials(current);
                for (int i = 0; i < current.Count && i < Assigned.Length; i++)
                {
                    if (SavedBlocks[i] == null)
                        continue;
                    Renderer.GetPropertyBlock(block, i);
                    if (!Owns(current[i], Assigned[i]))
                    {
                        if (block.GetFloat(OverrideId) == 1f)
                            Renderer.SetPropertyBlock(SavedBlocks[i].isEmpty ? null : SavedBlocks[i], i);
                        Original[i] = current[i];
                        Renderer.GetPropertyBlock(SavedBlocks[i], i);
                        current[i] = Assigned[i];
                        Renderer.SetSharedMaterials(current);
                        Renderer.GetPropertyBlock(block, i);
                    }

                    if (block.GetFloat(VisibilityId) == 1f && block.GetFloat(PowerId) == 1f && block.GetVector(SpecValuesId) == ScreenEmissionValues && block.GetColor(EmissionColorId) == Color.white && block.GetTexture(MainTextureId) == MainTextures[i] && block.GetTexture(EmissionMapId) == EmissionTextures[i])
                        continue;
                    if (block.GetFloat(OverrideId) != 1f)
                        Renderer.GetPropertyBlock(SavedBlocks[i], i);
                    if (block.isEmpty)
                        Renderer.GetPropertyBlock(block);
                    block.SetFloat(OverrideId, 1f);
                    block.SetFloat(VisibilityId, 1f);
                    block.SetFloat(PowerId, 1f);
                    block.SetVector(SpecValuesId, ScreenEmissionValues);
                    block.SetColor(EmissionColorId, Color.white);
                    block.SetTexture(MainTextureId, MainTextures[i]);
                    block.SetTexture(EmissionMapId, EmissionTextures[i]);
                    var textureTransform = TextureTransforms[i];
                    block.SetVector(MainTransformId, textureTransform);
                    block.SetVector(EmissionTransformId, textureTransform);
                    Renderer.SetPropertyBlock(block, i);
                }

                current.Clear();
            }

            private readonly List<Material> _restoreMaterials = new List<Material>(8);
            internal void Restore(MaterialPropertyBlock block)
            {
                if (Renderer == null)
                    return;
                var current = _restoreMaterials;
                Renderer.GetSharedMaterials(current);
                for (int i = 0; i < current.Count && i < Assigned.Length; i++)
                {
                    if (SavedBlocks[i] == null)
                        continue;
                    Renderer.GetPropertyBlock(block, i);
                    if (block.GetFloat(OverrideId) == 1f)
                        Renderer.SetPropertyBlock(SavedBlocks[i].isEmpty ? null : SavedBlocks[i], i);
                    if (!Owns(current[i], Assigned[i]))
                        continue;
                    current[i] = Original[i];
                }

                Renderer.SetSharedMaterials(current);
                current.Clear();
                if (Activated != null)
                {
                    foreach (var obj in Activated)
                        if (obj != null)
                            obj.SetActive(false);
                    if (EnabledRenderer)
                        Renderer.enabled = false;
                }
            }

            private static bool Owns(Material current, Material assigned)
            {
                return current != null && (current == assigned || (current.name.StartsWith(DecontaminationScreenRules.MaterialPrefix, StringComparison.Ordinal) && current.mainTexture == assigned.mainTexture && current.GetTexture(EmissionMapId) == assigned.GetTexture(EmissionMapId)));
            }
        }

        private static readonly int OverrideId = Shader.PropertyToID("_ISBVideoOverride");
        private static readonly int VisibilityId = Shader.PropertyToID("_EmissionVisibility");
        private static readonly int PowerId = Shader.PropertyToID("_EmissionPower");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissiveColor");
        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int MainTransformId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int EmissionTransformId = Shader.PropertyToID("_EmissionMap_ST");
        private static readonly int SpecValuesId = Shader.PropertyToID("_SpecVals");
        private static readonly Vector4 ScreenEmissionValues = new Vector4(2f, 0f, 0f, 0f);
        private readonly List<Surface> _surfaces = new List<Surface>();
        private readonly List<DecontaminationScreenFrame> _frames = new List<DecontaminationScreenFrame>();
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly List<Material> _currentMaterials = new List<Material>(8);
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
        private readonly bool _externalFrame, _hideOfficeOverlay;
        private readonly Func<Renderer, bool> _filter;
        private readonly VideoPlayer _player;
        private readonly GameObject _root;
        private readonly RenderTexture _video;
        private readonly float _prepareStarted;
        private readonly ManualLogSource _log;
        private bool _materialFailureReported;
        private float _nextFrameDraw;
        private bool _seekTimeoutReported;
        private long _lastFrame = -1, _overlayRevision = -1, _powerRevision = -1, _sourceRevision = -1;
        private bool _hasOffice, _hasTerminal;
        private int _drawnBindingCount = -1;
        private float _nextClockCheck;
        private float _nextMaterialCheck;
        private float _seekStarted;
        private bool _seeking;
        private bool _started;
        private bool _finished;
        private bool _disposed;
        private string _failure;
        internal DecontaminationScreens(RenderTexture frame, ManualLogSource log, Func<Renderer, bool> filter = null, bool hideOfficeOverlay = false)
        {
            _externalFrame = true;
            _hideOfficeOverlay = hideOfficeOverlay;
            _filter = filter;
            _video = frame;
            _log = log;
            _root = new GameObject("ISB_FacilityDisplayBindings");
            Camera.onPreCull += PrioritizeScreens;
        }

        internal DecontaminationScreens(string path, ManualLogSource log, Func<Renderer, bool> filter = null)
        {
            _filter = filter;
            _prepareStarted = Time.realtimeSinceStartup;
            _log = log;
            _root = new GameObject("ISB_DecontaminationVideo");
            try
            {
                _video = DecontaminationScreenFrame.CreateTexture(1280, 648, "ISB_DecontaminationVideo");
                _player = _root.AddComponent<VideoPlayer>();
                _player.playOnAwake = false;
                _player.source = VideoSource.Url;
                _player.url = System.IO.Path.GetFullPath(path);
                _player.renderMode = VideoRenderMode.RenderTexture;
                _player.targetTexture = _video;
                _player.audioOutputMode = VideoAudioOutputMode.None;
                _player.waitForFirstFrame = true;
                _player.skipOnDrop = true;
                _player.isLooping = false;
                _player.errorReceived += OnError;
                _player.seekCompleted += OnSeekCompleted;
                _player.loopPointReached += OnFinished;
                _player.Prepare();
                Camera.onPreCull += PrioritizeScreens;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal IEnumerator FindScreens()
        {
            var renderers = new List<Renderer>(1024);
            var materials = new List<Material>(8);
            int processed = 0;
            for (int i = 0; i < SceneManager.sceneCount && !_disposed; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.name.StartsWith("Laboratory_", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (_disposed)
                        yield break;
                    if (root == null || root.name.StartsWith("ISB_", StringComparison.Ordinal))
                        continue;
                    root.GetComponentsInChildren(true, renderers);
                    foreach (var renderer in renderers)
                    {
                        if (_disposed)
                            yield break;
                        if (++processed >= 192)
                        {
                            processed = 0;
                            yield return null;
                        }

                        if (renderer == null)
                            continue;
                        if (_hideOfficeOverlay && OfficeScreenCanvas.IsTerminalOverlay(renderer))
                        {
                            if (!_hiddenOfficeRenderers.ContainsKey(renderer))
                                _hiddenOfficeRenderers.Add(renderer, renderer.forceRenderingOff);
                            renderer.forceRenderingOff = true;
                            continue;
                        }

                        if (_filter != null && !_filter(renderer))
                            continue;
                        renderer.GetSharedMaterials(materials);
                        try
                        {
                            bool office = OfficeScreenCanvas.IsCodeScreen(renderer);
                            bool terminal = OfficeScreenCanvas.IsTerminalScreen(renderer);
                            Renderer target = renderer;
                            if ((OfficeScreenCanvas.IsTerminalScreen(renderer) || !renderer.gameObject.activeInHierarchy) && ((office && renderer.name.Equals("Lab_recreation_LED_LOD0", StringComparison.OrdinalIgnoreCase)) || OfficeScreenCanvas.IsTerminalScreen(renderer)))
                            {
                                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                                if (mesh != null)
                                {
                                    var display = new GameObject(renderer.name);
                                    display.transform.SetParent(_root.transform, false);
                                    display.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                                    display.transform.localScale = renderer.transform.lossyScale;
                                    if (OfficeScreenCanvas.IsTerminalScreen(renderer))
                                    {
                                        foreach (var original in renderer.transform.parent.GetComponentsInChildren<Renderer>(true))
                                            if (original.name.StartsWith("pc_laptop_d_LOD", StringComparison.OrdinalIgnoreCase))
                                            {
                                                if (!_hiddenOfficeRenderers.ContainsKey(original))
                                                    _hiddenOfficeRenderers.Add(original, original.forceRenderingOff);
                                                original.forceRenderingOff = true;
                                            }
                                    }

                                    display.AddComponent<MeshFilter>().sharedMesh = mesh;
                                    target = display.AddComponent<MeshRenderer>();
                                    target.sharedMaterials = materials.ToArray();
                                }
                            }

                            Bind(target, materials, office, terminal);
                        }
                        catch (Exception ex)
                        {
                            if (!_materialFailureReported)
                            {
                                _materialFailureReported = true;
                                _log?.LogError("ISB SOF: A monitor could not display the video: " + ex.GetBaseException().Message);
                            }
                        }

                        materials.Clear();
                    }

                    renderers.Clear();
                }
            }

            if (!_disposed)
            {
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    _log?.LogInfo("ISB SOF video: Screen scan completed. renderers=" + _bindings.Count + ", surfaces=" + _surfaces.Count + ", atlases=" + _frames.Count + ".");
                }
            }

            if (!_disposed && _bindings.Count == 0)
                _failure = "No supported Labs screen materials were found.";
        }

        private void Bind(Renderer renderer, List<Material> materials, bool office, bool terminal)
        {
            Material[] original = null;
            Material[] assigned = null;
            for (int i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null || !DecontaminationScreenRules.Match(renderer.name, material.name, i, out Rect area, out bool atlas))
                    continue;
                float aspect = DecontaminationScreenRules.AspectRatio(renderer.name, material.name, renderer.transform.lossyScale, renderer.localBounds.size);
                Surface surface = null;
                foreach (var candidate in _surfaces)
                    if (candidate.Source == material && candidate.Area == area && candidate.Aspect == aspect && candidate.Office == office && candidate.Terminal == terminal)
                    {
                        surface = candidate;
                        break;
                    }

                if (surface == null)
                {
                    surface = CreateSurface(material, area, atlas, aspect, office, terminal);
                    _surfaces.Add(surface);
                }

                if (assigned == null)
                {
                    original = materials.ToArray();
                    assigned = materials.ToArray();
                }

                assigned[i] = surface.Material;
            }

            if (assigned == null)
                return;
            var binding = new Binding
            {
                Renderer = renderer,
                Owner = renderer.gameObject,
                Original = original,
                Assigned = assigned,
                Frames = new DecontaminationScreenFrame[assigned.Length],
                SavedBlocks = new MaterialPropertyBlock[assigned.Length],
                MainTextures = new Texture[assigned.Length],
                EmissionTextures = new Texture[assigned.Length],
                TextureTransforms = new Vector4[assigned.Length]
            };
            for (int i = 0; i < assigned.Length; i++)
                if (original[i] != assigned[i])
                {
                    binding.SavedBlocks[i] = new MaterialPropertyBlock();
                    binding.MainTextures[i] = assigned[i].mainTexture;
                    binding.EmissionTextures[i] = assigned[i].GetTexture(EmissionMapId);
                    Vector2 scale = assigned[i].GetTextureScale("_MainTex"), offset = assigned[i].GetTextureOffset("_MainTex");
                    binding.TextureTransforms[i] = new Vector4(scale.x, scale.y, offset.x, offset.y);
                    foreach (var surface in _surfaces)
                        if (surface.Material == assigned[i])
                        {
                            binding.Frames[i] = surface.Frame;
                            break;
                        }
                }

            if (OfficeScreenCanvas.IsCodeScreen(renderer) || OfficeScreenCanvas.IsTerminalScreen(renderer))
            {
                binding.Activated = new List<GameObject>();
                binding.EnabledRenderer = !renderer.enabled;
                for (var part = renderer.transform; part != null; part = part.parent)
                {
                    if (!part.gameObject.activeSelf)
                        binding.Activated.Add(part.gameObject);
                    if (part.name == "Lab_Recreation_LED" || part.name == "Office_table_set_05 (2)")
                        break;
                }

                binding.EnsureOfficeVisible();
            }

            _bindings.Add(binding);
            _visibilityFrame = -1;
            renderer.sharedMaterials = assigned;
            binding.Refresh(_currentMaterials, _propertyBlock);
        }

        private Surface CreateSurface(Material original, Rect area, bool atlas, float aspect, bool office, bool terminal)
        {
            var shader = Shader.Find("p0/Reflective/Bumped Emissive Specular SMap");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The native emissive screen shader is unavailable.");
            _hasOffice |= office;
            _hasTerminal |= terminal;
            var surface = new Surface
            {
                Source = original,
                Area = area,
                Aspect = aspect,
                Office = office,
                Terminal = terminal
            };
            try
            {
                Texture background = atlas ? original.mainTexture ?? Texture2D.blackTexture : null;
                Rect frameArea = atlas ? area : new Rect(0f, 0f, 1f, 1f);
                DecontaminationScreenFrame frame = null;
                foreach (var candidate in _frames)
                    if (candidate.Background == background && candidate.Area == frameArea && candidate.Aspect == aspect && candidate.Office == office && candidate.Terminal == terminal)
                    {
                        frame = candidate;
                        break;
                    }

                if (frame == null)
                {
                    frame = new DecontaminationScreenFrame(background, frameArea, aspect, office, terminal);
                    _frames.Add(frame);
                }

                surface.Frame = frame;
                surface.Material = new Material(shader)
                {
                    name = DecontaminationScreenRules.MaterialPrefix + original.name
                };
                surface.Material.SetTexture("_MainTex", atlas ? (Texture)frame.ColorTexture : Texture2D.blackTexture);
                surface.Material.SetTexture("_EmissionMap", frame.EmissionTexture);
                surface.Material.SetFloat("_EmissionPower", 1f);
                surface.Material.SetFloat("_EmissionVisibility", 1f);
                surface.Material.SetColor("_EmissiveColor", Color.white);
                surface.Material.SetVector(SpecValuesId, ScreenEmissionValues);
                Vector2 scale = atlas ? Vector2.one : new Vector2(1f / area.width, 1f / area.height);
                Vector2 offset = atlas ? Vector2.zero : new Vector2(-area.x / area.width, -area.y / area.height);
                surface.Material.SetTextureScale("_MainTex", scale);
                surface.Material.SetTextureOffset("_MainTex", offset);
                surface.Material.SetTextureScale("_EmissionMap", scale);
                surface.Material.SetTextureOffset("_EmissionMap", offset);
                if (!atlas)
                {
                    surface.Material.SetColor("_Color", Color.white);
                    surface.Material.SetColor("_SpecColor", Color.black);
                }

                surface.Material.renderQueue = 2001;
                return surface;
            }
            catch
            {
                Object.Destroy(surface.Material);
                throw;
            }
        }

        private readonly Dictionary<Renderer, bool> _hiddenOfficeRenderers = new Dictionary<Renderer, bool>();
        private int _priorityFrame = -1, _visibilityFrame = -1;
        private readonly List<Binding> _visibleBindings = new List<Binding>();
        private long _drawRevision;
        private bool _hasFrame;
        private void UpdateVisibility()
        {
            if (_visibilityFrame == Time.frameCount)
                return;
            _visibilityFrame = Time.frameCount;
            _visibleBindings.Clear();
            foreach (var frame in _frames)
                frame.Visible = false;
            foreach (var binding in _bindings)
            {
                if (binding.Renderer == null || !binding.Renderer.isVisible || !binding.Renderer.enabled || !binding.Owner.activeInHierarchy)
                    continue;
                _visibleBindings.Add(binding);
                foreach (var frame in binding.Frames)
                    if (frame != null)
                        frame.Visible = true;
            }
        }

        private void DrawVisibleFrames()
        {
            if (!_hasFrame)
                return;
            UpdateVisibility();
            foreach (var frame in _frames)
                frame.DrawVisible(_video, _drawRevision);
        }

        private void PrioritizeScreens(Camera camera)
        {
            if (_disposed || camera == null || camera.cameraType != CameraType.Game || _priorityFrame == Time.frameCount)
                return;
            _priorityFrame = Time.frameCount;
            UpdateVisibility();
            DrawVisibleFrames();
            foreach (var binding in _visibleBindings)
                binding.Refresh(_currentMaterials, _propertyBlock);
        }

        private bool _paused;
        internal void SetPaused(bool paused)
        {
            _paused = paused;
            if (_player == null || !_started)
                return;
            if (paused)
                _player.Pause();
            else if (!_finished && !_player.isPlaying)
                _player.Play();
        }

        internal void Tick(double elapsed, float now)
        {
            if (_disposed)
                return;
            if (_failure != null)
                throw new InvalidOperationException(_failure);
            if (now >= _nextMaterialCheck)
            {
                _nextMaterialCheck = now + 1f;
                foreach (var hidden in _hiddenOfficeRenderers)
                    if (hidden.Key != null)
                        hidden.Key.forceRenderingOff = true;
                foreach (var binding in _bindings)
                    binding.Refresh(_currentMaterials, _propertyBlock);
            }

            long codeRevision = _hasOffice ? OfficeScreenCanvas.FrameRevision(OfficeScreenCanvas.Popup) : 0;
            long powerRevision = OfficeScreenCanvas.FrameRevision(OfficeScreenCanvas.PowerPopup);
            bool overlaysChanged = _overlayRevision != codeRevision || _powerRevision != powerRevision;
            if (_externalFrame)
            {
                long sourceRevision = OfficeScreenCanvas.FrameRevision(_video);
                if (_sourceRevision == sourceRevision && !overlaysChanged && _drawnBindingCount == _bindings.Count)
                {
                    DrawVisibleFrames();
                    return;
                }

                _sourceRevision = sourceRevision;
                _overlayRevision = codeRevision;
                _powerRevision = powerRevision;
                _drawnBindingCount = _bindings.Count;
                _hasFrame = true;
                _drawRevision++;
                DrawVisibleFrames();
                return;
            }

            if (!_player.isPrepared)
            {
                if (now - _prepareStarted > 60f)
                    throw new TimeoutException("Video preparation timed out.");
                return;
            }

            if (elapsed < 0d)
                return;
            double target = Math.Min(elapsed, Math.Max(0d, _player.length - .08d));
            if (!_started)
            {
                _started = true;
                Seek(target, now);
                if (!_paused)
                    _player.Play();
            }

            if (_seeking && now - _seekStarted > 8f)
            {
                if (!_seekTimeoutReported)
                {
                    _seekTimeoutReported = true;
                    _log?.LogWarning("ISB SOF video: Seek callback timed out; resuming playback and deferring clock correction.");
                }

                _seeking = false;
                _nextClockCheck = now + 5f;
                if (!_paused && !_player.isPlaying && !_finished)
                    _player.Play();
            }

            if (now >= _nextClockCheck && !_seeking && !_finished)
            {
                _nextClockCheck = now + 2f;
                if (Math.Abs(_player.time - target) > 2d)
                    Seek(target, now);
            }

            long frame = _player.frame;
            if (frame < 0 || (frame == _lastFrame && !overlaysChanged && _drawnBindingCount == _bindings.Count))
                return;
            if (now < _nextFrameDraw)
                return;
            _nextFrameDraw = now + 1f / 24f;
            _overlayRevision = codeRevision;
            _powerRevision = powerRevision;
            _drawnBindingCount = _bindings.Count;
            _lastFrame = frame;
            _hasFrame = true;
            _drawRevision++;
            DrawVisibleFrames();
        }

        private void Seek(double target, float now)
        {
            if (target < .05d || !_player.canSetTime)
                return;
            _seeking = true;
            _seekStarted = now;
            _player.time = target;
        }

        private void OnError(VideoPlayer source, string message) => _failure = message;
        private void OnSeekCompleted(VideoPlayer source) => _seeking = false;
        private void OnFinished(VideoPlayer source)
        {
            _finished = true;
            _seeking = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Camera.onPreCull -= PrioritizeScreens;
            if (_player != null)
            {
                _player.errorReceived -= OnError;
                _player.seekCompleted -= OnSeekCompleted;
                _player.loopPointReached -= OnFinished;
                _player.Stop();
                _player.targetTexture = null;
            }

            foreach (var binding in _bindings)
                binding.Restore(_propertyBlock);
            _bindings.Clear();
            _visibleBindings.Clear();
            foreach (var surface in _surfaces)
                Object.Destroy(surface.Material);
            foreach (var frame in _frames)
                frame.Dispose();
            _frames.Clear();
            _surfaces.Clear();
            _currentMaterials.Clear();
            foreach (var hidden in _hiddenOfficeRenderers)
                if (hidden.Key != null)
                    hidden.Key.forceRenderingOff = hidden.Value;
            _hiddenOfficeRenderers.Clear();
            if (!_externalFrame)
                DecontaminationScreenFrame.Release(_video);
            Object.Destroy(_root);
        }
    }
}
