using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT;
using EFT.Interactive;
using EFT.UI;
using Comfort.Common;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class NightmareContainmentRoom : IDisposable
    {
        private static readonly Bounds Room = new Bounds(new Vector3(-130.75f, 1.85f, -357.515f), new Vector3(7.1f, 3.9f, 4.5f));
        internal static bool Contains(Vector3 position) => Room.Contains(position);
        internal static Vector3 ButtonPosition(Scp173Config config) => new Vector3(config.containmentButton.x, config.containmentButton.y, config.containmentButton.z);
        private EFT.Interactive.Switch _button;
        private Door _door;
        private EDoorState _originalDoor;
        private bool _originalOperatable, _locked;
        private GameObject _root, _opacityRoot;
        private Mesh _glassMesh;
        private readonly Dictionary<MeshRenderer, Material[]> _doorMaterials = new Dictionary<MeshRenderer, Material[]>();
        private Material _glassMaterial, _particleMaterial;
        private Texture2D _particleTexture;
        private ParticleSystem _gas, _fire;
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[512];
        private float _nextParticleCheck;
        private Scp173Event _event;

        internal void Prepare(Scp173Event controller, bool presentation)
        {
            _event = controller;
            _door = Resources.FindObjectsOfTypeAll<Door>().FirstOrDefault(d => d.gameObject.scene.IsValid() && d.Id == "door_Laboratory_Research_Test_Room_00001");
            if (_door == null) throw new InvalidOperationException("Nightmare containment door is missing.");
            _originalDoor = _door.DoorState;
            _originalOperatable = _door.Operatable;
            controller.ContainmentReady = true;
            if (presentation) PreparePresentation();
        }

        internal void PreparePresentation()
        {
            if (_button != null) return;
            var controller = _event;
            _root = new GameObject("ISB_NightmareContainment");
            _button = LabsTakeoverInfrastructure.CreateEmergencySwitch("ISB_NightmareContainment_Button", ISB.Compatibility.ISBLocalizedText.Get("ISB.Nightmare.Contain", "CONTAIN SCP-173"), ButtonPosition(controller.Settings), Quaternion.Euler(controller.Settings.containmentButtonRotation.x, controller.Settings.containmentButtonRotation.y, controller.Settings.containmentButtonRotation.z), true);
            if (_button == null) throw new InvalidOperationException("Nightmare containment button is missing.");
            _button.Operatable = true;
            _button.transform.SetParent(_root.transform, true);
            LabsTakeoverInfrastructure.RegisterEmergencyAction(_button, owner =>
            {
                if (owner.Player.IsYourPlayer && Singleton<GUISounds>.Instantiated)
                    Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonClick);
                controller.RequestContainment(owner.Player);
            });
            var source = Resources.FindObjectsOfTypeAll<MeshRenderer>().FirstOrDefault(r => r.gameObject.scene.IsValid() && r.name == "dispatcher_console_LOD0" && r.GetComponent<MeshFilter>()?.sharedMesh != null);
            if (source == null) throw new InvalidOperationException("Native containment control console is missing.");
            var face = new GameObject("ISB_NightmareContainment_ControlConsole");
            face.transform.SetParent(_button.transform, false);
            var mesh = source.GetComponent<MeshFilter>().sharedMesh;
            face.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = face.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            face.transform.localRotation = source.transform.rotation;
            face.transform.localScale = source.transform.lossyScale * .45f;
            face.transform.localPosition = -(face.transform.localRotation * Vector3.Scale(mesh.bounds.center, face.transform.localScale));
            controller.ContainmentReady = true;
        }

        internal void Tick(double elapsed, float duration, bool presentation)
        {
            if (elapsed < 0d) return;
            if (!_locked)
            {
                _door.SetDoorState(EDoorState.Shut, false);
                _door.Lock();
                _door.Operatable = false;
                _locked = true;
            }
            if (!presentation) return;
            if (_opacityRoot == null) CreateEffects();
            _glassMaterial.color = new Color(.035f, .04f, .045f, Mathf.Clamp01((float)elapsed / Math.Max(.1f, duration * .6f)));
            if (elapsed >= duration * .35f && elapsed < duration + 3d)
            {
                if (!_gas.isPlaying) _gas.Play();
                if (!_fire.isPlaying) _fire.Play();
            }
            if (elapsed >= duration + 3d)
            {
                _gas.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                _fire.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (Time.realtimeSinceStartup >= _nextParticleCheck)
            {
                _nextParticleCheck = Time.realtimeSinceStartup + .05f;
                Confine(_gas);
                Confine(_fire);
            }
        }

        internal void UpdateButton()
        {
            if (_button != null)
                _button.Operatable = _event.InsideContainment && _event.Facility.containmentAt < 0d;
        }

        private void CreateEffects()
        {
            var collider = Resources.FindObjectsOfTypeAll<MeshCollider>().FirstOrDefault(c => c.gameObject.scene.IsValid() && c.name == "Lab_TestCamera_COLLIDER");
            if (collider == null) throw new InvalidOperationException("Nightmare containment glass is missing.");
            JArray data;
            using (var stream = typeof(NightmareContainmentRoom).Assembly.GetManifestResourceStream("ISB.Nightmare.SightSurfaces"))
            using (var reader = new StreamReader(stream)) data = JArray.Parse(reader.ReadToEnd());
            var shape = data.First(t => (string)t["collider"] == "Lab_TestCamera_COLLIDER");
            var vertices = new List<Vector3>();
            foreach (var face in shape["triangles"])
                foreach (var index in face)
                {
                    var point = shape["vertices"][(int)index];
                    vertices.Add(collider.transform.TransformPoint(new Vector3((float)point[0], (float)point[1], (float)point[2])));
                }
            _glassMesh = new Mesh { name = "ISB_NightmareContainment_Opacity" };
            _glassMesh.SetVertices(vertices);
            var indices = new int[vertices.Count * 2];
            for (int i = 0; i < vertices.Count; i += 3)
            {
                indices[i * 2] = i; indices[i * 2 + 1] = i + 1; indices[i * 2 + 2] = i + 2;
                indices[i * 2 + 3] = i + 2; indices[i * 2 + 4] = i + 1; indices[i * 2 + 5] = i;
            }
            _glassMesh.triangles = indices;
            _glassMesh.RecalculateBounds();
            _opacityRoot = new GameObject("ISB_NightmareContainment_Opacity");
            _opacityRoot.transform.SetParent(_root.transform, false);
            _opacityRoot.AddComponent<MeshFilter>().sharedMesh = _glassMesh;
            _glassMaterial = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
            _opacityRoot.AddComponent<MeshRenderer>().sharedMaterial = _glassMaterial;
            foreach (var renderer in _door.GetComponentsInChildren<MeshRenderer>(true))
            {
                var original = renderer.sharedMaterials;
                if (!original.Any(material => material != null && material.name.StartsWith("shopping_mall_med_camp_set_tarpaulin_transparent", StringComparison.Ordinal))) continue;
                var opaque = (Material[])original.Clone();
                for (int index = 0; index < opaque.Length; index++)
                    if (opaque[index] != null && opaque[index].name.StartsWith("shopping_mall_med_camp_set_tarpaulin_transparent", StringComparison.Ordinal)) opaque[index] = _glassMaterial;
                _doorMaterials[renderer] = original;
                renderer.sharedMaterials = opaque;
            }
            if (_doorMaterials.Count == 0) throw new InvalidOperationException("Nightmare containment door window material is missing.");
            _particleMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            _particleTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var pixels = new Color[1024];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 1.5f));
            }
            _particleTexture.SetPixels(pixels); _particleTexture.Apply(false, true);
            _particleMaterial.mainTexture = _particleTexture;
            _gas = CreateParticles("ISB_NightmareContainment_Gas", new Color(.45f, .57f, .27f, .65f), 240, 70f, .4f);
            _fire = CreateParticles("ISB_NightmareContainment_Fire", new Color(1f, .22f, .025f, .9f), 128, 45f, .15f);
            _fire.transform.position = new Vector3(Room.center.x, .12f, Room.center.z);
            var fireShape = _fire.shape; fireShape.scale = new Vector3(5.4f, .05f, 2.8f);
            var fireVelocity = _fire.velocityOverLifetime; fireVelocity.y = .65f;
        }

        private ParticleSystem CreateParticles(string name, Color color, int maximum, float rate, float size)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_root.transform, false);
            root.transform.position = Room.center - Vector3.up * .5f;
            var p = root.AddComponent<ParticleSystem>();
            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.main;
            main.maxParticles = maximum; main.startColor = color; main.startLifetime = 2f; main.startSize = size; main.startSpeed = .25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            var shape = p.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(5.4f, 1f, 2.8f);
            var emission = p.emission; emission.rateOverTime = rate;
            var velocity = p.velocityOverLifetime; velocity.enabled = true; velocity.y = .25f;
            var renderer = p.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return p;
        }

        private void Confine(ParticleSystem system)
        {
            int count = system.GetParticles(_particles);
            var inner = Room; inner.Expand(-.6f);
            for (int i = 0; i < count; i++) if (!inner.Contains(_particles[i].position)) _particles[i].remainingLifetime = 0f;
            system.SetParticles(_particles, count);
        }

        public void Dispose()
        {
            if (_button != null) LabsTakeoverInfrastructure.UnregisterEmergencyAction(_button);
            if (_locked && _door != null) { _door.SetDoorState(_originalDoor, false); _door.Operatable = _originalOperatable; }
            foreach (var surface in _doorMaterials) if (surface.Key != null) surface.Key.sharedMaterials = surface.Value;
            _doorMaterials.Clear();
            Object.Destroy(_root); Object.Destroy(_glassMesh); Object.Destroy(_glassMaterial); Object.Destroy(_particleMaterial);
            Object.Destroy(_particleTexture); _particleTexture = null;
            if (_event != null) _event.ContainmentReady = false;
            _root = _opacityRoot = null; _button = null; _door = null; _locked = false; _event = null;
        }
    }
}
