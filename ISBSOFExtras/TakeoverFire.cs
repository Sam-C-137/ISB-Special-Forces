using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverFireDefinition
    {
        public int maxVisibleFires { get; set; } = TakeoverFire.MaxFirePoints;
        public float visualDistance { get; set; } = 200f;
        public int maxFireLights { get; set; } = TakeoverFire.DefaultLitFires;
        public float lightDistance { get; set; } = 60f;
        public float particleDensity { get; set; } = 1f;
        public bool showSmoke { get; set; } = true;
        public bool enabled { get; set; }
        public TakeoverFirePoint[] fires { get; set; } = Array.Empty<TakeoverFirePoint>();
    }

    internal sealed class TakeoverFirePoint
    {
        public string source { get; set; }
        public string scene { get; set; }
        public string sourcePath { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotationX { get; set; }
        public float rotationY { get; set; }
        public float rotationZ { get; set; }
        public float scale { get; set; } = 1f;
        public bool damage { get; set; }
        public float damageWidth { get; set; } = 1.92f;
        public float damageHeight { get; set; } = 1.4f;
        public float damageDepth { get; set; } = 1.92f;
        public bool smoke { get; set; }
        public float smokeRate { get; set; } = 3f;
        public bool light { get; set; }
        public float lightIntensity { get; set; } = 1.5f;
        public float lightRange { get; set; } = 5f;
        public bool destroyedCar { get; set; }
        public float carRotationY { get; set; }
        public bool armoredVehicle { get; set; }
        public float armoredRotationY { get; set; }
        public float? vehicleX { get; set; }
        public float? vehicleY { get; set; }
        public float? vehicleZ { get; set; }
    }

    internal sealed class TakeoverFire
    {
        internal const int MaxFirePoints = 64;
        private sealed class FireInstance
        {
            internal GameObject Root;
            internal GameObject Effects;
            internal ParticleSystem[] Particles;
            internal TakeoverFlameTrigger[] Triggers;
            internal Light[] Lights;
            internal float[] EmissionRates;
            internal float[] DistanceEmissionRates;
            internal float[] LightIntensities;
            internal float[] LightRanges;
            internal float Distance;
            internal bool Selected;
            internal bool VisualSelected;
            internal float NearDistanceSquared;
            internal bool[] SmokeParticles;
            internal bool[] ParticleVisible;
            internal bool Lit;
        }

        private readonly List<FireInstance> _instances = new List<FireInstance>();
        private bool _prepared;
        private bool _disableFire;
        private bool _disableDestroyedVehicles;
        private TakeoverFireDefinition _definition;
        private ManualLogSource _log;
        private bool _active;
        private float _nextLightSelection;
        private bool _hasLights;
        private bool _hasParticles;
        private int _visibleFires = MaxFirePoints;
        private int _lightLimit = DefaultLitFires;
        private float _visualDistance = 200f;
        private float _lightDistance = 60f;
        private float _particleDensity = 1f;
        private bool _showSmoke = true;
        private double _extinctionStartedAt = -1d;
        private float _nextExtinctionUpdate;
        private float _strength = 1f;
        internal const float ExtinctionSeconds = 20f;
        internal const int DefaultLitFires = 8;
        private readonly TakeoverProps _props = new TakeoverProps();
        private readonly ShorelineFireAssets _shoreline = new ShorelineFireAssets();
        private readonly TakeoverCarAssets _car = new TakeoverCarAssets();
        private readonly TakeoverCarAssets _armored = new TakeoverCarAssets(true);
        private string _folder;
        internal static int GetPointCount(int requested) => Math.Max(0, Math.Min(MaxFirePoints, requested));
        internal void Load(string path, ManualLogSource log, bool prepareLocalAssets, bool disableFire = false, bool disableDestroyedVehicles = false)
        {
            Dispose();
            _log = log;
            _disableFire = disableFire;
            _disableDestroyedVehicles = disableDestroyedVehicles;
            _folder = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(Path.GetFullPath(path));
            _definition = null;
            _props.Load(_folder, log);
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    _definition = JsonConvert.DeserializeObject<TakeoverFireDefinition>(File.ReadAllText(path));
                if (!_disableFire && prepareLocalAssets && _definition != null && _definition.enabled && _definition.fires != null)
                    for (int i = 0; i < GetPointCount(_definition.fires.Length); i++)
                        if (_definition.fires[i]?.source == ShorelineFireAssets.SourceName)
                        {
                            _shoreline.Prepare(_folder);
                            break;
                        }

                if (prepareLocalAssets && _definition?.enabled == true && _definition.fires != null)
                    for (int i = 0; i < GetPointCount(_definition.fires.Length); i++)
                        if (!_disableDestroyedVehicles && _definition.fires[i]?.destroyedCar == true)
                        {
                            _car.Prepare(_folder, true);
                            break;
                        }

                if (prepareLocalAssets && _definition?.enabled == true && _definition.fires != null)
                    for (int i = 0; i < GetPointCount(_definition.fires.Length); i++)
                        if (!_disableDestroyedVehicles && _definition.fires[i]?.armoredVehicle == true)
                        {
                            _armored.Prepare(_folder, true);
                            break;
                        }
            }
            catch (Exception)
            {
            }
        }

        internal static bool ShouldShow(bool raidActive, bool labs, bool localPresentation, bool takeover)
        {
            return localPresentation && ShouldActivate(raidActive, labs, takeover);
        }

        internal static bool ShouldActivate(bool raidActive, bool labs, bool takeover)
        {
            return raidActive && labs && takeover;
        }

        internal void SetActive(bool active, bool localPresentation)
        {
            if (!active)
            {
                Clear();
                return;
            }

            if (_active)
                return;
            Prepare(localPresentation);
            _active = true;
            if (_strength <= 0f)
                return;
            foreach (var instance in _instances)
                if (instance.Effects != null)
                    instance.Effects.SetActive(true);
            ApplyStrength(_strength);
            Physics.SyncTransforms();
            _nextLightSelection = 0f;
            foreach (var instance in _instances)
            {
                if (instance.Effects == null)
                    continue;
                try
                {
                    foreach (var trigger in instance.Triggers)
                        if (trigger != null)
                            trigger.PrepareForActivation();
                    for (int p = 0; p < instance.Particles.Length; p++)
                    {
                        var particle = instance.Particles[p];
                        if (particle == null || !particle.gameObject.activeInHierarchy)
                            continue;
                        particle.Play(false);
                        instance.ParticleVisible[p] = true;
                    }
                }
                catch (Exception ex)
                {
                    instance.Effects.SetActive(false);
                    _log?.LogError("[ISB Fire] Activation failed: " + ex.Message);
                }
            }

            Tick(0f);
        }

        internal void Prepare(bool localPresentation)
        {
            if (_prepared)
                return;
            _prepared = true;
            _props.Prepare(localPresentation, _disableDestroyedVehicles);
            if (_definition == null || !_definition.enabled || _definition.fires == null)
                return;
            int count = GetPointCount(_definition.fires.Length);
            for (int i = 0; i < count; i++)
            {
                GameObject root = null;
                try
                {
                    var point = _definition.fires[i];
                    ValidatePoint(point);
                    bool createFire = !_disableFire && (localPresentation || point.damage);
                    bool createCar = !_disableDestroyedVehicles && point.destroyedCar;
                    bool createArmored = !_disableDestroyedVehicles && point.armoredVehicle;
                    if (!createFire && !createCar && !createArmored)
                        continue;
                    root = new GameObject("ISB_ContainmentBreachFire_" + i);
                    root.SetActive(false);
                    root.transform.position = new Vector3(point.x, point.y, point.z);
                    root.transform.rotation = Quaternion.Euler(point.rotationX, point.rotationY, point.rotationZ);
                    root.transform.localScale = Vector3.one * point.scale;
                    GameObject effects = null;
                    if (createFire)
                    {
                        effects = new GameObject("ISB_FireEffects");
                        effects.SetActive(false);
                        effects.transform.SetParent(root.transform, false);
                    }

                    if (createFire && localPresentation)
                    {
                        var donor = point.source == ShorelineFireAssets.SourceName ? _shoreline.Prepare(_folder) : FindUniqueDonor(point);
                        ValidateDonor(donor);
                        var copy = Object.Instantiate(donor, effects.transform, false);
                        copy.hideFlags = HideFlags.None;
                        copy.transform.localPosition = Vector3.zero;
                        copy.transform.localRotation = Quaternion.identity;
                        copy.SetActive(true);
                        foreach (var particle in copy.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            var main = particle.main;
                            main.maxParticles = Math.Min(main.maxParticles, 256);
                            main.cullingMode = ParticleSystemCullingMode.Pause;
                            main.stopAction = ParticleSystemStopAction.None;
                            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                            if (main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                            var collision = particle.collision;
                            collision.enabled = false;
                            var trigger = particle.trigger;
                            trigger.enabled = false;
                        }

                        foreach (var light in copy.GetComponentsInChildren<Light>(true))
                        {
                            light.shadows = LightShadows.None;
                            light.enabled = false;
                        }

                        if (point.smoke)
                        {
                            try
                            {
                                _shoreline.AddSmoke(effects.transform, _folder, point.smokeRate);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        if (point.light)
                        {
                            var lamp = new GameObject("ISB_FireLight");
                            lamp.transform.SetParent(effects.transform, false);
                            lamp.transform.localPosition = Vector3.up;
                            var component = lamp.AddComponent<Light>();
                            component.type = LightType.Point;
                            component.color = new Color(1f, .43f, .12f);
                            component.intensity = point.lightIntensity;
                            component.range = point.lightRange * point.scale;
                            component.shadows = LightShadows.None;
                            component.enabled = false;
                        }
                    }

                    if (createCar)
                    {
                        try
                        {
                            _car.AddTo(root.transform, _folder, point.scale, point.carRotationY, localPresentation, GetVehiclePosition(point));
                        }
                        catch (Exception)
                        {
                        }
                    }

                    if (createArmored)
                    {
                        try
                        {
                            _armored.AddTo(root.transform, _folder, point.scale, point.armoredRotationY, localPresentation, GetVehiclePosition(point));
                        }
                        catch (Exception)
                        {
                        }
                    }

                    if (createFire && point.damage)
                    {
                        var hazard = new GameObject("ISB_FlameDamage");
                        hazard.transform.SetParent(effects.transform, false);
                        var box = hazard.AddComponent<BoxCollider>();
                        box.isTrigger = true;
                        box.size = new Vector3(point.damageWidth, point.damageHeight, point.damageDepth);
                        box.center = new Vector3(0f, point.damageHeight * .5f, 0f);
                        hazard.AddComponent<TakeoverFlameTrigger>();
                    }

                    var instance = new FireInstance
                    {
                        Root = root,
                        Effects = effects,
                        VisualSelected = true,
                        NearDistanceSquared = (float)Math.Pow(Math.Max(12f, point.scale * Math.Max(point.damageWidth, Math.Max(point.damageHeight, point.damageDepth)) * 1.75f + 4f), 2d),
                        Particles = effects != null ? effects.GetComponentsInChildren<ParticleSystem>(true) : Array.Empty<ParticleSystem>(),
                        Lights = effects != null ? effects.GetComponentsInChildren<Light>(true) : Array.Empty<Light>(),
                        Triggers = effects != null ? effects.GetComponentsInChildren<TakeoverFlameTrigger>(true) : Array.Empty<TakeoverFlameTrigger>()
                    };
                    instance.SmokeParticles = new bool[instance.Particles.Length];
                    instance.ParticleVisible = new bool[instance.Particles.Length];
                    instance.EmissionRates = new float[instance.Particles.Length];
                    instance.DistanceEmissionRates = new float[instance.Particles.Length];
                    for (int p = 0; p < instance.Particles.Length; p++)
                    {
                        instance.SmokeParticles[p] = instance.Particles[p].gameObject.name == "ISB_LightSmoke";
                        var emission = instance.Particles[p].emission;
                        instance.EmissionRates[p] = emission.rateOverTimeMultiplier;
                        instance.DistanceEmissionRates[p] = emission.rateOverDistanceMultiplier;
                    }

                    instance.LightIntensities = new float[instance.Lights.Length];
                    instance.LightRanges = new float[instance.Lights.Length];
                    for (int l = 0; l < instance.Lights.Length; l++)
                    {
                        instance.LightIntensities[l] = instance.Lights[l].intensity;
                        instance.LightRanges[l] = instance.Lights[l].range;
                    }

                    root.SetActive(true);
                    _instances.Add(instance);
                    _hasLights |= instance.Lights.Length > 0;
                    _hasParticles |= instance.Particles.Length > 0;
                }
                catch (Exception ex)
                {
                    if (root != null)
                    {
                        root.SetActive(false);
                        Object.Destroy(root);
                    }

                    _log?.LogError("[ISB Fire] Point " + i + " skipped: " + ex.Message);
                }
            }
        }

        internal void SetPresentation(int visibleFires, float visualDistance, int lights, float lightDistance, float particleDensity, bool smoke)
        {
            _visibleFires = Math.Max(1, Math.Min(MaxFirePoints, visibleFires));
            _visualDistance = GasRules.Clamp(visualDistance, 15f, 300f, 200f);
            _lightLimit = Math.Max(0, lights);
            _lightDistance = GasRules.Clamp(lightDistance, 5f, 120f, 60f);
            _particleDensity = GasRules.Clamp(particleDensity, 0.25f, 1f, 1f);
            _showSmoke = smoke;
            _nextLightSelection = 0f;
            if (_instances.Count > 0)
                ApplyStrength(_strength);
        }

        internal void BeginExtinguishing(double startedAt)
        {
            if (_extinctionStartedAt >= 0d || double.IsNaN(startedAt) || double.IsInfinity(startedAt) || startedAt < 0d)
                return;
            _extinctionStartedAt = startedAt;
            _nextExtinctionUpdate = 0f;
        }

        internal void Tick(float now, double raidTime = -1d)
        {
            if (!_active || _strength <= 0f)
                return;
            if (_extinctionStartedAt >= 0d && raidTime >= _extinctionStartedAt && !double.IsInfinity(raidTime) && now >= _nextExtinctionUpdate)
            {
                _nextExtinctionUpdate = now + 0.1f;
                float progress = (float)Math.Min(1d, (raidTime - _extinctionStartedAt) / ExtinctionSeconds);
                float strength = 1f - progress * progress * (3f - 2f * progress);
                if (strength < _strength)
                    ApplyStrength(strength);
            }

            if (_strength <= 0f || (!_hasLights && !_hasParticles) || now < _nextLightSelection)
                return;
            _nextLightSelection = now + 0.5f;
            var camera = Camera.main;
            if (camera == null)
                return;
            var position = camera.transform.position;
            int nearby = 0;
            foreach (var instance in _instances)
            {
                instance.Selected = false;
                instance.Distance = instance.Root != null ? (position - instance.Root.transform.position).sqrMagnitude : float.PositiveInfinity;
                instance.VisualSelected = instance.Particles.Length > 0 && instance.Distance <= instance.NearDistanceSquared;
                if (instance.VisualSelected)
                    nearby++;
            }

            for (int i = nearby; i < _visibleFires; i++)
            {
                FireInstance nearest = null;
                float distance = _visualDistance * _visualDistance;
                foreach (var instance in _instances)
                    if (!instance.VisualSelected && instance.Particles.Length > 0 && instance.Distance < distance)
                    {
                        nearest = instance;
                        distance = instance.Distance;
                    }

                if (nearest == null)
                    break;
                nearest.VisualSelected = true;
            }

            foreach (var instance in _instances)
                ApplyParticleVisibility(instance);
            for (int i = 0; i < _lightLimit; i++)
            {
                FireInstance nearest = null;
                float distance = _lightDistance * _lightDistance;
                foreach (var instance in _instances)
                    if (!instance.Selected && instance.VisualSelected && instance.Lights.Length > 0 && instance.Distance < distance)
                    {
                        nearest = instance;
                        distance = instance.Distance;
                    }

                if (nearest == null)
                    break;
                nearest.Selected = true;
            }

            foreach (var instance in _instances)
            {
                if (instance.Selected == instance.Lit)
                    continue;
                instance.Lit = instance.Selected;
                foreach (var light in instance.Lights)
                    if (light != null)
                        light.enabled = instance.Lit;
            }
        }

        private void ApplyParticleVisibility(FireInstance instance)
        {
            for (int p = 0; p < instance.Particles.Length; p++)
            {
                var particle = instance.Particles[p];
                if (particle == null)
                    continue;
                bool visible = _active && _strength > 0f && instance.Effects != null && instance.Effects.activeInHierarchy && instance.VisualSelected && (_showSmoke || !instance.SmokeParticles[p]);
                if (visible == instance.ParticleVisible[p])
                    continue;
                instance.ParticleVisible[p] = visible;
                if (visible)
                    particle.Play(false);
                else
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ApplyStrength(float strength)
        {
            _strength = strength;
            foreach (var instance in _instances)
            {
                if (instance.Effects == null || !instance.Effects.activeSelf)
                    continue;
                if (strength <= 0f)
                {
                    foreach (var trigger in instance.Triggers)
                        if (trigger != null)
                            trigger.enabled = false;
                    foreach (var particle in instance.Particles)
                        if (particle != null)
                            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    foreach (var light in instance.Lights)
                        if (light != null)
                            light.enabled = false;
                    instance.Lit = false;
                    instance.Effects.SetActive(false);
                    continue;
                }

                instance.Effects.transform.localScale = Vector3.one * strength;
                for (int p = 0; p < instance.Particles.Length; p++)
                {
                    if (instance.Particles[p] == null)
                        continue;
                    var emission = instance.Particles[p].emission;
                    emission.rateOverTimeMultiplier = instance.EmissionRates[p] * strength * _particleDensity;
                    emission.rateOverDistanceMultiplier = instance.DistanceEmissionRates[p] * strength * _particleDensity;
                }

                for (int l = 0; l < instance.Lights.Length; l++)
                {
                    var light = instance.Lights[l];
                    if (light == null)
                        continue;
                    light.intensity = instance.LightIntensities[l] * strength;
                    light.range = instance.LightRanges[l] * strength;
                }
            }
        }

        internal void Clear()
        {
            if (!_active)
                return;
            foreach (var instance in _instances)
            {
                if (instance.Effects != null)
                    instance.Effects.SetActive(false);
                foreach (var particle in instance.Particles)
                    if (particle != null)
                        particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _active = false;
        }

        internal void Dispose()
        {
            Clear();
            foreach (var instance in _instances)
                if (instance.Root != null)
                {
                    instance.Root.SetActive(false);
                    Object.Destroy(instance.Root);
                }

            _instances.Clear();
            _hasLights = false;
            _hasParticles = false;
            _prepared = false;
            _extinctionStartedAt = -1d;
            _nextExtinctionUpdate = 0f;
            _strength = 1f;
            _props.Dispose();
            _shoreline.Dispose();
            _car.Dispose();
            _armored.Dispose();
        }

        internal static Vector3? GetVehiclePosition(TakeoverFirePoint point)
        {
            if (!point.vehicleX.HasValue && !point.vehicleY.HasValue && !point.vehicleZ.HasValue)
                return null;
            if (!point.vehicleX.HasValue || !point.vehicleY.HasValue || !point.vehicleZ.HasValue)
                throw new InvalidOperationException("Vehicle position requires vehicleX, vehicleY and vehicleZ.");
            foreach (float value in new[]
            {
                point.vehicleX.Value,
                point.vehicleY.Value,
                point.vehicleZ.Value
            }

            )
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidOperationException("Invalid vehicle position.");
            return new Vector3(point.vehicleX.Value, point.vehicleY.Value, point.vehicleZ.Value);
        }

        private static void ValidatePoint(TakeoverFirePoint point)
        {
            if (point == null)
                throw new InvalidOperationException("Fire point is null.");
            GetVehiclePosition(point);
            if (point.destroyedCar && point.armoredVehicle)
                throw new InvalidOperationException("Select either destroyedCar or armoredVehicle for a fire point.");
            if (!string.IsNullOrEmpty(point.source) && point.source != ShorelineFireAssets.SourceName)
                throw new InvalidOperationException("Unknown fire source: " + point.source);
            if (string.IsNullOrEmpty(point.source) && (string.IsNullOrWhiteSpace(point.scene) || string.IsNullOrWhiteSpace(point.sourcePath)))
                throw new InvalidOperationException("scene and full sourcePath are required.");
            foreach (float value in new[]
            {
                point.x,
                point.y,
                point.z,
                point.rotationX,
                point.rotationY,
                point.rotationZ,
                point.scale,
                point.damageWidth,
                point.damageHeight,
                point.damageDepth,
                point.smokeRate,
                point.lightIntensity,
                point.lightRange,
                point.carRotationY,
                point.armoredRotationY
            }

            )
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidOperationException("Coordinates must be finite.");
            if (point.scale <= 0 || point.scale > 3)
                throw new InvalidOperationException("scale must be greater than 0 and at most 3.");
            if (point.damage && (point.damageWidth <= 0 || point.damageWidth > 8 || point.damageHeight <= 0 || point.damageHeight > 4 || point.damageDepth <= 0 || point.damageDepth > 8))
                throw new InvalidOperationException("damage dimensions must be positive: width/depth at most 8 and height at most 4.");
            if (point.smoke && (point.smokeRate < 0 || point.smokeRate > 8))
                throw new InvalidOperationException("smokeRate must be 0..8 particles per second.");
            if (point.light && (point.lightIntensity < 0 || point.lightIntensity > 4 || point.lightRange <= 0 || point.lightRange > 12))
                throw new InvalidOperationException("lightIntensity must be 0..4; lightRange must be greater than zero and at most 12.");
        }

        private static GameObject FindUniqueDonor(TakeoverFirePoint point)
        {
            var segments = point.sourcePath.Trim('/').Split('/');
            GameObject match = null;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded || !string.Equals(scene.name, point.scene, StringComparison.Ordinal))
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != segments[0])
                        continue;
                    Transform current = root.transform;
                    for (int p = 1; p < segments.Length && current != null; p++)
                    {
                        Transform next = null;
                        for (int c = 0; c < current.childCount; c++)
                        {
                            var child = current.GetChild(c);
                            if (child.name != segments[p])
                                continue;
                            if (next != null)
                                throw new InvalidOperationException("Ambiguous sibling name in sourcePath.");
                            next = child;
                        }

                        current = next;
                    }

                    if (current == null)
                        continue;
                    if (match != null)
                        throw new InvalidOperationException("More than one object matches scene/sourcePath.");
                    match = current.gameObject;
                }
            }

            if (match == null)
                throw new InvalidOperationException("Source not loaded in this raid: " + point.scene + "/" + point.sourcePath);
            return match;
        }

        private static void ValidateDonor(GameObject donor)
        {
            var components = donor.GetComponentsInChildren<Component>(true);
            if (components.Length > 128)
                throw new InvalidOperationException("Select a small effect subtree (maximum 128 components).");
            int emitters = 0;
            foreach (var component in components)
            {
                if (component is ParticleSystem particle)
                {
                    emitters++;
                    var subEmitters = particle.subEmitters;
                    for (int i = 0; i < subEmitters.subEmittersCount; i++)
                    {
                        var sub = subEmitters.GetSubEmitterSystem(i);
                        if (sub != null && !sub.transform.IsChildOf(donor.transform))
                            throw new InvalidOperationException("Sub-emitter outside source subtree; select its common visual parent.");
                    }

                    var lights = particle.lights;
                    if (lights.enabled && lights.light != null && !lights.light.transform.IsChildOf(donor.transform))
                        throw new InvalidOperationException("Particle light outside source subtree; select its common visual parent.");
                    continue;
                }

                if (component is Transform || component is ParticleSystemRenderer || component is MeshFilter || component is MeshRenderer || component is Light)
                    continue;
                throw new InvalidOperationException("Select the visual effect child; unsupported component: " + (component == null ? "missing script" : component.GetType().FullName));
            }

            if (emitters < 1 || emitters > 16)
                throw new InvalidOperationException("Source must contain 1..16 ParticleSystems.");
        }
    }
}
