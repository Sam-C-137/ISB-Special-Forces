using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal static class ManimalLabsCullingCompatibility
    {
        private static readonly Guid OfficeGuid = new Guid("aecf21d0-6986-4dfb-810e-a19922d7aace");
        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static GameObject _placeholder;
        private static bool _reported;

        internal static void Enable(ManualLogSource log)
        {
            if (_harmony != null || !Chainloader.PluginInfos.ContainsKey(ManimalLabsBlackoutCompatibility.PluginGuid))
                return;
            _log = log;
            try
            {
                _harmony = new Harmony("com.samc137.isbsofextras.manimal-culling");
                _harmony.Patch(AccessTools.Method(typeof(PerfectCullingCrossSceneGroup),
#if ISB_SPT41
                    "InitializeRuntime"
#else
                    "method_6"
#endif
                ), prefix: new HarmonyMethod(typeof(ManimalLabsCullingCompatibility), nameof(BeforeInitializeRuntime)));
            }
            catch (Exception e)
            {
                Dispose();
                log?.LogError("ISB Labs culling compatibility could not initialize: " + e.Message);
            }
        }

        private static object Read(object component, string name) => AccessTools.Field(component.GetType(), name)?.GetValue(component);
        private static void Write(object component, string name, object value) => AccessTools.Field(component.GetType(), name).SetValue(component, value);

        private static void BeforeInitializeRuntime(PerfectCullingCrossSceneGroup __instance)
        {
            if (__instance == null || (__instance.name != "SBG_Areas" && __instance.name != "SBG_Doors"))
                return;
            var scene = __instance.gameObject.scene;
            var replacement = SceneManager.GetSceneByName(ManimalLabsBlackoutCompatibility.SceneName);
            if (scene.name != "Laboratory_Culling" || !scene.isLoaded || !replacement.IsValid() || !replacement.isLoaded)
                return;
            if (new GuidReference(OfficeGuid.ToByteArray()).gameObject != null)
                return;
            try
            {
                CrossSceneCullingGroupPreProcess areas = null;
                PerfectCullingDoorPreProcess doors = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var candidate in root.GetComponentsInChildren<CrossSceneCullingGroupPreProcess>(true))
                        if (candidate.name == "SBG_Areas") areas = candidate;
                    foreach (var candidate in root.GetComponentsInChildren<PerfectCullingDoorPreProcess>(true))
                        if (candidate.name == "SBG_Doors") doors = candidate;
                }
                if (!TryLayout(areas, doors, out int groupId, out int start, out int count))
                    throw new InvalidOperationException("The replacement office has an unsupported native culling layout.");
                var obj = new GameObject("ISB_Manimal_RemovedOfficeCullingSlots");
                obj.SetActive(false);
                _placeholder = obj;
                SceneManager.MoveGameObjectToScene(obj, scene);
                var meshes = obj.AddComponent<PerfectCullingCrossSceneContentMeshes>();
                Write(meshes, "_contentGroupId", groupId);
                Write(meshes, "_contentHash", 0);
                Write(meshes, "_indexSpan", new Vector2Int(start, start + count - 1));
                Write(meshes, "_windows", Array.Empty<Renderer>());
                var slots = new PerfectCullingBakeGroup[count];
                for (int i = 0; i < count; i++) slots[i] = EmptySlot(PerfectCullingBakeGroup.GroupType.LOD, i);
                Write(meshes, "_bakeGroups", slots);
                var doorContent = obj.AddComponent<PerfectCullingCrossSceneContentDoors>();
                Write(doorContent, "_contentGroupId", 2);
                Write(doorContent, "_contentHash", 0);
                Write(doorContent, "_indexSpan", Vector2Int.zero);
                Write(doorContent, "_bakeGroups", new[] { EmptySlot(PerfectCullingBakeGroup.GroupType.Door, 0), EmptySlot(PerfectCullingBakeGroup.GroupType.User, 0) });
                var guid = obj.AddComponent<GuidComponent>();
                Write(guid, "serializedGuid", OfficeGuid.ToByteArray());
                guid.OnAfterDeserialize();
                obj.SetActive(true);
                RefreshOfficeReference((GuidReference[])Read(areas, "_cullingGroups"));
                RefreshOfficeReference((GuidReference[])Read(doors, "_doorContent"));
                if (new GuidReference(OfficeGuid.ToByteArray()).gameObject != obj)
                    throw new InvalidOperationException("Removed office culling slots were not registered.");
            }
            catch (Exception e)
            {
                if (_placeholder != null) UnityEngine.Object.Destroy(_placeholder);
                _placeholder = null;
                if (!_reported) _log?.LogError("ISB Labs culling compatibility failed: " + e.Message);
                _reported = true;
            }
        }

        private static void RefreshOfficeReference(GuidReference[] references)
        {
            foreach (var reference in references)
                if (reference.ObjectGuid == OfficeGuid)
                    Write(reference,
#if ISB_SPT41
                        "isCacheSet",
#else
                        "IsCacheSet",
#endif
                        false);
        }

        private static bool TryLayout(CrossSceneCullingGroupPreProcess areas, PerfectCullingDoorPreProcess doors, out int groupId, out int start, out int count)
        {
            groupId = start = count = 0;
            if (areas == null || doors == null) return false;
            var references = Read(areas, "_cullingGroups") as GuidReference[];
            var doorReferences = Read(doors, "_doorContent") as GuidReference[];
            if (references == null || doorReferences == null || references.Length < 2 || references.Any(r => ReferenceEquals(r, null)) || doorReferences.Any(r => ReferenceEquals(r, null))) return false;
            if (references.Count(r => r.ObjectGuid == OfficeGuid) != 1 || doorReferences.Count(r => r.ObjectGuid == OfficeGuid) != 1) return false;
            var contents = new List<PerfectCullingCrossSceneContentMeshes>();
            foreach (var reference in references)
            {
                if (reference.ObjectGuid == OfficeGuid) continue;
                var target = reference.gameObject;
                var content = target == null ? null : target.GetComponent<PerfectCullingCrossSceneContentMeshes>();
                if (content == null || content.BakeGroups == null) return false;
                contents.Add(content);
            }
            var missing = Enumerable.Range(0, references.Length).Except(contents.Select(c => c.ContentGroupId)).ToArray();
            if (missing.Length != 1 || contents.Select(c => c.ContentGroupId).Distinct().Count() != contents.Count) return false;
            groupId = missing[0];
            start = contents.Where(c => c.ContentGroupId < missing[0]).Sum(c => c.BakeGroups.Length);
            count = (int)Read(areas, "_numBakeGroups") - contents.Sum(c => c.BakeGroups.Length);
            if (count < 1 || count > 1024) return false;
            int offset = 0;
            foreach (var content in contents.OrderBy(c => c.ContentGroupId))
            {
                if (content.ContentGroupId > groupId && offset == start) offset += count;
                var span = (Vector2Int)Read(content, "_indexSpan");
                if (content.BakeGroups.Length > 0 && (span.x != offset || span.y != offset + content.BakeGroups.Length - 1)) return false;
                offset += content.BakeGroups.Length;
            }
            if (offset != (int)Read(areas, "_numBakeGroups")) return false;
            foreach (var reference in doorReferences)
            {
                if (reference.ObjectGuid == OfficeGuid) continue;
                var target = reference.gameObject;
                var content = target == null ? null : target.GetComponent<PerfectCullingCrossSceneContentDoors>();
                if (content == null || content.BakeGroups == null || content.ContentGroupId == 2) return false;
            }
            return true;
        }

        private static PerfectCullingBakeGroup EmptySlot(PerfectCullingBakeGroup.GroupType type, int serializedId)
        {
            return new PerfectCullingBakeGroup
            {
                groupType = type, serializedGroupId = serializedId, occludeMode = (EOccludeMode)2,
                renderers = Array.Empty<Renderer>(), cullingLightObjects = Array.Empty<CullingObject>(), analyticSources = Array.Empty<AnalyticSource>()
            };
        }

        internal static void Dispose()
        {
            _harmony?.UnpatchSelf(); _harmony = null;
            if (_placeholder != null) UnityEngine.Object.Destroy(_placeholder);
            _placeholder = null; _reported = false;
        }
    }
}
