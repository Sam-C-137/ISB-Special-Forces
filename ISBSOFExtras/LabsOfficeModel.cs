using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed partial class LabsOfficeScene
    {
        private AssetBundle _keypadBundle;
        private readonly List<Material> _keypadMaterials = new List<Material>();
        private readonly Dictionary<string, AudioSource> _keypadSounds = new Dictionary<string, AudioSource>();
        private void CreateReaderModel()
        {
            try
            {
                using (var stream = typeof(LabsOfficeScene).Assembly.GetManifestResourceStream("ISB.Office.keypad.bundle"))
                using (var data = new MemoryStream())
                {
                    if (stream == null)
                        throw new InvalidOperationException("The office keypad bundle is missing.");
                    stream.CopyTo(data);
                    _keypadBundle = AssetBundle.LoadFromMemory(data.ToArray());
                }

                if (_keypadBundle == null)
                    throw new InvalidOperationException("The office keypad bundle could not be loaded.");
                var prefab = _keypadBundle.LoadAsset<GameObject>("Assets/keypad/keypad.prefab");
                if (prefab == null)
                    throw new InvalidOperationException("The physical keypad prefab is missing.");
                _readerModel = Object.Instantiate(prefab, _keypadDoor.transform, false);
                _readerModel.name = "Model";
                _readerModel.transform.localPosition = Vector3.zero;
                _readerModel.transform.localRotation = prefab.transform.localRotation;
                var shader = Shader.Find("p0/Reflective/Bumped Specular SMap");
                if (shader == null || !shader.isSupported)
                    shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The physical keypad shader is unavailable.");
                foreach (var renderer in _readerModel.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null || materials[i].name != "Keypad")
                            continue;
                        var material = new Material(materials[i])
                        {
                            shader = shader
                        };
                        _keypadMaterials.Add(material);
                        materials[i] = material;
                    }

                    renderer.sharedMaterials = materials;
                    renderer.enabled = true;
                    renderer.forceRenderingOff = false;
                }

                foreach (var sound in _readerModel.GetComponentsInChildren<AudioSource>(true))
                {
                    sound.playOnAwake = false;
                    sound.enabled = true;
                    sound.spatialBlend = 1f;
                    sound.minDistance = 1f;
                    sound.maxDistance = 12f;
                    sound.volume = .5f;
                    sound.loop = false;
                    _keypadSounds[sound.name] = sound;
                }

                var mesh = _readerModel.GetComponent<MeshFilter>();
                if (mesh?.sharedMesh == null)
                    throw new InvalidOperationException("The physical keypad mesh is missing.");
                var local = mesh.sharedMesh.bounds;
                var matrix = _keypadDoor.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
                var collider = _keypadDoor.GetComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size + new Vector3(.03f, .03f, .03f);
            }
            catch
            {
                DisposeKeypadModel();
                throw;
            }
        }

        private void PlayKeypadSound(string name)
        {
            if (_keypadSounds.TryGetValue(name, out var source) && source != null && source.clip != null)
                source.PlayOneShot(source.clip);
        }

        private void DisposeKeypadModel()
        {
            Object.Destroy(_readerModel);
            _readerModel = null;
            foreach (var material in _keypadMaterials)
                Object.Destroy(material);
            _keypadMaterials.Clear();
            _keypadSounds.Clear();
            if (_keypadBundle != null)
                _keypadBundle.Unload(true);
            _keypadBundle = null;
        }
    }
}
