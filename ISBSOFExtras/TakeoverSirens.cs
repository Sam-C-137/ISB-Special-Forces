using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverSirenPoint
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotationX { get; set; }
        public float rotationY { get; set; } = 180f;
        public float rotationZ { get; set; } = 180f;
        internal bool IsValid => Finite(x) && Finite(y) && Finite(z) && Finite(rotationX) && Finite(rotationY) && Finite(rotationZ);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class TakeoverSirensDefinition
    {
        public bool enabled { get; set; }
        public float rotationSpeed { get; set; } = 300.0f;
        public float spotIntensity { get; set; } = 4.0f;
        public float spotRange { get; set; } = 15.0f;
        public float pointIntensity { get; set; } = 1.25f;
        public float pointRange { get; set; } = 8.0f;
        public float lightActivationDistance { get; set; } = 60.0f;
        public int maxLitSirens { get; set; } = 4;
        public TakeoverSirenPoint[] sirens { get; set; } = Array.Empty<TakeoverSirenPoint>();

        internal void Normalize()
        {
            rotationSpeed = Clamp(rotationSpeed, -360f, 360f, 300.0f);
            spotIntensity = Clamp(spotIntensity, 0f, 8f, 4.0f);
            pointIntensity = Clamp(pointIntensity, 0f, 4f, 1.25f);
            spotRange = Clamp(spotRange, 1f, 25f, 15.0f);
            pointRange = Clamp(pointRange, 1f, 10f, 8.0f);
            lightActivationDistance = Clamp(lightActivationDistance, 10f, 120f, 60.0f);
            maxLitSirens = Math.Max(0, maxLitSirens);
            sirens = sirens ?? Array.Empty<TakeoverSirenPoint>();
        }

        private static float Clamp(float value, float min, float max, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
    }

    internal sealed class TakeoverSirens : IDisposable
    {
        internal const string RootPrefix = "ISB_ContainmentBreachSiren_";
        private sealed class SirenInstance
        {
            internal GameObject Root;
            internal Transform Rotor;
            internal Quaternion RestRotation;
            internal Light[] Lights;
            internal float Distance;
            internal bool Selected;
            internal bool Lit;
        }

        private readonly List<SirenInstance> _instances = new List<SirenInstance>();
        private readonly Dictionary<Material, Material> _materials = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Dictionary<string, Color>> _originalTints = new Dictionary<Material, Dictionary<string, Color>>();
        private static readonly Color Red = new Color(1f, .015f, .005f, 1f);
        private static readonly Color WarningGreen = new Color(.69f, 1f, .162f, 1f);
        private bool _warning;
        private TakeoverSirensDefinition _definition;
        private ManualLogSource _log;
        private bool _prepared;
        private bool _active;
        private float _angle;
        private float _nextLightSelection;
        internal static bool ShouldActivate(bool raid, bool labs, bool local, bool takeover, bool lightsStarted, bool lightsDisabled) => raid && labs && local && takeover && lightsStarted && !lightsDisabled;
        internal void Load(string path, ManualLogSource log)
        {
            Dispose();
            _log = log;
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    _definition = JsonConvert.DeserializeObject<TakeoverSirensDefinition>(File.ReadAllText(path));
                _definition?.Normalize();
            }
            catch (Exception ex)
            {
                _definition = null;
                _log?.LogError("[ISB Sirens] Invalid configuration: " + ex.Message);
            }
        }

        internal void SetPresentation(int lights, float distance, float rotation, float spotIntensity, float spotRange, float pointIntensity, float pointRange)
        {
            if (_definition == null)
                return;
            _definition.maxLitSirens = lights;
            _definition.lightActivationDistance = distance;
            _definition.rotationSpeed = rotation;
            _definition.spotIntensity = spotIntensity;
            _definition.spotRange = spotRange;
            _definition.pointIntensity = pointIntensity;
            _definition.pointRange = pointRange;
            _definition.Normalize();
            foreach (var instance in _instances)
                foreach (var light in instance.Lights)
                {
                    if (light == null)
                        continue;
                    light.intensity = light.type == LightType.Spot ? _definition.spotIntensity : _definition.pointIntensity;
                    light.range = light.type == LightType.Spot ? _definition.spotRange : _definition.pointRange;
                }

            _nextLightSelection = 0f;
        }

        internal void Prepare(bool localPresentation)
        {
            if (_prepared || !localPresentation || _definition?.enabled != true || _definition.sirens.Length == 0)
                return;
            var scene = SceneManager.GetSceneByName("Laboratory_Collector_Exit");
            if (!scene.isLoaded)
                return;
            _prepared = true;
            Transform donor = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Hangar_Gate")
                {
                    donor = root.transform.Find("Siren");
                    break;
                }

            if (donor == null || donor.Find("Siren_lamp") == null)
            {
                _log?.LogError("[ISB Sirens] The Labs gate siren model could not be found.");
                return;
            }

            for (int i = 0; i < _definition.sirens.Length; i++)
            {
                var point = _definition.sirens[i];
                if (point == null || !point.IsValid)
                    continue;
                GameObject copy = null;
                try
                {
                    copy = new GameObject(RootPrefix + i);
                    copy.SetActive(false);
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    copy.transform.position = new Vector3(point.x, point.y, point.z);
                    copy.transform.rotation = Quaternion.Euler(point.rotationX, point.rotationY, point.rotationZ);
                    copy.transform.localScale = donor.lossyScale;
                    CopyChildren(donor, copy.transform);
                    var rotor = copy.transform.Find("Siren_lamp");
                    var lights = copy.GetComponentsInChildren<Light>(true);
                    if (rotor == null || lights.Length == 0)
                        throw new InvalidOperationException("The Labs siren has no rotor or lights.");
                    _instances.Add(new SirenInstance { Root = copy, Rotor = rotor, RestRotation = rotor.localRotation, Lights = lights });
                    copy.SetActive(true);
                }
                catch (Exception ex)
                {
                    if (copy != null)
                    {
                        copy.SetActive(false);
                        Object.Destroy(copy);
                    }

                    _log?.LogError("[ISB Sirens] Could not create siren " + i + ": " + ex.Message);
                }
            }
        }

        private void CopyChildren(Transform source, Transform destination)
        {
            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                if (child.name.IndexOf("BALLISTIC", StringComparison.OrdinalIgnoreCase) >= 0 || child.name.EndsWith("_LOD1", StringComparison.Ordinal))
                    continue;
                var copy = new GameObject(child.name);
                copy.layer = child.gameObject.layer;
                copy.transform.SetParent(destination, false);
                copy.transform.localPosition = child.localPosition;
                copy.transform.localRotation = child.localRotation;
                copy.transform.localScale = child.localScale;
                var filter = child.GetComponent<MeshFilter>();
                var renderer = child.GetComponent<MeshRenderer>();
                if (filter != null && filter.sharedMesh != null && renderer != null)
                {
                    copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var rendered = copy.AddComponent<MeshRenderer>();
                    var originals = renderer.sharedMaterials;
                    for (int m = 0; m < originals.Length; m++)
                        originals[m] = CopyMaterial(originals[m]);
                    rendered.sharedMaterials = originals;
                    rendered.shadowCastingMode = ShadowCastingMode.Off;
                    rendered.receiveShadows = false;
                }

                var sourceLight = child.GetComponent<Light>();
                if (sourceLight != null && (sourceLight.type == LightType.Spot || sourceLight.type == LightType.Point))
                {
                    var light = copy.AddComponent<Light>();
                    light.enabled = false;
                    light.type = sourceLight.type;
                    light.color = _warning ? WarningGreen : Red;
                    light.intensity = light.type == LightType.Spot ? _definition.spotIntensity : _definition.pointIntensity;
                    light.range = light.type == LightType.Spot ? _definition.spotRange : _definition.pointRange;
                    light.spotAngle = sourceLight.spotAngle;
                    light.cookie = sourceLight.cookie;
                    light.cullingMask = sourceLight.cullingMask;
                    light.shadows = LightShadows.None;
                }

                CopyChildren(child, copy.transform);
            }
        }

        private Material CopyMaterial(Material source)
        {
            if (source == null)
                return null;
            if (_materials.TryGetValue(source, out var material))
                return material;
            material = new Material(source)
            {
                name = "ISB_Siren_" + source.name
            };
            if (source.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 && material.HasProperty("_Color"))
            {
                var tint = material.GetColor("_Color");
                material.SetColor("_Color", new Color(1f, 0.015f, 0.005f, tint.a));
            }

            var colors = new Dictionary<string, Color>();
            if (source.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 || source.name.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) >= 0)
                foreach (var property in new[] { "_Color", "_BaseColor", "_EmissionColor", "_EmissiveColor" })
                    if (material.HasProperty(property)) colors[property] = material.GetColor(property);
            _originalTints.Add(material, colors);
            if (_warning) ApplyTint(material, colors, true);
            _materials.Add(source, material);
            return material;
        }

        private static void ApplyTint(Material material, Dictionary<string, Color> colors, bool warning)
        {
            foreach (var entry in colors)
            {
                var color = entry.Value;
                if (warning)
                {
                    float strength = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                    color = WarningGreen * strength;
                    color.a = entry.Value.a;
                }
                material.SetColor(entry.Key, color);
            }
        }

        internal void SetWarningColor(bool warning)
        {
            if (_warning == warning) return;
            _warning = warning;
            foreach (var entry in _originalTints)
                if (entry.Key != null) ApplyTint(entry.Key, entry.Value, warning);
            foreach (var instance in _instances)
                foreach (var light in instance.Lights)
                    if (light != null) light.color = warning ? WarningGreen : Red;
        }

        internal void SetActive(bool active)
        {
            if (_active == active)
                return;
            _active = active;
            _nextLightSelection = 0f;
            if (!active)
                foreach (var instance in _instances)
                    SetLit(instance, false);
        }

        internal void Tick(float deltaTime, float now)
        {
            if (!_active || _instances.Count == 0)
                return;
            if (now >= _nextLightSelection)
            {
                _nextLightSelection = now + 0.5f;
                SelectLights();
            }

            _angle = Mathf.Repeat(_angle + _definition.rotationSpeed * deltaTime, 360f);
            var rotation = Quaternion.Euler(0f, 0f, _angle);
            foreach (var instance in _instances)
                if (instance.Rotor != null && instance.Distance < 14400f)
                    instance.Rotor.localRotation = instance.RestRotation * rotation;
        }

        private void SelectLights()
        {
            var camera = Camera.main;
            float limit = _definition.lightActivationDistance * _definition.lightActivationDistance;
            foreach (var instance in _instances)
            {
                instance.Selected = false;
                instance.Distance = camera != null && instance.Root != null ? (camera.transform.position - instance.Root.transform.position).sqrMagnitude : float.PositiveInfinity;
            }

            for (int i = 0; i < _definition.maxLitSirens; i++)
            {
                SirenInstance nearest = null;
                float distance = limit;
                foreach (var instance in _instances)
                    if (!instance.Selected && instance.Distance < distance)
                    {
                        nearest = instance;
                        distance = instance.Distance;
                    }

                if (nearest == null)
                    break;
                nearest.Selected = true;
            }

            foreach (var instance in _instances)
                SetLit(instance, instance.Selected);
        }

        private static void SetLit(SirenInstance instance, bool lit)
        {
            if (instance.Lit == lit)
                return;
            instance.Lit = lit;
            foreach (var light in instance.Lights)
                if (light != null)
                    light.enabled = lit;
        }

        public void Dispose()
        {
            SetActive(false);
            foreach (var instance in _instances)
                if (instance.Root != null)
                {
                    instance.Root.SetActive(false);
                    Object.Destroy(instance.Root);
                }

            _instances.Clear();
            foreach (var material in _materials.Values)
                if (material != null)
                    Object.Destroy(material);
            _materials.Clear();
            _originalTints.Clear();
            _warning = false;
            _definition = null;
            _prepared = false;
            _angle = 0f;
        }
    }
}
