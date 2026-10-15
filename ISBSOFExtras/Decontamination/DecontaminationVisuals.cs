using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class DecontaminationVisuals : IDisposable
    {
        private sealed class Vent
        {
            internal ParticleSystem Particles;
            internal bool Selected;
            internal float Distance;
            internal Vector3 Position;
        }

        private GameObject _root;
        private Material _material;
        private Texture2D _texture;
        private ParticleSystem _ambient;
        private Vent[] _vents;
        private DecontaminationConfig _config;
        internal void Prepare(string folder, DecontaminationConfig config)
        {
            Dispose();
            _config = config;
            try
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Gas particle shader unavailable.");
                byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "shoreline-smoke.png"));
                if (bytes.Length > 1024 * 1024)
                    throw new InvalidDataException("Gas texture exceeds the size limit.");
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
                if (!ImageConversion.LoadImage(_texture, bytes, true) || _texture.width != 256 || _texture.height != 256)
                    throw new InvalidDataException("Gas smoke atlas is invalid.");
                _texture.name = "ISB_Decontamination_Texture";
                _texture.wrapMode = TextureWrapMode.Clamp;
                _material = new Material(shader)
                {
                    name = "ISB_Decontamination_Material",
                    mainTexture = _texture
                };
                _material.SetColor("_TintColor", new Color(.64f, .69f, .43f, .42f));
                if (_material.HasProperty("_InvFade"))
                    _material.SetFloat("_InvFade", 2f);
                _root = new GameObject("ISB_Decontamination");
                _vents = new Vent[config.emitters.Length];
                for (int i = 0; i < _vents.Length; i++)
                {
                    var point = config.emitters[i];
                    _vents[i] = new Vent
                    {
                        Position = new Vector3(point.x, point.y, point.z)
                    };
                }

                if (config.ambientParticles > 0)
                    _ambient = Create("ISB_Decontamination_LocalHaze", config.ambientParticles, true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private ParticleSystem Create(string name, int maximum, bool ambient)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.transform.SetParent(_root.transform, false);
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = maximum;
            main.startLifetime = ambient ? 7f : 6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(ambient ? .025f : .55f, ambient ? .09f : .95f);
            main.startSize = new ParticleSystem.MinMaxCurve(ambient ? 1.5f : .55f, ambient ? 2.7f : 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            var shape = particles.shape;
            shape.shapeType = ambient ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
            shape.radius = ambient ? 4.5f : .22f;
            shape.angle = 23f;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(.84f, .89f, .70f), 0f), new GradientColorKey(Color.white, .35f), new GradientColorKey(new Color(.63f, .68f, .48f), 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(ambient ? .42f : .78f, .18f), new GradientAlphaKey(ambient ? .22f : .36f, .6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .32f, 1f, 1.15f));
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-.12f, .12f);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = ambient ? .12f : .2f;
            noise.frequency = .24f;
            noise.scrollSpeed = .15f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.octaveCount = 1;
            var sheet = particles.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.frameOverTime = 0f;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 4f);
            var collision = particles.collision;
            collision.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return particles;
        }

        internal void Tick(Vector3 position, bool exposedArea, float density)
        {
            if (_root == null)
                return;
            float limit = _config.visualDistance * _config.visualDistance;
            foreach (var vent in _vents)
            {
                vent.Selected = false;
                Vector3 delta = vent.Position - position;
                vent.Distance = exposedArea && Math.Abs(delta.y) <= 8f ? delta.sqrMagnitude : float.MaxValue;
            }

            for (int i = 0; i < _config.maxActiveEmitters; i++)
            {
                Vent nearest = null;
                float distance = limit;
                foreach (var vent in _vents)
                    if (!vent.Selected && vent.Distance < distance)
                    {
                        distance = vent.Distance;
                        nearest = vent;
                    }

                if (nearest == null)
                    break;
                nearest.Selected = true;
            }

            foreach (var vent in _vents)
            {
                if (vent.Selected && vent.Particles == null)
                {
                    vent.Particles = Create("ISB_Decontamination_Vent", _config.particlesPerEmitter, false);
                    vent.Particles.transform.position = vent.Position;
                    vent.Particles.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                }

                SetEmission(vent.Particles, vent.Selected, density * _config.particlesPerEmitter / 6f);
            }

            if (_ambient != null)
            {
                _ambient.transform.position = position + Vector3.up * .55f;
                SetEmission(_ambient, exposedArea, density * _config.ambientParticles / 7f);
            }
        }

        internal void Suspend()
        {
            if (_vents != null)
                foreach (var vent in _vents)
                    SetEmission(vent.Particles, false, 0f);
            SetEmission(_ambient, false, 0f);
        }

        private static void SetEmission(ParticleSystem particles, bool enabled, float rate)
        {
            if (particles == null)
                return;
            if (enabled)
            {
                var emission = particles.emission;
                emission.rateOverTime = rate;
                if (!particles.gameObject.activeSelf)
                {
                    particles.gameObject.SetActive(true);
                    particles.Play(false);
                }
            }
            else if (particles.gameObject.activeSelf)
            {
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.gameObject.SetActive(false);
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                _root.SetActive(false);
                Object.Destroy(_root);
            }

            if (_material != null)
                Object.Destroy(_material);
            if (_texture != null)
                Object.Destroy(_texture);
            _root = null;
            _material = null;
            _texture = null;
            _ambient = null;
            _vents = null;
        }
    }
}
