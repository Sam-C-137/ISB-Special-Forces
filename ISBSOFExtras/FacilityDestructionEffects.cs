using System;
using System.Collections.Generic;
using BepInEx.Logging;
using EFT;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class FacilityDestructionEffects : IDisposable
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly Material _material;
        private readonly Texture2D _texture;
        private readonly AudioClip[] _booms;
        private readonly Vector3 _origin;
        private readonly Player _player;
        private readonly float _duration;
        private readonly Vector3 _forward;
        private int _burst;
        internal FacilityDestructionEffects(Player player, ManualLogSource log, AudioClip[] clips, float duration)
        {
            _booms = clips;
            _player = player;
            _duration = Math.Max(2f, duration);
            _origin = player != null ? player.Position : new Vector3(-210, 4, -340);
            _forward = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.forward : Vector3.forward, Vector3.up).normalized;
            if (_forward.sqrMagnitude < .1f)
                _forward = Vector3.forward;
            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null)
            {
                log.LogError("ISB: explosion particle shader is unavailable.");
                return;
            }

            _texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var colors = new Color[1024];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = ((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 240f;
                    colors[y * 32 + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - radius) * Mathf.Clamp01(1f - radius));
                }

            _texture.SetPixels(colors);
            _texture.Apply(false, true);
            _material = new Material(shader)
            {
                mainTexture = _texture
            };
            _material.SetColor("_TintColor", new Color(1f, .3f, .045f, .8f));
        }

        internal void Tick(float elapsed)
        {
            while (_burst < 10 && elapsed >= _burst * (_duration / 10f))
            {
                int number = _burst++;
                if (elapsed - number * (_duration / 10f) > .7f)
                    continue;
                var root = new GameObject("ISB_FacilityExplosion_" + number);
                _objects.Add(root);
                float approach = number / 9f;
                float angle = Mathf.Lerp(55f, 12f, approach) * (number % 2 == 0 ? -1f : 1f);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * _forward;
                float distance = Mathf.Lerp(32f, 1.8f, approach);
                Vector3 origin = _player != null ? _player.Position : _origin;
                Vector3 point = origin + direction * distance;
                if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out var floor, 6f, ~0, QueryTriggerInteraction.Ignore))
                    point = floor.point;
                root.transform.position = point + Vector3.up * .08f;
                if (_booms != null && _booms.Length > 0 && _booms[number % _booms.Length] != null)
                {
                    var audio = root.AddComponent<AudioSource>();
                    audio.spatialBlend = 1f;
                    audio.minDistance = 8f;
                    audio.maxDistance = 80f;
                    audio.dopplerLevel = 0f;
                    audio.volume = .5f + approach * .5f;
                    audio.pitch = 1f;
                    audio.PlayOneShot(_booms[number % _booms.Length]);
                }

                if (_material == null)
                    continue;
                var particles = root.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.loop = false;
                main.duration = 2f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, 1.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
                main.startSize = new ParticleSystem.MinMaxCurve(.7f, 2.5f);
                main.startColor = new Color(1f, .6f, .12f, .8f);
                main.maxParticles = 120;
                main.gravityModifier = .2f;
                var emission = particles.emission;
                emission.rateOverTime = 0;
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.radius = .5f;
                shape.angle = 32f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = _material;
                var fade = particles.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, .95f, .6f), 0f), new GradientColorKey(new Color(1f, .22f, .015f), .35f), new GradientColorKey(new Color(.12f, .045f, .01f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.8f, .3f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;
                particles.Play();
                particles.Emit(80);
            }
        }

        public void Dispose()
        {
            foreach (var obj in _objects)
                Object.Destroy(obj);
            _objects.Clear();
            Object.Destroy(_material);
            Object.Destroy(_texture);
        }
    }
}
