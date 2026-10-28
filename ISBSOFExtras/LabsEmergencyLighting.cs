using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal sealed class LabsEmergencyLighting : IDisposable
    {
        private const float RefreshSeconds = 1f / 25f;
        private const int MaxPointLights = 24;
        private readonly Plane[] _planes = new Plane[6];
        private readonly int[] _selected = new int[MaxPointLights];
        private readonly int[] _previousSelected = new int[MaxPointLights];
        private int _selectedCount;
        private readonly float[] _scores = new float[MaxPointLights];
        private Camera _camera;
        private float _nextSelection;
        private const float PeakEmission = .85f;
        private const float PanelEmission = .12f;
        private const float PanelIntensity = .035f;
        private const float PeakIntensity = .85f;
        private static readonly Color Red = new Color(1f, 0.025f, 0.012f);
        private readonly List<Surface> _surfaces = new List<Surface>();
        private readonly List<Fixture> _fixtures = new List<Fixture>();
        private readonly Dictionary<Material, Material> _materials = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _panelMaterials = new Dictionary<Material, Material>();
        private readonly List<Texture2D> _masks = new List<Texture2D>();
        private readonly Dictionary<GameObject, bool> _activation = new Dictionary<GameObject, bool>();
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
        private static readonly int EmissionPower = Shader.PropertyToID("_EmissionPower");
        private static readonly int EmissionVisibility = Shader.PropertyToID("_EmissionVisibility");
        private readonly List<Material> _powerMaterials = new List<Material>();
        private readonly List<Material> _colorMaterials = new List<Material>();
        private readonly List<BlockTarget> _blocks = new List<BlockTarget>();
        private sealed class BlockTarget
        {
            internal Renderer Renderer;
            internal MaterialPropertyBlock Block;
            internal int Slot;
            internal Material Expected;
            internal bool Panel;
            internal float LastPulse = -1f;
        }

        private Color _warningColor = Red;
        private Light[] _lights;
        private EmergencyLightCulling[] _culling;
        private GameObject _root;
        private Material _tubeTemplate;
        private bool _prepared;
        private bool _preparing;
        private bool _active;
        private bool _screensSuppressed;
        private readonly List<Material> _currentMaterials = new List<Material>();
        private int _generation;
        private float _nextRefresh;
        private float _lastPulse = -1f;
        private sealed class Surface
        {
            internal Renderer Renderer;
            internal bool Panel;
            internal bool[] Screens;
            internal bool[] Applied;
            internal Material[] Original;
            internal Material[] Emergency;
            internal Material[] Blackout;
            internal MaterialPropertyBlock[] BlackoutBlocks;
        }

        private sealed class Fixture
        {
            internal bool Panel;
            internal Transform Transform;
            internal Vector3 Position;
        }

        internal IEnumerator Prepare()
        {
            if (_prepared)
                yield break;
            int generation = _generation;
            if (_preparing)
            {
                while (_preparing && generation == _generation)
                    yield return null;
                yield break;
            }

            _preparing = true;
            try
            {
                foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                    if (material.name.StartsWith("shopping_mall_lamp_lum_ceiling_emissive", StringComparison.OrdinalIgnoreCase) && IsEmissive(material))
                    {
                        _tubeTemplate = material;
                        break;
                    }

                var groups = new HashSet<Transform>();
                int visited = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || !scene.name.StartsWith("Laboratory", StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (generation != _generation)
                                yield break;
                            var group = FindFixture(renderer.transform);
                            bool panel = group != null && IsPanel(group.name);
                            var originals = renderer.sharedMaterials;
                            Material[] emergency = null;
                            var screens = new bool[originals.Length];
                            for (int slot = 0; slot < originals.Length; slot++)
                            {
                                var material = originals[slot];
                                screens[slot] = material != null && DecontaminationScreenRules.Match(renderer.name, material.name, slot, out _, out _);
                                bool integrated = material != null && material.name.StartsWith("lab_generic_lamp", StringComparison.OrdinalIgnoreCase) && scene.name == "Laboratory_ControlServerRoom_floor_2";
                                if (material == null || (group == null && !integrated))
                                    continue;
                                bool server = group != null && group.name.StartsWith("Lab_Server_Light", StringComparison.OrdinalIgnoreCase);
                                if (!server && !IsEmissive(material) && !(IsUnlitTube(material) && _tubeTemplate != null))
                                    continue;
                                var replacement = GetEmergencyMaterial(material, server, panel);
                                if (replacement == null)
                                    continue;
                                if (emergency == null)
                                    emergency = (Material[])originals.Clone();
                                emergency[slot] = replacement;
                                var key = group ?? renderer.transform;
                                if (groups.Add(key))
                                {
                                    AddFixture(key, renderer, slot, panel);
                                    CacheActivation(key);
                                }
                            }

                            if (emergency != null)
                                _surfaces.Add(new Surface { Renderer = renderer, Panel = panel, Original = originals, Emergency = emergency, Screens = screens, Applied = new bool[originals.Length], Blackout = new Material[originals.Length], BlackoutBlocks = new MaterialPropertyBlock[originals.Length] });
                            if (++visited % 128 == 0)
                                yield return null;
                        }
                }

                if (generation != _generation)
                    yield break;
                _root = new GameObject("ISB_Prop_EmergencyIllumination");
                _root.SetActive(false);
                _lights = new Light[_fixtures.Count];
                _culling = new EmergencyLightCulling[_fixtures.Count];
                for (int i = 0; i < _lights.Length; i++)
                {
                    if (generation != _generation)
                        yield break;
                    var child = new GameObject("EmergencyLight_" + i);
                    child.transform.SetParent(_root.transform, false);
                    child.transform.position = _fixtures[i].Position;
                    var light = child.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = _warningColor;
                    light.range = _fixtures[i].Panel ? 1.75f : 6f;
                    light.intensity = 0f;
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.ForcePixel;
                    _lights[i] = light;
                    light.enabled = false;
                    _culling[i] = child.AddComponent<EmergencyLightCulling>();
                    _culling[i].Configure(light);
                    if (i % 32 == 31)
                        yield return null;
                }

                if (generation == _generation)
                    _prepared = true;
            }
            finally
            {
                if (generation == _generation)
                    _preparing = false;
            }
        }

        private static Transform FindFixture(Transform transform)
        {
            if (FacilityLightingInterop.IsManaged(transform)) return null;
            for (var node = transform; node != null; node = node.parent)
            {
                string name = node.name;
                if (name.StartsWith("ISB_", StringComparison.OrdinalIgnoreCase))
                    return null;
                string family = Family(name);
                if (family != null)
                {
                    var parent = node.parent;
                    while (parent != null && parent.name.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                    {
                        node = parent;
                        parent = parent.parent;
                    }

                    return node;
                }
            }

            return null;
        }

        private static string Family(string name)
        {
            if (name.StartsWith("Lab_Cylinder_Logo", StringComparison.OrdinalIgnoreCase))
                return "Lab_Cylinder_Logo";
            if (name.StartsWith("lab_poster_glass_stand", StringComparison.OrdinalIgnoreCase))
                return "lab_poster_glass_stand";
            if (name.StartsWith("Lab_Recreation_LED", StringComparison.OrdinalIgnoreCase))
                return "Lab_Recreation_LED";
            if (name.StartsWith("terragroup_logo2", StringComparison.OrdinalIgnoreCase))
                return "terragroup_logo2";
            if (name.StartsWith("Searchlight_01", StringComparison.OrdinalIgnoreCase))
                return "Searchlight_01";
            if (name.StartsWith("Fluorescent_lamp_03", StringComparison.OrdinalIgnoreCase))
                return "Fluorescent_lamp_03";
            if (name.Equals("lab_reception", StringComparison.OrdinalIgnoreCase) || name.StartsWith("lab_reception (", StringComparison.OrdinalIgnoreCase))
                return "lab_reception";
            if (name.StartsWith("Lab_Blue_Light", StringComparison.OrdinalIgnoreCase))
                return "Lab_Blue_Light";
            if (name.StartsWith("Lab_vent_module_1", StringComparison.OrdinalIgnoreCase))
                return "Lab_vent_module_1";
            if (name.StartsWith("Lab_vent_module_3", StringComparison.OrdinalIgnoreCase))
                return "Lab_vent_module_3";
            if (name.StartsWith("lab_lamp_lam_ceiling", StringComparison.OrdinalIgnoreCase))
                return "lab_lamp_lam_ceiling";
            if (name.StartsWith("shopping_mall_lamp_lum_ceiling", StringComparison.OrdinalIgnoreCase))
                return "shopping_mall_lamp_lum_ceiling";
            if (name.StartsWith("SDO_angele", StringComparison.OrdinalIgnoreCase))
                return "SDO_angele";
            if (name.StartsWith("armstrong_light", StringComparison.OrdinalIgnoreCase))
                return "armstrong_light";
            if (name.StartsWith("Lab_Server_Light", StringComparison.OrdinalIgnoreCase))
                return "Lab_Server_Light";
            if (name.StartsWith("ULTRA_lamp", StringComparison.OrdinalIgnoreCase))
                return "ULTRA_lamp";
            if (name.StartsWith("lab_office_lighting", StringComparison.OrdinalIgnoreCase))
                return "lab_office_lighting";
            if (name.StartsWith("terragroup_logo3", StringComparison.OrdinalIgnoreCase))
                return "terragroup_logo3";
            if (name.StartsWith("lab_logo", StringComparison.OrdinalIgnoreCase))
                return "lab_logo";
            if (name.StartsWith("Lab_recreation_flowerbed_09_lamp", StringComparison.OrdinalIgnoreCase))
                return "Lab_recreation_flowerbed_09_lamp";
            if (name.StartsWith("Lab_recreation_flowerbed_06_lamp", StringComparison.OrdinalIgnoreCase))
                return "Lab_recreation_flowerbed_06_lamp";
            if (name.StartsWith("cafe_lab_light", StringComparison.OrdinalIgnoreCase))
                return "cafe_lab_light";
            if (name.StartsWith("hex_light", StringComparison.OrdinalIgnoreCase))
                return "hex_light";
            if (name.StartsWith("lab_reception_table", StringComparison.OrdinalIgnoreCase))
                return "lab_reception_table";
            if (name.StartsWith("Lab_Recreation_offices_interior", StringComparison.OrdinalIgnoreCase))
                return "Lab_Recreation_offices_interior";
            if (name.StartsWith("Lab_recreation_celling", StringComparison.OrdinalIgnoreCase))
                return "Lab_recreation_celling";
            return null;
        }

        private static bool IsPanel(string name)
        {
            return name.StartsWith("Lab_Recreation_LED", StringComparison.OrdinalIgnoreCase) || name.Equals("lab_reception", StringComparison.OrdinalIgnoreCase) || name.StartsWith("lab_reception (", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnlitTube(Material material)
        {
            return material.name.StartsWith("shopping_mall_lamp_lum_ceiling_noemissive", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmissive(Material material)
        {
            string shader = material.shader == null ? "" : material.shader.name;
            return shader.IndexOf("Emissive", StringComparison.OrdinalIgnoreCase) >= 0 || (material.IsKeywordEnabled("_EMISSION") && material.HasProperty("_EmissionColor")) || (material.name.StartsWith("Lab_lights_terragroup_logo", StringComparison.OrdinalIgnoreCase) && material.HasProperty("_EmissionColor"));
        }

        private Material GetEmergencyMaterial(Material original, bool server, bool panel)
        {
            var cache = panel ? _panelMaterials : _materials;
            if (cache.TryGetValue(original, out var cached))
                return cached;
            if (server && !original.HasProperty("_MainTex"))
                return null;
            var material = new Material(original)
            {
                name = "ISB_Prop_Emergency_" + original.name
            };
            if (IsUnlitTube(original) && _tubeTemplate != null)
            {
                material.shader = _tubeTemplate.shader;
                if (_tubeTemplate.HasProperty("_EmissionMap"))
                    material.SetTexture("_EmissionMap", _tubeTemplate.GetTexture("_EmissionMap"));
            }

            if (panel)
            {
                var shader = Shader.Find("Standard");
                if (shader == null)
                {
                    UnityEngine.Object.Destroy(material);
                    return null;
                }

                var texture = original.HasProperty("_MainTex") ? original.GetTexture("_MainTex") : null;
                material.shader = shader;
                material.shaderKeywords = new string[0];
                material.SetTexture("_MainTex", Texture2D.blackTexture);
                material.SetColor("_Color", Color.black);
                material.SetTexture("_EmissionMap", texture ?? Texture2D.whiteTexture);
                material.SetColor("_EmissionColor", Color.black);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Glossiness", 0f);
                material.SetFloat("_SpecularHighlights", 0f);
                material.SetFloat("_GlossyReflections", 0f);
                material.EnableKeyword("_EMISSION");
            }
            else if (server)
            {
                var shader = Shader.Find("Standard");
                var texture = original.mainTexture;
                if (shader == null || texture == null)
                {
                    UnityEngine.Object.Destroy(material);
                    return null;
                }

                material.shader = shader;
                var mask = MakeLedMask(texture);
                material.SetTexture("_EmissionMap", mask);
                _masks.Add(mask);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.SetFloat("_Glossiness", 0.25f);
            }
            else if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", _warningColor);
                if (material.HasProperty("_EmissionPower"))
                    material.SetFloat("_EmissionPower", 0f);
                if (material.HasProperty("_EmissionVisibility"))
                    material.SetFloat("_EmissionVisibility", 1f);
            }
            else
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }

            if (!panel)
            {
                if (material.HasProperty(EmissionPower))
                    _powerMaterials.Add(material);
                else if (material.HasProperty(EmissionColor))
                    _colorMaterials.Add(material);
            }

            cache.Add(original, material);
            return material;
        }

        private static Texture2D MakeLedMask(Texture source)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var mask = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                mask.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var pixels = mask.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    bool led = i / source.width < source.height * 0.34f && pixel.r > 90 && pixel.g > 90 && pixel.b < pixel.g * 0.8f && Math.Abs(pixel.r - pixel.g) < 70;
                    pixels[i] = led ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                }

                mask.SetPixels32(pixels);
                mask.Apply(false, true);
                return mask;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void AddFixture(Transform key, MeshRenderer renderer, int slot, bool panel)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter == null ? null : filter.sharedMesh;
            var bounds = renderer.bounds;
            if (mesh != null && slot < mesh.subMeshCount)
            {
                var local = mesh.GetSubMesh(slot).bounds;
                var size = local.size;
                var transform = renderer.transform;
                bounds = new Bounds(transform.TransformPoint(local.center), new Vector3(transform.TransformVector(Vector3.right * size.x).magnitude, transform.TransformVector(Vector3.up * size.y).magnitude, transform.TransformVector(Vector3.forward * size.z).magnitude));
                var axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
                float length = transform.TransformVector(axis * Mathf.Max(size.x, size.y, size.z)).magnitude;
                int count = panel ? 1 : Math.Max(1, Mathf.CeilToInt(length / 5f));
                float localLength = Mathf.Max(size.x, size.y, size.z);
                for (int i = 0; i < count; i++)
                {
                    float offset = ((i + .5f) / count - .5f) * localLength;
                    _fixtures.Add(new Fixture { Panel = panel, Transform = key, Position = transform.TransformPoint(local.center + axis * offset) + Vector3.down * .2f });
                }

                return;
            }

            _fixtures.Add(new Fixture { Panel = panel, Transform = key, Position = bounds.center + Vector3.down * .2f });
        }

        private void CacheActivation(Transform fixture)
        {
            for (var node = fixture; node != null; node = node.parent)
                if (!node.gameObject.activeSelf && !_activation.ContainsKey(node.gameObject))
                    _activation.Add(node.gameObject, false);
        }

        private void SetSlot(Surface surface, int slot, bool enabled)
        {
            if (surface.Original[slot] == surface.Emergency[slot] || surface.Applied[slot] == enabled)
                return;
            var current = surface.Renderer.sharedMaterials;
            if (slot >= current.Length)
                return;
            if (enabled)
            {
                if (current[slot] != null && current[slot].name.StartsWith(DecontaminationScreenRules.MaterialPrefix, StringComparison.Ordinal))
                    return;
                surface.Blackout[slot] = current[slot];
                var saved = new MaterialPropertyBlock();
                var block = new MaterialPropertyBlock();
                surface.Renderer.GetPropertyBlock(saved, slot);
                surface.Renderer.GetPropertyBlock(block, slot);
                surface.BlackoutBlocks[slot] = saved;
                if (block.isEmpty)
                    surface.Renderer.GetPropertyBlock(block);
                if (surface.Panel)
                {
                    block.SetColor("_Color", Color.black);
                    block.SetTexture("_MainTex", Texture2D.blackTexture);
                    block.SetTexture("_EmissionMap", surface.Emergency[slot].GetTexture("_EmissionMap"));
                    block.SetFloat("_Metallic", 0f);
                    block.SetFloat("_Glossiness", 0f);
                }

                block.SetColor(EmissiveColor, _warningColor);
                block.SetFloat(EmissionVisibility, 1f);
                _blocks.Add(new BlockTarget { Renderer = surface.Renderer, Block = block, Slot = slot, Expected = surface.Emergency[slot], Panel = surface.Panel });
                current[slot] = surface.Emergency[slot];
            }
            else
            {
                _blocks.RemoveAll(target => target.Renderer == surface.Renderer && target.Slot == slot);
                if (current[slot] == surface.Emergency[slot])
                {
                    current[slot] = surface.Blackout[slot];
                    var block = surface.BlackoutBlocks[slot];
                    surface.Renderer.SetPropertyBlock(block == null || block.isEmpty ? null : block, slot);
                }
            }

            surface.Applied[slot] = enabled;
            surface.Renderer.sharedMaterials = current;
        }

        internal void SetScreensSuppressed(bool suppressed)
        {
            if (_screensSuppressed == suppressed)
                return;
            _screensSuppressed = suppressed;
            foreach (var surface in _surfaces)
            {
                if (surface.Renderer == null)
                    continue;
                for (int slot = 0; slot < surface.Screens.Length; slot++)
                    if (surface.Screens[slot])
                        SetSlot(surface, slot, _active && !suppressed);
            }

            ApplyActivation();
            _nextRefresh = 0f;
        }

        private void ApplyActivation()
        {
            foreach (var pair in _activation)
            {
                if (pair.Key == null)
                    continue;
                bool video = false;
                if (_screensSuppressed && !_active)
                    foreach (var surface in _surfaces)
                        if (surface.Renderer != null && Array.IndexOf(surface.Screens, true) >= 0 && surface.Renderer.transform.IsChildOf(pair.Key.transform))
                        {
                            video = true;
                            break;
                        }

                pair.Key.SetActive(_active || video || pair.Value);
            }
        }

        internal void SetActive(bool active)
        {
            active &= _prepared;
            if (active == _active)
                return;
            _active = active;
            FacilityLightingInterop.SetEmergency(active, _warningColor, 0f);
            foreach (var surface in _surfaces)
            {
                if (surface.Renderer == null)
                    continue;
                for (int slot = 0; slot < surface.Original.Length; slot++)
                    SetSlot(surface, slot, active && !(_screensSuppressed && surface.Screens[slot]));
            }

            ApplyActivation();
            if (active)
            {
                _root.SetActive(true);
                _nextRefresh = 0f;
                _lastPulse = -1f;
                Tick(0f);
                _nextRefresh = 0f;
                _nextSelection = 0f;
            }
            else if (_root != null)
                _root.SetActive(false);
        }

        private void SelectLights(float now)
        {
            if (now < _nextSelection || _culling == null)
                return;
            _nextSelection = now + .25f;
            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;
            if (_camera == null)
            {
                foreach (var light in _culling)
                    if (light != null)
                        light.SetBudget(false);
                _selectedCount = 0;
                return;
            }

            int previousCount = _selectedCount;
            Array.Copy(_selected, _previousSelected, previousCount);
            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            Vector3 origin = _camera.transform.position;
            int count = 0;
            for (int i = 0; i < _culling.Length; i++)
            {
                if ((_fixtures[i].Position - origin).sqrMagnitude >= 4900f)
                    continue;
                var light = _culling[i];
                if (light == null || !light.Maintain(origin, _planes))
                    continue;
                float score = light.Distance * (light.BudgetAllowed ? .85f : 1f);
                int at = count;
                while (at > 0 && _scores[at - 1] > score)
                    at--;
                if (at >= MaxPointLights)
                    continue;
                for (int j = Mathf.Min(count, MaxPointLights - 1); j > at; j--)
                {
                    _selected[j] = _selected[j - 1];
                    _scores[j] = _scores[j - 1];
                }

                _selected[at] = i;
                _scores[at] = score;
                count = Mathf.Min(count + 1, MaxPointLights);
            }

            for (int i = 0; i < previousCount; i++)
            {
                int index = _previousSelected[i];
                bool selected = false;
                for (int j = 0; j < count; j++)
                    if (_selected[j] == index)
                    {
                        selected = true;
                        break;
                    }

                if (!selected && _culling[index] != null)
                    _culling[index].SetBudget(false);
            }

            _selectedCount = count;
            for (int j = 0; j < count; j++)
                _culling[_selected[j]].SetBudget(true);
        }

        internal void SetWarningColor(bool warning)
        {
            Color next = warning ? new Color(.69f, 1f, .162f) : Red;
            if (_warningColor == next) return;
            float pulse = Mathf.Clamp01(_lastPulse);
            float power = PeakEmission * pulse;
            _warningColor = next;
            FacilityLightingInterop.SetEmergency(_active, next, pulse);
            _lastPulse = -1f;
            _nextRefresh = 0f;
            foreach (var target in _blocks)
            {
                target.LastPulse = -1f;
                target.Block.SetColor(EmissiveColor, next);
                target.Block.SetColor(EmissionColor, next * (target.Panel ? PanelEmission * pulse : power));
                target.Block.SetFloat(EmissionPower, power);
                if (target.Renderer == null) continue;
                target.Renderer.GetSharedMaterials(_currentMaterials);
                if (target.Slot < _currentMaterials.Count && _currentMaterials[target.Slot] == target.Expected)
                    target.Renderer.SetPropertyBlock(target.Block, target.Slot);
            }
            if (_lights != null) foreach (var light in _lights) if (light != null) light.color = next;
            foreach (var material in _powerMaterials) if (material != null && material.HasProperty(EmissiveColor)) material.SetColor(EmissiveColor, next);
        }

        internal void Tick(float pulse)
        {
            if (!_active || Time.realtimeSinceStartup < _nextRefresh)
                return;
            _nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;
            SelectLights(Time.realtimeSinceStartup);
            pulse = Mathf.Clamp01(pulse);
            FacilityLightingInterop.SetEmergency(true, _warningColor, pulse);
            bool changed = pulse != _lastPulse;
            _lastPulse = pulse;
            float power = PeakEmission * pulse;
            Color emission = _warningColor * power;
            for (int i = 0; changed && i < _powerMaterials.Count; i++)
                if (_powerMaterials[i] != null)
                    _powerMaterials[i].SetFloat(EmissionPower, power);
            for (int i = 0; changed && i < _colorMaterials.Count; i++)
                if (_colorMaterials[i] != null)
                    _colorMaterials[i].SetColor(EmissionColor, emission);
            if (changed)
                foreach (var material in _panelMaterials.Values)
                    if (material != null)
                        material.SetColor(EmissionColor, _warningColor * (PanelEmission * pulse));
            for (int i = 0; i < _blocks.Count; i++)
            {
                var target = _blocks[i];
                if (target.LastPulse == pulse || target.Renderer == null || !target.Renderer.isVisible)
                    continue;
                target.Renderer.GetSharedMaterials(_currentMaterials);
                if (target.Slot >= _currentMaterials.Count || _currentMaterials[target.Slot] != target.Expected)
                    continue;
                target.LastPulse = pulse;
                target.Block.SetColor(EmissiveColor, _warningColor);
                target.Block.SetColor(EmissionColor, target.Panel ? _warningColor * (PanelEmission * pulse) : emission);
                target.Block.SetFloat(EmissionPower, power);
                target.Renderer.SetPropertyBlock(target.Block, target.Slot);
            }

            float intensity = PeakIntensity * pulse;
            for (int i = 0; i < _selectedCount; i++)
            {
                int index = _selected[i];
                if (_culling[index] != null)
                    _culling[index].SetPulse(_fixtures[index].Panel ? PanelIntensity * pulse : intensity);
            }
        }

        public void Dispose()
        {
            _generation++;
            _screensSuppressed = false;
            SetActive(false);
            ApplyActivation();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            foreach (var material in _materials.Values)
                UnityEngine.Object.Destroy(material);
            foreach (var material in _panelMaterials.Values)
                UnityEngine.Object.Destroy(material);
            _panelMaterials.Clear();
            foreach (var mask in _masks)
                UnityEngine.Object.Destroy(mask);
            _materials.Clear();
            _masks.Clear();
            _surfaces.Clear();
            _fixtures.Clear();
            _activation.Clear();
            _powerMaterials.Clear();
            _colorMaterials.Clear();
            _blocks.Clear();
            _root = null;
            _lights = null;
            _culling = null;
            _prepared = false;
            _preparing = false;
            _tubeTemplate = null;
            _currentMaterials.Clear();
            _camera = null;
            _nextSelection = 0f;
            _selectedCount = 0;
        }
    }
}
