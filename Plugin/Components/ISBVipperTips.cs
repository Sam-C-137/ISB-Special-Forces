using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using EFT.AssetsManager;
using HarmonyLib;
using ISB.Compatibility;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
#if SPT40
using Factory = PoolManagerClass;
using Ammo = AmmoItemClass;
using NativeHash = GClass928;
#else
using Factory = EFT.ObjectsFactory;
using NativeHash = IconsHash;
#endif

namespace ISBSpecialForces.Components
{
    internal static class ISBVipperTips
    {
        private sealed class Tip
        {
            public string name;
            public int count;
            public float[] center, extent, vertices, normals;
            public int[] triangles;
            internal Mesh Mesh;
        }
        private static Dictionary<string, Tip> Tips;
        private static Material Green;
        internal static void Initialize()
        {
            var harmony = new Harmony("com.isb.vipper.tips");
            foreach (var method in typeof(Factory).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var args = method.GetParameters();
                if (args.Length == 0 || args[0].ParameterType != typeof(Item)) continue;
                if (method.ReturnType == typeof(GameObject))
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(ISBVipperTips), nameof(Created)));
                else if (method.ReturnType == typeof(Task<GameObject>))
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(ISBVipperTips), nameof(CreatedAsync)));
            }
            harmony.Patch(AccessTools.Method(typeof(AmmoPoolObject), nameof(AmmoPoolObject.SetUsed)),
                postfix: new HarmonyMethod(typeof(ISBVipperTips), nameof(Used)));
            harmony.Patch(AccessTools.Method(typeof(NativeHash),
#if SPT40
                "smethod_1"
#else
                "HashForItem"
#endif
                ), postfix: new HarmonyMethod(typeof(ISBVipperTips), nameof(IconHash)));
        }
        private static void Created(Item __0, GameObject __result) => Apply(__result, __0);
        private static void Used(AmmoPoolObject __instance, bool __0) => __instance.GetComponent<ISBVipperTipBinding>()?.SetUsed(__0);
        private static void CreatedAsync(Item __0, ref Task<GameObject> __result)
        {
            if (__0 is Ammo) __result = Completed(__result, __0);
        }
        private static async Task<GameObject> Completed(Task<GameObject> task, Item item)
        {
            var obj = await task;
            Apply(obj, item);
            return obj;
        }
        private static void IconHash(Item __0, ref int __result)
        {
            if (VipperPoisonIdentity.IsPoisoned(__0?.StringTemplateId))
                __result = unchecked(__result * 397 ^ 0x13703f4);
        }
        internal static void Apply(GameObject obj, Item item)
        {
            if (obj == null || !(item is Ammo) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            bool poisoned = VipperPoisonIdentity.IsPoisoned(item.StringTemplateId);
            var binding = obj.GetComponent<ISBVipperTipBinding>();
            if (binding == null && !poisoned) return;
            if (binding == null) binding = obj.AddComponent<ISBVipperTipBinding>();
            if (!binding.Prepared && poisoned) Prepare(obj, binding);
            binding.Set(poisoned, ((Ammo)item).IsUsed);
        }
        private static void Load()
        {
            if (Tips != null) return;
            using (var stream = typeof(ISBVipperTips).Assembly.GetManifestResourceStream("ISB.PoisonTips.json"))
            using (var reader = new StreamReader(stream ?? throw new InvalidDataException("Poison tip geometry is missing.")))
                Tips = JsonConvert.DeserializeObject<Tip[]>(reader.ReadToEnd()).ToDictionary(t => t.name + "|" + t.count);
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            if (shader == null) throw new InvalidOperationException("Poison tip material shader is unavailable.");
            Green = new Material(shader) { name = "ISB Poisoned Green Tip", color = new Color(.22f, .43f, .12f, 1f) };
            var stain = new Texture2D(32, 32, TextureFormat.RGB24, true) { name = "ISB Poison Stain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float shade = .72f + .28f * Mathf.PerlinNoise(x * .2f, y * .2f);
                    pixels[y * 32 + x] = new Color(shade, shade, shade, 1f);
                }
            stain.SetPixels(pixels);
            stain.Apply(true, true);
            Green.mainTexture = stain;
            if (Green.HasProperty("_Metallic")) Green.SetFloat("_Metallic", 0f);
            if (Green.HasProperty("_Glossiness")) Green.SetFloat("_Glossiness", .1f);
        }
        private static void Prepare(GameObject root, ISBVipperTipBinding binding)
        {
            Load();
            var additions = new Dictionary<Renderer, Renderer>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !Tips.TryGetValue(mesh.name + "|" + mesh.vertexCount, out var tip)) continue;
                if ((mesh.bounds.center - Vector(tip.center, 0)).sqrMagnitude > .00000001f ||
                    (mesh.bounds.extents - Vector(tip.extent, 0)).sqrMagnitude > .00000001f) continue;
                var source = filter.GetComponent<MeshRenderer>();
                if (source == null) continue;
                if (tip.Mesh == null)
                {
                    int count = tip.vertices.Length / 3;
                    var vertices = new Vector3[count];
                    var normals = new Vector3[count];
                    var uv = new Vector2[count];
                    for (int i = 0; i < count; i++) { vertices[i] = Vector(tip.vertices, i * 3); normals[i] = Vector(tip.normals, i * 3); }
                    for (int i = 0; i < count; i++)
                        uv[i] = new Vector2(vertices[i].x * 350f + vertices[i].y * 87f, vertices[i].z * 350f + vertices[i].y * 121f);
                    tip.Mesh = new Mesh { name = "ISB Poison Tip " + tip.name, vertices = vertices, normals = normals, uv = uv, triangles = tip.triangles };
                    tip.Mesh.RecalculateBounds();
                    tip.Mesh.UploadMeshData(true);
                }
                var cap = new GameObject("ISB Poison Tip") { layer = filter.gameObject.layer };
                cap.transform.SetParent(filter.transform, false);
                cap.AddComponent<MeshFilter>().sharedMesh = tip.Mesh;
                var renderer = cap.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Green;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = source.lightProbeUsage;
                renderer.reflectionProbeUsage = source.reflectionProbeUsage;
                additions[source] = renderer;
                binding.Caps.Add(cap);
            }
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    lods[i].renderers = lods[i].renderers.Concat(lods[i].renderers.Where(r => r != null && additions.ContainsKey(r)).Select(r => additions[r])).ToArray();
                group.SetLODs(lods);
            }
            binding.Prepared = true;
        }
        private static Vector3 Vector(float[] values, int offset) => new Vector3(values[offset], values[offset + 1], values[offset + 2]);
    }
    public sealed class ISBVipperTipBinding : MonoBehaviour
    {
        internal readonly List<GameObject> Caps = new List<GameObject>();
        internal bool Prepared;
        private bool Poisoned, Used;
        internal void Set(bool poisoned, bool used) { Poisoned = poisoned; Used = used; Refresh(); }
        internal void SetUsed(bool used) { Used = used; Refresh(); }
        private void Refresh() { foreach (var cap in Caps) if (cap != null) cap.SetActive(Poisoned && !Used); }

    }
}
