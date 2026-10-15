using System;
using System.Collections.Generic;
using System.IO;
using EFT.Ballistics;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverCarAssets : IDisposable
    {
        internal sealed class Surface
        {
            public string texture { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public float[] color { get; set; }
            public float[] scale { get; set; }
            public float[] offset { get; set; }
        }

        internal sealed class Definition
        {
            public int version { get; set; }
            public string model { get; set; }
            public string collisionModel { get; set; }

            [JsonIgnore]
            public WavefrontMesh.Geometry visual { get; set; }

            [JsonIgnore]
            public WavefrontMesh.Geometry collision { get; set; }
            public Surface[] materials { get; set; }
        }

        private Definition _data;
        private Mesh _mesh, _collision;
        private Material[] _materials;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly string _assetFolder, _objectName;
        private readonly MaterialType _ballisticMaterial;
        internal TakeoverCarAssets() : this(false)
        {
        }

        internal TakeoverCarAssets(bool armored)
        {
            _assetFolder = armored ? "reserve-armored" : "shoreline-car";
            _objectName = armored ? "ISB_DestroyedArmored_BRDM2" : "ISB_DestroyedCar";
            _ballisticMaterial = armored ? MaterialType.MetalThick : MaterialType.MetalThin;
        }

        internal void AddTo(Transform parent, string folder, float fireScale, float yaw, bool presentation, Vector3? worldPosition = null)
        {
            Prepare(folder, presentation);
            AddInstance(parent, fireScale, yaw, presentation, worldPosition);
        }

        internal void Prepare(string folder, bool presentation)
        {
            if (_data == null)
            {
                var path = Path.Combine(folder, _assetFolder, "car.json");
                if (new FileInfo(path).Length > 2 * 1024 * 1024)
                    throw new InvalidDataException("Car data exceeds 2 MB.");
                var data = JsonConvert.DeserializeObject<Definition>(File.ReadAllText(path));
                if (data == null || data.version != 2 || data.model != "visual.obj" || data.collisionModel != "collision.obj")
                    throw new InvalidDataException("Unsupported vehicle definition.");
                data.visual = WavefrontMesh.Read(Path.Combine(folder, _assetFolder, data.model));
                data.collision = WavefrontMesh.Read(Path.Combine(folder, _assetFolder, data.collisionModel));
                Validate(data);
                _data = data;
            }

            if (_collision == null)
                _collision = CreateMesh(_data.collision, _objectName + "_Collision");
            if (presentation && _materials == null)
                PrepareVisuals(folder);
        }

        private void AddInstance(Transform parent, float fireScale, float yaw, bool presentation, Vector3? worldPosition = null)
        {
            var car = new GameObject(_objectName);
            car.SetActive(false);
            car.transform.SetParent(parent, false);
            car.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (worldPosition.HasValue)
                car.transform.position = worldPosition.Value;
            car.transform.localScale = Vector3.one / fireScale;
            try
            {
                if (presentation)
                {
                    car.AddComponent<MeshFilter>().sharedMesh = _mesh;
                    var renderer = car.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = _materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }

                AddCollider(car.transform, _objectName + "_PlayerCollision", "LowPolyCollider");
                var hit = AddCollider(car.transform, _objectName + "_BulletCollision", "HighPolyCollider");
                var ballistic = hit.AddComponent<BallisticCollider>();
                var presets = EFTHardSettings.Instance.ColliderPresets;
                BallisticPreset metal = null;
                foreach (var preset in presets)
                    if (preset.MaterialType == _ballisticMaterial)
                    {
                        metal = preset;
                        break;
                    }

                if (metal == null)
                    throw new InvalidOperationException("EFT " + _ballisticMaterial + " ballistic preset missing.");
                metal.ApplyPresetValues(ballistic);
                var obstacle = car.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = _collision.bounds.center;
                obstacle.size = _collision.bounds.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                car.SetActive(true);
            }
            catch
            {
                car.SetActive(false);
                Object.Destroy(car);
                throw;
            }
        }

        private GameObject AddCollider(Transform parent, string name, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
                throw new InvalidOperationException("Missing collision layer: " + layerName);
            var obj = new GameObject(name)
            {
                layer = layer
            };
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshCollider>().sharedMesh = _collision;
            return obj;
        }

        private void PrepareVisuals(string folder)
        {
            var shader = Shader.Find("p0/Reflective/Bumped Specular SMap");
            if (shader == null || !shader.isSupported)
                shader = Shader.Find("Standard");
            if (shader == null || !shader.isSupported)
                shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("No supported car surface shader found.");
            var materials = new Material[_data.materials.Length];
            try
            {
                if (_mesh == null)
                    _mesh = CreateMesh(_data.visual, _objectName + "_Visual");
                for (int i = 0; i < materials.Length; i++)
                {
                    var surface = _data.materials[i];
                    var path = Path.Combine(folder, _assetFolder, surface.texture);
                    if (new FileInfo(path).Length > 8 * 1024 * 1024)
                        throw new InvalidDataException("Car texture exceeds 8 MB.");
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false)
                    {
                        name = _objectName + "_Albedo_" + i
                    };
                    _textures.Add(texture);
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true) || texture.width != surface.width || texture.height != surface.height)
                        throw new InvalidDataException("Invalid car texture: " + surface.texture);
                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    var material = new Material(shader)
                    {
                        name = _objectName + "_Surface_" + i
                    };
                    materials[i] = material;
                    material.mainTexture = texture;
                    material.mainTextureScale = new Vector2(surface.scale[0], surface.scale[1]);
                    material.mainTextureOffset = new Vector2(surface.offset[0], surface.offset[1]);
                    material.SetColor("_Color", new Color(surface.color[0], surface.color[1], surface.color[2], surface.color[3]));
                    if (material.HasProperty("_Glossiness"))
                        material.SetFloat("_Glossiness", .1f);
                    if (material.HasProperty("_Specularness"))
                        material.SetFloat("_Specularness", .05f);
                    if (material.HasProperty("_ReflectColor"))
                        material.SetColor("_ReflectColor", Color.black);
                }

                _materials = materials;
            }
            catch
            {
                foreach (var material in materials)
                    if (material != null)
                        Object.Destroy(material);
                foreach (var texture in _textures)
                    if (texture != null)
                        Object.Destroy(texture);
                _textures.Clear();
                throw;
            }
        }

        private static Mesh CreateMesh(WavefrontMesh.Geometry data, string name)
        {
            var mesh = new Mesh
            {
                name = name
            };
            try
            {
                var vertices = new Vector3[data.vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = V3(data.vertices[i]);
                mesh.vertices = vertices;
                if (data.normals != null && data.normals.Length == vertices.Length)
                {
                    var normals = new Vector3[vertices.Length];
                    for (int i = 0; i < normals.Length; i++)
                        normals[i] = V3(data.normals[i]);
                    mesh.normals = normals;
                }

                if (data.uv != null && data.uv.Length == vertices.Length)
                {
                    var uv = new Vector2[vertices.Length];
                    for (int i = 0; i < uv.Length; i++)
                        uv[i] = new Vector2(data.uv[i][0], data.uv[i][1]);
                    mesh.uv = uv;
                }

                mesh.subMeshCount = data.triangles.Length;
                for (int i = 0; i < data.triangles.Length; i++)
                    mesh.SetTriangles(data.triangles[i], i);
                mesh.RecalculateBounds();
                if (data.normals == null || data.normals.Length == 0)
                    mesh.RecalculateNormals();
                if (data.uv != null && data.uv.Length > 0)
                    mesh.RecalculateTangents();
                return mesh;
            }
            catch
            {
                Object.Destroy(mesh);
                throw;
            }
        }

        private static Vector3 V3(float[] v) => new Vector3(v[0], v[1], v[2]);
        internal static void Validate(Definition data)
        {
            if (data == null || data.version != 2 || data.materials == null || data.materials.Length != 4)
                throw new InvalidDataException("Unsupported car definition.");
            foreach (var geometry in new[]
            {
                data.visual,
                data.collision
            }

            )
            {
                if (geometry?.vertices == null || geometry.vertices.Length < 3 || geometry.vertices.Length > 10000 || geometry.triangles == null || geometry.triangles.Length < 1 || geometry.triangles.Length > 4)
                    throw new InvalidDataException("Invalid car geometry size.");
                CheckVectors(geometry.vertices, 3, geometry.vertices.Length);
                if (geometry.normals?.Length > 0)
                    CheckVectors(geometry.normals, 3, geometry.vertices.Length);
                if (geometry.uv?.Length > 0)
                    CheckVectors(geometry.uv, 2, geometry.vertices.Length);
                int indices = 0;
                foreach (var sub in geometry.triangles)
                {
                    if (sub == null || sub.Length % 3 != 0)
                        throw new InvalidDataException("Invalid triangle data.");
                    indices += sub.Length;
                    foreach (int index in sub)
                        if (index < 0 || index >= geometry.vertices.Length)
                            throw new InvalidDataException("Car triangle index outside vertex array.");
                }

                if (indices > 21000)
                    throw new InvalidDataException("Car triangle budget exceeded.");
            }

            if (data.visual.triangles.Length != data.materials.Length)
                throw new InvalidDataException("Car surface count mismatch.");
            for (int i = 0; i < data.materials.Length; i++)
            {
                var s = data.materials[i];
                if (s == null || s.texture != "albedo-" + i + ".png" || s.width < 1 || s.width > 2048 || s.height < 1 || s.height > 2048)
                    throw new InvalidDataException("Invalid car texture definition.");
                CheckVectors(new[] { s.color }, 4, 1);
                CheckVectors(new[] { s.scale, s.offset }, 2, 2);
            }
        }

        private static void CheckVectors(float[][] values, int width, int count)
        {
            if (values.Length != count)
                throw new InvalidDataException("Car vertex attribute length mismatch.");
            foreach (var v in values)
            {
                if (v == null || v.Length != width)
                    throw new InvalidDataException("Invalid car vector.");
                foreach (float f in v)
                    if (float.IsNaN(f) || float.IsInfinity(f))
                        throw new InvalidDataException("Nonfinite car vector.");
            }
        }

        public void Dispose()
        {
            if (_materials != null)
                foreach (var material in _materials)
                    if (material != null)
                        Object.Destroy(material);
            foreach (var texture in _textures)
                if (texture != null)
                    Object.Destroy(texture);
            if (_mesh != null)
                Object.Destroy(_mesh);
            if (_collision != null)
                Object.Destroy(_collision);
            _textures.Clear();
            _materials = null;
            _mesh = null;
            _collision = null;
            _data = null;
        }
    }
}
