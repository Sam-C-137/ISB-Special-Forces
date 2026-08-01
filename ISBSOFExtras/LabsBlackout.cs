using Audio.SpatialSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using EFT;
using EFT.Interactive;
using EFT.Weather;
using EFT.GameTriggers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using EFT.Communications;
using SPT.Reflection.Patching;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;

namespace ISBSSOFExtra
{
	public static class LabsBlackout
	{
        public static void DisableExfils()
        {
            var exfils = new HashSet<string>()
            {
                "lab_Hangar_Gate",
                "lab_Parking_Gate",
            };
            foreach (var exfil in ExfiltrationControllerClass.Instance.ExfiltrationPoints)
            {
                if (!exfils.Contains(exfil.Settings.Name))
                {
    				exfil.Status = EExfiltrationStatus.NotPresent;
                }
            }

            var sceneName = "Laboratory_Collector_Exit";
            var scene = SceneManager.GetSceneByName(sceneName);
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                if (sceneRootGO.name == "TRANSITS")
                {
                    foreach (var transit in sceneRootGO.GetComponentsInChildren<TransitPoint>(true))
                    {
                        transit.Enabled = false;
                    }
                    break;
                }
            }
        }

        public static void DisableAmbientLight()
        {
            var sceneName = "Laboratory_Scripts";
            var scene = SceneManager.GetSceneByName(sceneName);
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                var sceneRootName = sceneRootGO.name;
                if (sceneRootName == "ambientManager")
                {
                    sceneRootGO.SetActive(false);
                    continue;
                }
                if (sceneRootName == "Stencil")
                {
                    sceneRootGO.SetActive(false);
                    continue;
                }
            }

            var tod = MonoBehaviourSingleton<TODSkySimple>.Instance;
            tod.AmbientBrightness = AnimationCurve.Constant(0, 1, 0);

            var levelSettings = Singleton<LevelSettings>.Instance;
        	levelSettings.SkyColor = Color.black;
        	levelSettings.EquatorColor = Color.black;
        	levelSettings.GroundColor = Color.black;
        	levelSettings.AmbientIntensity = 0;

        	levelSettings.NightVisionSkyColor = new Color(0.030f, 0.037f, 0.042f, 1f);
        	levelSettings.NightVisionEquatorColor = new Color(0.013f, 0.014f, 0.015f, 1f);
        	levelSettings.NightVisionGroundColor = new Color(0.031f, 0.030f, 0.028f, 1f);
            levelSettings.NightVisionAmbientIntensity = 0;
        }

        public static void DisableLights()
        {
            // TODO this also disables lights on guns, which is hard to avoid, but kinda cool on its own

            var cullingManager = CullingManager.Instance;
            var indices = cullingManager.GetField<CullingManager, List<int>>("list_0");
        	var freeIndices = cullingManager.GetField<CullingManager, Queue<int>>("queue_0");
            var objects = cullingManager.GetField<CullingManager, CullingManager.GStruct69[]>("gstruct69_0");
            var baseLightField = AccessTools.Field(typeof(CullingAdvancedLightObject), "_light");

            var i = 0;
            while (i < indices.Count)
            {
                var index = indices[i];
                var cullingObject = objects[index].CullingObject;
                if (TryDisableLight(cullingObject, baseLightField))
                {
                    // unregister culling object to free cpu from processing disabled lights,
                    // interestingly on labs all culling objects are lights.
                    // Swap-remove places new object on this index, so no need for increment
                    Unregister(i, cullingObject, freeIndices, objects, indices);
                }
                else
                {
                    i++;
                }
            }
        }

        private static bool TryDisableLight(GInterface55 cullingObject, FieldInfo baseLightField)
        {
            // TODO simply disabling light is not super nice, maybe add fade or flickering? Similar to LampController

            if (cullingObject is CullingAdvancedLightObject advancedLight)
            {
                var baseLight = baseLightField.GetValue(advancedLight);
                if (baseLight is AreaLight areaLight)
                {
                    areaLight.enabled = false;
                    return true;
                }
                if (baseLight is TubeLight tubeLight)
                {
                    tubeLight.enabled = false;
                    return true;
                }
            }
            if (cullingObject is CullingLightObject light)
            {
                light.GetLight().enabled = false;
                return true;
            }

            return false;
        }

        // this is simplified version of CullingManager.Unregister, simplified
        // because list_1 (early indices) and queue_0 (free indices) don't contain any items,
        // so Contains check turns always false, and removing from list is
        // done via swap remove for speed
    	private static void Unregister(
            int index,
            GInterface55 o,
            Queue<int> queue_0,
            CullingManager.GStruct69[] gstruct69_0,
            List<int> list_0)
    	{
			queue_0.Enqueue(o.Index);
    		gstruct69_0[o.Index].Reset();

            var lastIndex = list_0.Count - 1;
            list_0[index] = list_0[lastIndex];
            list_0.RemoveAt(lastIndex);
    	}

        public static void DisableAutomaticLights()
        {
            var sceneName = "Laboratory_DesignMain";
            var scene = SceneManager.GetSceneByName(sceneName);
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                if (sceneRootGO.name == "UniversalTriggers")
                {
                    var lightsTriggers = sceneRootGO.transform.Find("Lights_triggers");
                    lightsTriggers.gameObject.SetActive(false);
                    break;
                }
            }
        }

        public static void DisableElectricSounds()
        {
            var spatialAudioSystem = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            var gclass1122_0 = spatialAudioSystem.GetField<SpatialAudioSystem, GClass1122>("gclass1122_0");
            var allRooms = gclass1122_0.GetField<GClass1122, List<SpatialAudioRoom>>("List_0");
            var currentRooms = gclass1122_0.GetField<GClass1122, List<ISpatialAudioRoom>>("List_3");

            foreach (var room in currentRooms)
            {
                room.StopRoomToneSound();
            }
            foreach (var room in allRooms)
            {
                // ambient sounds are attached to rooms,
                // can't play sound if its null, haha
                room.AmbientData.RoomTone = null;
            }

            var sceneName = "Laboratory_Scripts";
            var scene = SceneManager.GetSceneByName(sceneName);
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                if (sceneRootGO.name == "Laboratory_Sounds")
                {
                    var sceneRootTransform = sceneRootGO.transform;
                    var childrenCount = sceneRootTransform.childCount;
                    for (var i = 0; i < childrenCount; i++)
                    {
                        var child = sceneRootTransform.GetChild(i);
                        var childName = child.name;
                        if (childName.Contains("server") ||
                            childName.Contains("generator") ||
                            childName.Contains("electric") ||
                            childName.Contains("airvent") ||
                            childName.Contains("turbine"))
                        {
                            child.gameObject.SetActive(false);
                        }
                    }
                    break;
                }
            }
        }

        public static void DisableAutomaticDoors(Dictionary<string, Dictionary<int, List<List<int>>>> automaticDoorsPaths)
        {
            var scenesCount = SceneManager.sceneCount;
            for (var i = 0; i < scenesCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && automaticDoorsPaths.TryGetValue(scene.name, out var roots))
                {
                    var sceneRoots = scene.GetRootGameObjects();
                    foreach (var (rootIndex, doorPaths) in roots)
                    {
                        var root = sceneRoots[rootIndex].transform;
                        foreach (var doorPath in doorPaths)
                        {
                            var doorTrigger = Find(root, doorPath);
                            doorTrigger.gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        private static Transform Find(Transform root, List<int> path)
        {
            var result = root;
            foreach (var index in path)
            {
                result = result.GetChild(index);
            }

            return result;
        }

#if DEBUG
        public static Dictionary<string, Dictionary<int, List<List<int>>>> RecordAutomaticDoors(string outputPath, ManualLogSource logger)
        {
            var triggersList = new List<TriggerZone>(10);
            var namesList = new List<string>(15);
            var result = new Dictionary<string, Dictionary<int, List<List<int>>>>();

            logger.LogWarning("Automatic Doors:");
            var scenesCount = SceneManager.sceneCount;
            for (var i = 0; i < scenesCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    var sceneRoots = scene.GetRootGameObjects();
                    for (var k = 0; k < sceneRoots.Length; k++)
                    {
                        var sceneRoot = sceneRoots[k];
                        sceneRoot.GetComponentsInChildren<TriggerZone>(true, triggersList);
                        foreach (var trigger in triggersList)
                        {
                            var pathString = GetGameObjectPath_String(trigger.gameObject, scene.name, namesList);
                            if (pathString.Contains("Door"))
                            {
                                var path = GetGameObjectPath_Parents(trigger.gameObject, sceneRoot.name);
                                AddGameObjectPath(path, k, scene.name, result);
                                logger.LogWarning(pathString);
                            }
                            else
                            {
                                logger.LogWarning($"skipped: {pathString}");
                            }
                        }
                        triggersList.Clear();
                    }
                }
            }

            var json = JsonConvert.SerializeObject(result);
            File.WriteAllText(outputPath, json);

            return result;
        }

        private static List<int> GetGameObjectPath_Parents(GameObject obj, string sceneRootName)
        {
            var result = new List<int>();
            var current = obj.transform;

            while (current)
            {
                if (current.name == sceneRootName)
                {
                    break;
                }

                result.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            result.Reverse();

            return result;
        }

        private static string GetGameObjectPath_String(GameObject obj, string sceneName, List<string> namesList)
        {
            var current = obj.transform;

            while (current)
            {
                namesList.Add(current.name);
                current = current.parent;
            }
            namesList.Add(sceneName);

            namesList.Reverse();
            var result = string.Join("/", namesList);
            namesList.Clear();

            return result;
        }

        private static void AddGameObjectPath(
            List<int> path,
            int rootIndex,
            string sceneName,
            Dictionary<string, Dictionary<int, List<List<int>>>> result)
        {
            if (result.TryGetValue(sceneName, out var roots))
            {
                if (roots.TryGetValue(rootIndex, out var GOs))
                {
                    GOs.Add(path);
                }
                else
                {
                    roots[rootIndex] = new List<List<int>>() { path };
                }
            }
            else
            {
                result[sceneName] = new Dictionary<int, List<List<int>>> { { rootIndex, new List<List<int>>() { path } } };
            }
        }
#endif

	}

    public static class R
    {
        public static V GetField<T, V>(this T instance, string fieldName)
        {
            return (V)AccessTools.Field(typeof(T), fieldName).GetValue(instance);
        }

        public static V GetField<T, V>(string fieldName)
        {
            return (V)AccessTools.Field(typeof(T), fieldName).GetValue(null);
        }
    }
}
