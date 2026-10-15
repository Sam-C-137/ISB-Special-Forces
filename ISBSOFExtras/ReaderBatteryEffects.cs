using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal static class ReaderBatteryEffects
    {
        private sealed class Burst
        {
            internal GameObject Root;
            internal Light Flash;
            internal float Started, NextPulse, LastPulse, Duration;
            internal string Token;
            internal bool Modified;
            internal ParticleSystem Particles;
            internal AudioSource Sound;
        }

        private static readonly List<Burst> Active = new List<Burst>(4);
        private static readonly HashSet<string> Played = new HashSet<string>(StringComparer.Ordinal);
        private static Material _material;
        private static AudioClip _clip;
        private static bool _failed;
        internal static void Play(KeycardDoor reader, bool modified, string token, float duration = 0f)
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (_failed || reader == null || player == null || player.IsAI || string.IsNullOrEmpty(token) || !Played.Add(token))
                return;
            try
            {
                bool found = false;
                if (reader.Proxies != null)
                    foreach (var proxy in reader.Proxies)
                    {
                        if (proxy == null)
                            continue;
                        found = true;
                        var target = proxy.StatusLight != null ? proxy.StatusLight.transform : proxy.transform;
                        Emit(target.position + target.forward * .035f, target.rotation, player.Transform.position, modified, token, duration);
                    }

                if (!found)
                    Emit(reader.transform.position + Vector3.up * 1.15f, reader.transform.rotation, player.Transform.position, modified, token, duration);
            }
            catch
            {
                Reset();
                _failed = true;
            }
        }

        private static void Prepare()
        {
            if (_material != null)
                return;
            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Reader spark shader unavailable.");
            _material = new Material(shader)
            {
                name = "ISB_ReaderSparks",
                mainTexture = Texture2D.whiteTexture
            };
            _material.SetColor("_TintColor", Color.white);
            const int sampleRate = 22050;
            var samples = new float[(int)(sampleRate * .32f)];
            var random = new System.Random(137);
            float previous = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                float envelope = (float)(Math.Exp(-t * 36f) + .38f * Math.Exp(-Math.Abs(t - .058f) * 170f) + .15f * Math.Exp(-Math.Abs(t - .115f) * 130f));
                samples[i] = Mathf.Clamp((noise - previous * .65f) * envelope * .4f, -.8f, .8f);
                previous = noise;
            }

            _clip = AudioClip.Create("ISB_ReaderElectricalArc", samples.Length, 1, sampleRate, false);
            _clip.SetData(samples, 0);
        }

        private static void Emit(Vector3 position, Quaternion rotation, Vector3 viewer, bool modified, string token, float duration)
        {
            if ((position - viewer).sqrMagnitude > 625f || Active.Count >= 4)
                return;
            Prepare();
            var root = new GameObject(modified ? "ISB_ReaderBatterySparks" : "ISB_ReaderBatteryShortCircuit");
            root.SetActive(false);
            var burst = new Burst
            {
                Root = root,
                Started = Time.realtimeSinceStartup,
                Modified = modified,
                Token = token,
                Duration = duration > 0f ? duration : modified ? 2f : 3f
            };
            Active.Add(burst);
            root.transform.SetPositionAndRotation(position, rotation);
            var particles = root.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = burst.Duration;
            main.maxParticles = 96;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.12f, .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.6f);
            main.startSize = modified ? new ParticleSystem.MinMaxCurve(.006f, .018f) : new ParticleSystem.MinMaxCurve(.018f, .07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .42f, .09f), new Color(1f, .9f, .55f));
            main.gravityModifier = .55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 65f;
            shape.radius = .025f;
            var collision = particles.collision;
            collision.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.renderMode = modified ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 2f;
            renderer.velocityScale = .045f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .62f, .24f);
            light.range = modified ? .85f : 1.3f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            burst.Flash = light;
            burst.Particles = particles;
            var sound = root.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.clip = _clip;
            sound.spatialBlend = 1f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.minDistance = .6f;
            sound.maxDistance = 12f;
            sound.dopplerLevel = 0f;
            sound.volume = modified ? .4f : .7f;
            burst.Sound = sound;
            root.SetActive(true);
            particles.Play(false);
            Pulse(burst, Time.realtimeSinceStartup);
        }

        private static void Pulse(Burst burst, float now)
        {
            burst.LastPulse = now;
            burst.NextPulse = now + (burst.Modified ? .28f : .42f);
            burst.Particles.Emit(burst.Modified ? 16 : 28);
            burst.Sound.Play();
        }

        internal static void Stop(string token)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i].Token == token)
                {
                    if (Active[i].Root != null)
                        Object.Destroy(Active[i].Root);
                    Active.RemoveAt(i);
                }
        }

        internal static void Tick()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var burst = Active[i];
                float elapsed = Time.realtimeSinceStartup - burst.Started;
                float duration = burst.Duration;
                if (elapsed > duration + .4f || burst.Root == null)
                {
                    if (burst.Root != null)
                        Object.Destroy(burst.Root);
                    Active.RemoveAt(i);
                }
                else
                {
                    if (elapsed < duration && Time.realtimeSinceStartup >= burst.NextPulse)
                        Pulse(burst, Time.realtimeSinceStartup);
                    if (burst.Flash != null)
                        burst.Flash.intensity = (burst.Modified ? 1.2f : 2.6f) * Mathf.Clamp01(1f - (Time.realtimeSinceStartup - burst.LastPulse) / (burst.Modified ? .09f : .16f));
                }
            }
        }

        internal static void Reset()
        {
            foreach (var burst in Active)
                if (burst.Root != null)
                    Object.Destroy(burst.Root);
            Active.Clear();
            Played.Clear();
            if (_material != null)
                Object.Destroy(_material);
            if (_clip != null)
                Object.Destroy(_clip);
            _material = null;
            _clip = null;
            _failed = false;
        }
    }
}
