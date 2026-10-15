using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using EFT.Ballistics;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverPropPoint
    {
        public string id { get; set; }
        public string model { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotationX { get; set; }
        public float rotationY { get; set; }
        public float rotationZ { get; set; }
        public float scale { get; set; } = 1f;
    }

    internal sealed class TakeoverPropsDefinition
    {
        public bool enabled { get; set; } = true;
        public TakeoverPropPoint[] props { get; set; } = Array.Empty<TakeoverPropPoint>();
    }

    internal sealed class TakeoverPropMap
    {
        public string property { get; set; }
        public string file { get; set; }
        public float[] scale { get; set; } = new[]
        {
            1f,
            1f
        };
        public float[] offset { get; set; } = new[]
        {
            0f,
            0f
        };
    }

    internal sealed class TakeoverPropMaterial
    {
        public string name { get; set; }
        public string texture { get; set; }
        public float[] color { get; set; } = new[]
        {
            1f,
            1f,
            1f,
            1f
        };
        public float[] scale { get; set; } = new[]
        {
            1f,
            1f
        };
        public float[] offset { get; set; } = new[]
        {
            0f,
            0f
        };
        public string mode { get; set; }
        public string shader { get; set; }
        public Dictionary<string, float> floats { get; set; }
        public Dictionary<string, float[]> vectors { get; set; }
        public TakeoverPropMap[] maps { get; set; }
        public float cutoff { get; set; } = .3f;
    }

    internal sealed class TakeoverPropMesh
    {
        public string name { get; set; }
        public string file { get; set; }
        public int[] materials { get; set; }
        public string kind { get; set; }
        public string material { get; set; }
    }

    internal sealed class TakeoverPropModel
    {
        public int version { get; set; }
        public bool navigationObstacle { get; set; } = true;
        public float[] boundsCenter { get; set; }
        public float[] boundsSize { get; set; }
        public TakeoverPropMaterial[] materials { get; set; }
        public TakeoverPropMesh[] meshes { get; set; }
        public TakeoverPropMesh[] colliders { get; set; }
    }

    internal sealed class TakeoverProps : IDisposable
    {
        private sealed class Model : IDisposable
        {
            internal TakeoverPropModel Data;
            internal Mesh[] Meshes, Colliders;
            internal Material[] Materials;
            internal readonly Dictionary<string, Mesh> MeshAssets = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, Texture2D> TextureAssets = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<Texture2D> Textures = new List<Texture2D>();
            public void Dispose()
            {
                foreach (var value in MeshAssets.Values)
                    if (value != null)
                        Object.Destroy(value);
                MeshAssets.Clear();
                TextureAssets.Clear();
                if (Materials != null)
                    foreach (var value in Materials)
                        if (value != null)
                            Object.Destroy(value);
                foreach (var value in Textures)
                    if (value != null)
                        Object.Destroy(value);
                Textures.Clear();
            }
        }

        private readonly Dictionary<string, Model> _models = new Dictionary<string, Model>(StringComparer.Ordinal);
        private readonly List<GameObject> _instances = new List<GameObject>();
        private TakeoverPropsDefinition _definition;
        private string _folder;
        private ManualLogSource _log;
        private bool _prepared;
        internal void Load(string folder, ManualLogSource log)
        {
            Dispose();
            _folder = folder;
            _log = log;
            if (string.IsNullOrEmpty(folder))
                return;
            var path = Path.Combine(folder, "takeover_props.json");
            if (!File.Exists(path))
                return;
            try
            {
                _definition = ReadJson<TakeoverPropsDefinition>(path);
                if (_definition?.props == null || _definition.props.Length > 128)
                    throw new InvalidDataException("Expected at most 128 scenery objects.");
            }
            catch (Exception ex)
            {
                _definition = null;
                _log?.LogError("[ISB Scenery] Configuration could not be loaded: " + ex.Message);
            }
        }

        internal void Prepare(bool presentation, bool disableVehicles)
        {
            if (_prepared || _definition?.enabled != true)
                return;
            _prepared = true;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in _definition.props)
            {
                GameObject instance = null;
                try
                {
                    ValidatePoint(point);
                    if (!ids.Add(point.id))
                        throw new InvalidDataException("Duplicate object id: " + point.id);
                    if (disableVehicles && point.model == "suv")
                        continue;
                    if (!_models.TryGetValue(point.model, out var model))
                    {
                        model = LoadModel(point.model, presentation);
                        _models.Add(point.model, model);
                    }

                    instance = new GameObject("ISB_" + point.id);
                    instance.SetActive(false);
                    instance.transform.position = new Vector3(point.x, point.y, point.z);
                    instance.transform.rotation = Quaternion.Euler(point.rotationX, point.rotationY, point.rotationZ);
                    instance.transform.localScale = Vector3.one * point.scale;
                    if (presentation)
                        for (int i = 0; i < model.Meshes.Length; i++)
                        {
                            var part = Child(instance.transform, model.Data.meshes[i].name, 0);
                            part.AddComponent<MeshFilter>().sharedMesh = model.Meshes[i];
                            var renderer = part.AddComponent<MeshRenderer>();
                            renderer.sharedMaterials = Array.ConvertAll(model.Data.meshes[i].materials, index => model.Materials[index]);
                            renderer.shadowCastingMode = ShadowCastingMode.On;
                            renderer.receiveShadows = true;
                        }

                    for (int i = 0; i < model.Colliders.Length; i++)
                    {
                        var definition = model.Data.colliders[i];
                        bool ballistic = definition.kind == "ballistic";
                        var part = Child(instance.transform, definition.name + "_" + definition.kind, RequiredLayer(ballistic ? "HighPolyCollider" : "LowPolyCollider"));
                        part.AddComponent<MeshCollider>().sharedMesh = model.Colliders[i];
                        if (ballistic)
                        {
                            var material = (MaterialType)Enum.Parse(typeof(MaterialType), definition.material, false);
                            var preset = Array.Find(EFTHardSettings.Instance.ColliderPresets, value => value.MaterialType == material);
                            if (preset == null)
                                throw new InvalidDataException("Ballistic material unavailable: " + definition.material);
                            preset.ApplyPresetValues(part.AddComponent<BallisticCollider>());
                        }
                    }

                    if (point.model == "barbed-wire")
                    {
                        var part = Child(instance.transform, "BarbedWireTrigger", RequiredLayer("Triggers"));
                        var collider = part.AddComponent<BoxCollider>();
                        collider.isTrigger = true;
                        collider.center = V3(model.Data.boundsCenter);
                        collider.size = V3(model.Data.boundsSize);
                        part.AddComponent<TakeoverBarbedWire>();
                    }
                    else if (model.Data.navigationObstacle)
                    {
                        var obstacle = instance.AddComponent<NavMeshObstacle>();
                        obstacle.shape = NavMeshObstacleShape.Box;
                        obstacle.center = V3(model.Data.boundsCenter);
                        obstacle.size = V3(model.Data.boundsSize);
                        obstacle.carving = true;
                        obstacle.carveOnlyStationary = true;
                    }

                    instance.SetActive(true);
                    var wire = instance.GetComponentInChildren<TakeoverBarbedWire>();
                    if (wire != null)
                        wire.PrepareForActivation();
                    _instances.Add(instance);
                }
                catch (Exception ex)
                {
                    if (instance != null)
                    {
                        instance.SetActive(false);
                        Object.Destroy(instance);
                    }

                    _log?.LogError("[ISB Scenery] Object " + (point?.id ?? "<null>") + " skipped: " + ex.Message);
                }
            }

            Physics.SyncTransforms();
        }

        internal static void ValidatePoint(TakeoverPropPoint point)
        {
            if (point == null || string.IsNullOrEmpty(point.id) || point.id.Length > 64)
                throw new InvalidDataException("Invalid scenery object id.");
            foreach (char c in point.id)
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    throw new InvalidDataException("Invalid scenery object id.");
            if (point.model != "suv" && point.model != "sandbags" && point.model != "rubble" && point.model != "barbed-wire" && point.model != "military-crates" && point.model != "industrial-crates" && point.model != "wrapped-cargo")
                throw new InvalidDataException("Unknown scenery model: " + point.model);
            foreach (var value in new[]
            {
                point.x,
                point.y,
                point.z,
                point.rotationX,
                point.rotationY,
                point.rotationZ,
                point.scale
            }

            )
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidDataException("Invalid scenery transform.");
            if (point.scale <= 0f || point.scale > 3f)
                throw new InvalidDataException("Scenery scale must be greater than 0 and at most 3.");
        }

        private Model LoadModel(string name, bool presentation)
        {
            var folder = Path.Combine(_folder, "props", name);
            var model = new Model();
            try
            {
                model.Data = ReadJson<TakeoverPropModel>(Path.Combine(folder, "model.json"));
                var data = model.Data;
                if (data == null || data.version != 1 || data.meshes == null || data.meshes.Length < 1 || data.meshes.Length > 16 || data.colliders == null || data.colliders.Length > 16 || data.materials == null || data.materials.Length < 1 || data.materials.Length > 16)
                    throw new InvalidDataException("Invalid scenery model definition.");
                Vector(data.boundsCenter, 3);
                Vector(data.boundsSize, 3);
                foreach (float size in data.boundsSize)
                    if (size <= 0f || size > 30f)
                        throw new InvalidDataException("Invalid scenery bounds.");
                model.Colliders = new Mesh[data.colliders.Length];
                for (int i = 0; i < model.Colliders.Length; i++)
                {
                    var definition = data.colliders[i];
                    if (definition.kind != "player" && definition.kind != "ballistic")
                        throw new InvalidDataException("Invalid collider kind.");
                    if (definition.kind == "ballistic" && !Enum.IsDefined(typeof(MaterialType), definition.material))
                        throw new InvalidDataException("Invalid ballistic material.");
                    string path = AssetPath(folder, definition.file, ".obj");
                    if (!model.MeshAssets.TryGetValue(path, out var mesh))
                    {
                        bool rendered = presentation && Array.Exists(data.meshes, value => string.Equals(value.file, definition.file, StringComparison.OrdinalIgnoreCase));
                        mesh = ReadMesh(path, name + "_Collision_" + i, rendered);
                        model.MeshAssets.Add(path, mesh);
                    }

                    model.Colliders[i] = mesh;
                }

                model.Meshes = new Mesh[presentation ? data.meshes.Length : 0];
                model.Materials = new Material[presentation ? data.materials.Length : 0];
                if (presentation)
                {
                    for (int i = 0; i < model.Materials.Length; i++)
                        model.Materials[i] = ReadMaterial(folder, data.materials[i], model);
                    for (int i = 0; i < model.Meshes.Length; i++)
                    {
                        var definition = data.meshes[i];
                        string path = AssetPath(folder, definition.file, ".obj");
                        if (!model.MeshAssets.TryGetValue(path, out var mesh))
                        {
                            mesh = ReadMesh(path, name + "_Visual_" + i, true);
                            model.MeshAssets.Add(path, mesh);
                        }

                        model.Meshes[i] = mesh;
                        if (definition.materials == null || definition.materials.Length != model.Meshes[i].subMeshCount)
                            throw new InvalidDataException("Scenery material count mismatch.");
                        foreach (int index in definition.materials)
                            if (index < 0 || index >= model.Materials.Length)
                                throw new InvalidDataException("Invalid scenery material index.");
                    }

                    foreach (var mesh in model.MeshAssets.Values)
                        if (Array.IndexOf(model.Colliders, mesh) < 0)
                            mesh.UploadMeshData(true);
                }

                return model;
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }

        private static Material ReadMaterial(string folder, TakeoverPropMaterial data, Model model)
        {
            Vector(data.color, 4);
            Vector(data.scale, 2);
            Vector(data.offset, 2);
            bool cutout = data.mode == "cutout", glass = data.mode == "glass";
            var shaderName = data.shader?.Replace("Bumped Emissive Specular SMap", "Bumped Specular SMap");
            var shader = !glass && !string.IsNullOrEmpty(shaderName) ? Shader.Find(shaderName) : null;
            if (shader == null || !shader.isSupported)
                shader = Shader.Find(cutout ? "Legacy Shaders/Transparent/Cutout/Diffuse" : glass ? "Legacy Shaders/Transparent/Diffuse" : "Legacy Shaders/Diffuse");
            if (shader == null || !shader.isSupported)
                throw new InvalidDataException("Scenery surface shader unavailable.");
            var material = new Material(shader)
            {
                name = "ISB_Prop_" + data.name
            };
            try
            {
                if (!glass && data.floats != null)
                    foreach (var entry in data.floats)
                        if (material.HasProperty(entry.Key))
                            material.SetFloat(entry.Key, entry.Value);
                if (!glass && data.vectors != null)
                    foreach (var entry in data.vectors)
                    {
                        Vector(entry.Value, 4);
                        if (material.HasProperty(entry.Key))
                            material.SetVector(entry.Key, new Vector4(entry.Value[0], entry.Value[1], entry.Value[2], entry.Value[3]));
                    }

                if (material.HasProperty("_ReflectColor"))
                    material.SetColor("_ReflectColor", Color.black);
                if (material.HasProperty("_EmissionPower"))
                    material.SetFloat("_EmissionPower", 0f);
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", Color.black);
                material.color = new Color(data.color[0], data.color[1], data.color[2], data.color[3]);
                if (cutout)
                    material.SetFloat("_Cutoff", Mathf.Clamp01(data.cutoff));
                if (!string.IsNullOrEmpty(data.texture))
                {
                    var path = AssetPath(folder, data.texture, ".png");
                    var texture = ReadTexture(path, false, model);
                    material.mainTexture = texture;
                    material.mainTextureScale = new Vector2(data.scale[0], data.scale[1]);
                    material.mainTextureOffset = new Vector2(data.offset[0], data.offset[1]);
                }

                if (!glass && data.maps != null)
                    foreach (var map in data.maps)
                    {
                        if (map.property != "_BumpMap" && map.property != "_SpecMap")
                            throw new InvalidDataException("Unsupported scenery map.");
                        if (!material.HasProperty(map.property))
                            continue;
                        Vector(map.scale, 2);
                        Vector(map.offset, 2);
                        var path = AssetPath(folder, map.file, ".png");
                        var texture = ReadTexture(path, true, model);
                        material.SetTexture(map.property, texture);
                        material.SetTextureScale(map.property, new Vector2(map.scale[0], map.scale[1]));
                        material.SetTextureOffset(map.property, new Vector2(map.offset[0], map.offset[1]));
                    }

                return material;
            }
            catch
            {
                Object.Destroy(material);
                throw;
            }
        }

        private static Texture2D ReadTexture(string path, bool linear, Model model)
        {
            string key = path + (linear ? "|linear" : "|color");
            if (model.TextureAssets.TryGetValue(key, out var texture))
                return texture;
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Scenery texture exceeds budget.");
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = "ISB_Prop_" + Path.GetFileNameWithoutExtension(path)
            };
            model.Textures.Add(texture);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true) || texture.width > 2048 || texture.height > 2048)
                throw new InvalidDataException("Invalid scenery texture.");
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            model.TextureAssets.Add(key, texture);
            return texture;
        }

        private static Mesh ReadMesh(string path, string name, bool rendered)
        {
            var data = WavefrontMesh.Read(path, 20000, 120000, 8, 8 * 1024 * 1024);
            if (data.vertices.Length < 3 || data.triangles.Length < 1 || (data.normals.Length != 0 && data.normals.Length != data.vertices.Length) || (data.uv.Length != 0 && data.uv.Length != data.vertices.Length))
                throw new InvalidDataException("Invalid scenery mesh.");
            var mesh = new Mesh
            {
                name = name
            };
            try
            {
                mesh.vertices = Array.ConvertAll(data.vertices, V3);
                if (rendered && data.normals.Length > 0)
                    mesh.normals = Array.ConvertAll(data.normals, V3);
                if (rendered && data.uv.Length > 0)
                    mesh.uv = Array.ConvertAll(data.uv, value => new Vector2(value[0], value[1]));
                mesh.subMeshCount = data.triangles.Length;
                for (int i = 0; i < data.triangles.Length; i++)
                    mesh.SetTriangles(data.triangles[i], i);
                mesh.RecalculateBounds();
                if (rendered && data.normals.Length == 0)
                    mesh.RecalculateNormals();
                if (rendered && data.uv.Length > 0)
                    mesh.RecalculateTangents();
                return mesh;
            }
            catch
            {
                Object.Destroy(mesh);
                throw;
            }
        }

        internal static string AssetPath(string folder, string file, string extension)
        {
            if (string.IsNullOrEmpty(file) || file != Path.GetFileName(file) || file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid scenery asset path.");
            return Path.Combine(folder, file);
        }

        private static T ReadJson<T>(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new InvalidDataException("Scenery definition exceeds budget.");
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }

        private static void Vector(float[] values, int count)
        {
            if (values == null || values.Length != count)
                throw new InvalidDataException("Invalid scenery vector.");
            foreach (var value in values)
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidDataException("Invalid scenery vector.");
        }

        private static Vector3 V3(float[] value) => new Vector3(value[0], value[1], value[2]);
        private static int RequiredLayer(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0)
                throw new InvalidDataException("Missing physics layer: " + name);
            return layer;
        }

        private static GameObject Child(Transform parent, string name, int layer)
        {
            var item = new GameObject(name)
            {
                layer = layer
            };
            item.transform.SetParent(parent, false);
            return item;
        }

        public void Dispose()
        {
            foreach (var item in _instances)
                if (item != null)
                {
                    item.SetActive(false);
                    Object.Destroy(item);
                }

            _instances.Clear();
            foreach (var model in _models.Values)
                model.Dispose();
            _models.Clear();
            _definition = null;
            _prepared = false;
        }
    }
}
