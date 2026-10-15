using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class ShorelineFireAssets : IDisposable
    {
        internal const string SourceName = "shoreline-gas";
        internal const string TextureFile = "shoreline-fire.png";
        private GameObject _prototype;
        private Material _material;
        private Texture2D _texture;
        private Material _smokeMaterial;
        private Texture2D _smokeTexture;
        internal GameObject Prepare(string folder)
        {
            if (_prototype != null)
                return _prototype;
            try
            {
                var path = Path.Combine(folder, TextureFile);
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing fire atlas: " + path);
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 4 * 1024 * 1024)
                    throw new InvalidDataException("Fire atlas exceeds 4 MB.");
                var shader = Shader.Find("Legacy Shaders/Particles/Additive");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The original fire shader is not available: Legacy Shaders/Particles/Additive.");
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
                _texture.name = "ISB_ShorelineFireAtlas";
                _texture.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(_texture, bytes, true))
                    throw new InvalidDataException("Invalid fire atlas PNG.");
                if (_texture.width != 2048 || _texture.height != 2048)
                    throw new InvalidDataException("Fire atlas must be the original 2048x2048 texture.");
                _texture.wrapMode = TextureWrapMode.Clamp;
                _texture.filterMode = FilterMode.Bilinear;
                _material = new Material(shader)
                {
                    name = "ISB_ShorelineFireMaterial",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _material.mainTexture = _texture;
                _material.SetColor("_TintColor", new Color(1f, .55192703f, .30882353f, .501960814f));
                if (_material.HasProperty("_InvFade"))
                    _material.SetFloat("_InvFade", 1.35f);
                _prototype = new GameObject("ISB_ShorelineGasFire_Source");
                _prototype.SetActive(false);
                _prototype.hideFlags = HideFlags.HideAndDontSave;
                var emitter = new GameObject("BigFire");
                emitter.transform.SetParent(_prototype.transform, false);
                emitter.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var ps = emitter.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                main.prewarm = true;
                main.playOnAwake = false;
                main.maxParticles = 100;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 2f);
                main.startSize = new ParticleSystem.MinMaxCurve(1f, 3.83f);
                main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .798782945f, .544117689f, .547f), new Color(1f, .522058845f, .522058845f, .497f));
                main.gravityModifier = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.cullingMode = ParticleSystemCullingMode.Automatic;
                main.stopAction = ParticleSystemStopAction.None;
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 30f;
                emission.rateOverDistance = 0f;
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 0f;
                shape.radius = 1.92656219f;
                shape.radiusThickness = 1f;
                shape.arc = 360f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .306961983f, 3.96186733f, 3.96186733f), new Keyframe(.179016367f, .75405556f, 1.0556798f, 1.0556798f), new Keyframe(1f, .99683547f, 0f, 0f)));
                var color = ps.colorOverLifetime;
                color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 10987f / 65535f), new GradientAlphaKey(1f, 1f) });
                color.color = gradient;
                var uv = ps.textureSheetAnimation;
                uv.enabled = true;
                uv.mode = ParticleSystemAnimationMode.Grid;
                uv.numTilesX = 8;
                uv.numTilesY = 6;
                uv.animation = ParticleSystemAnimationType.WholeSheet;
                uv.frameOverTime = new ParticleSystem.MinMaxCurve(.999899983f, new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(1f, 1f, 1f, 0f)));
                uv.startFrame = 0f;
                uv.cycleCount = 1;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = _material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.minParticleSize = 0f;
                renderer.maxParticleSize = 5f;
                renderer.sortingFudge = -50f;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return _prototype;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void AddSmoke(Transform parent, string folder, float rate)
        {
            if (_smokeMaterial == null)
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Alpha-blended smoke shader unavailable.");
                Texture2D texture = null;
                Material material = null;
                try
                {
                    var data = File.ReadAllBytes(Path.Combine(folder, "shoreline-smoke.png"));
                    if (data.Length > 1024 * 1024)
                        throw new InvalidDataException("Smoke atlas exceeds 1 MB.");
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false)
                    {
                        name = "ISB_ShorelineSmokeAtlas",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    if (!ImageConversion.LoadImage(texture, data, true) || texture.width != 256 || texture.height != 256)
                        throw new InvalidDataException("Smoke atlas must be the recovered 256x256 texture.");
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    material = new Material(shader)
                    {
                        name = "ISB_ShorelineSmokeMaterial",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    material.mainTexture = texture;
                    material.SetColor("_TintColor", new Color(.6f, .6f, .6f, .5f));
                    if (material.HasProperty("_InvFade"))
                        material.SetFloat("_InvFade", 3f);
                    _smokeTexture = texture;
                    _smokeMaterial = material;
                }
                catch
                {
                    if (texture != null)
                        Object.Destroy(texture);
                    if (material != null)
                        Object.Destroy(material);
                    throw;
                }
            }

            var smoke = new GameObject("ISB_LightSmoke");
            smoke.transform.SetParent(parent, false);
            smoke.transform.localPosition = Vector3.up * .4f;
            smoke.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            try
            {
                var ps = smoke.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 5f;
                main.loop = true;
                main.prewarm = true;
                main.playOnAwake = false;
                main.maxParticles = 24;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
                main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startColor = new Color(.45f, .43f, .4f, .45f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var emission = ps.emission;
                emission.rateOverTime = rate;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 9.08f;
                shape.radius = .79f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .5f, 1f, 1.5f));
                var colors = ps.colorOverLifetime;
                colors.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.6f, .2f), new GradientAlphaKey(.3f, .7f), new GradientAlphaKey(0f, 1f) });
                colors.color = gradient;
                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-.15f, .15f);
                var uv = ps.textureSheetAnimation;
                uv.enabled = true;
                uv.numTilesX = 2;
                uv.numTilesY = 2;
                uv.animation = ParticleSystemAnimationType.WholeSheet;
                uv.frameOverTime = .9999f;
                uv.startFrame = 0f;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = _smokeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            catch
            {
                smoke.SetActive(false);
                Object.Destroy(smoke);
                throw;
            }
        }

        public void Dispose()
        {
            if (_prototype != null)
            {
                _prototype.SetActive(false);
                Object.Destroy(_prototype);
            }

            if (_material != null)
                Object.Destroy(_material);
            if (_texture != null)
                Object.Destroy(_texture);
            if (_smokeMaterial != null)
                Object.Destroy(_smokeMaterial);
            if (_smokeTexture != null)
                Object.Destroy(_smokeTexture);
            _prototype = null;
            _material = null;
            _texture = null;
            _smokeMaterial = null;
            _smokeTexture = null;
        }
    }
}
