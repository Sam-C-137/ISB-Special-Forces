using Audio.SpatialSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
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
        private sealed class AmbientState
        {
            public bool Captured;
            public GameObject AmbientManager;
            public bool AmbientManagerActive;
            public GameObject Stencil;
            public bool StencilActive;
            public AnimationCurve TodAmbientBrightness;
            public Color SkyColor;
            public Color EquatorColor;
            public Color GroundColor;
            public float AmbientIntensity;
            public Color NightVisionSkyColor;
            public Color NightVisionEquatorColor;
            public Color NightVisionGroundColor;
            public float NightVisionAmbientIntensity;
        }

        private sealed class SimpleLightState
        {
            public CullingLightObject Culling;
            public float Multiplier;
            public Light Light;
            public bool Enabled;
            public float Intensity;
        }

        private sealed class AdvancedLightState
        {
            public CullingAdvancedLightObject Culling;
            public float Multiplier;
            public Behaviour Light;
            public bool Enabled;
        }

        private sealed class GameObjectState
        {
            public GameObject Object;
            public bool Active;
        }

        private sealed class RoomToneState
        {
            public object Target;
            public MemberInfo Member;
            public object Value;
        }

        private sealed class MaterialState
        {
            public bool HasSpec;
            public Vector4 Spec;
            public bool HasEmissionPower;
            public float EmissionPower;
            public bool HasEmissionColor;
            public Color EmissionColor;
        }

        private static readonly AmbientState SavedAmbient = new AmbientState();
        private static readonly List<SimpleLightState> SavedSimpleLights = new List<SimpleLightState>();
        private static readonly List<AdvancedLightState> SavedAdvancedLights = new List<AdvancedLightState>();
        private static readonly List<GameObjectState> SavedElectricObjects = new List<GameObjectState>();
        private static readonly List<RoomToneState> SavedRoomTones = new List<RoomToneState>();
        private static readonly Dictionary<Material, MaterialState> SavedMaterials = new Dictionary<Material, MaterialState>();
        private static GameObjectState SavedAutomaticLights;
        private static bool LightsCaptured;
        private static bool ElectricSoundsCaptured;
        private static int _materialGeneration;
        private static readonly HashSet<Material> PreparedMaterials = new HashSet<Material>();
        private static readonly HashSet<Material> PreparedLightDecals = new HashSet<Material>();
        private static readonly Dictionary<Material, bool> SavedLightDecals = new Dictionary<Material, bool>();
        private static bool _materialsReady, _materialsPreparing;
        public static void DisableExfils()
        {
            if (Singleton<GameWorld>.Instance == null)
                return;
            var controller = CommonAssets.Scripts.Game.ExfiltrationController.Instance;
            if (controller == null || controller.ExfiltrationPoints == null)
                return;
            BlackoutBridge.CaptureExfilStates(null);
            var exfils = new HashSet<string>()
            {
                "lab_Hangar_Gate",
                "lab_Parking_Gate",
            };
            foreach (var exfil in controller.ExfiltrationPoints)
            {
                if (exfil == null || exfil.Settings == null || string.IsNullOrEmpty(exfil.Settings.Name))
                    continue;
                if (!exfils.Contains(exfil.Settings.Name) && !LabsFinalLockdown.PreservesVent(exfil.Settings.Name))
                {
                    exfil.Status = EExfiltrationStatus.NotPresent;
                }
            }

            var sceneName = "Laboratory_Collector_Exit";
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                if (sceneRootGO.name == "TRANSITS")
                {
                    foreach (var transit in sceneRootGO.GetComponentsInChildren<TransitPoint>(true))
                    {
                        if (transit != null)
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
                    if (!SavedAmbient.Captured)
                    {
                        SavedAmbient.AmbientManager = sceneRootGO;
                        SavedAmbient.AmbientManagerActive = sceneRootGO.activeSelf;
                    }

                    sceneRootGO.SetActive(false);
                    continue;
                }

                if (sceneRootName == "Stencil")
                {
                    if (!SavedAmbient.Captured)
                    {
                        SavedAmbient.Stencil = sceneRootGO;
                        SavedAmbient.StencilActive = sceneRootGO.activeSelf;
                    }

                    sceneRootGO.SetActive(false);
                    continue;
                }
            }

            var tod = MonoBehaviourSingleton<TODSkySimple>.Instance;
            var levelSettings = Singleton<LevelSettings>.Instance;
            if (!SavedAmbient.Captured)
            {
                if (tod != null)
                    SavedAmbient.TodAmbientBrightness = tod.AmbientBrightness;
                if (levelSettings != null)
                {
                    SavedAmbient.SkyColor = levelSettings.SkyColor;
                    SavedAmbient.EquatorColor = levelSettings.EquatorColor;
                    SavedAmbient.GroundColor = levelSettings.GroundColor;
                    SavedAmbient.AmbientIntensity = levelSettings.AmbientIntensity;
                    SavedAmbient.NightVisionSkyColor = levelSettings.NightVisionSkyColor;
                    SavedAmbient.NightVisionEquatorColor = levelSettings.NightVisionEquatorColor;
                    SavedAmbient.NightVisionGroundColor = levelSettings.NightVisionGroundColor;
                    SavedAmbient.NightVisionAmbientIntensity = levelSettings.NightVisionAmbientIntensity;
                }

                SavedAmbient.Captured = true;
            }

            if (tod != null)
                tod.AmbientBrightness = AnimationCurve.Constant(0, 1, 0);
            if (levelSettings != null)
            {
                levelSettings.SkyColor = Color.black;
                levelSettings.EquatorColor = Color.black;
                levelSettings.GroundColor = Color.black;
                levelSettings.AmbientIntensity = 0;
                levelSettings.NightVisionSkyColor = new Color(0.030f, 0.037f, 0.042f, 1f);
                levelSettings.NightVisionEquatorColor = new Color(0.013f, 0.014f, 0.015f, 1f);
                levelSettings.NightVisionGroundColor = new Color(0.031f, 0.030f, 0.028f, 1f);
                levelSettings.NightVisionAmbientIntensity = 0;
            }
        }

        public static void RestoreAmbientLight()
        {
            if (!SavedAmbient.Captured)
                return;
            if (SavedAmbient.AmbientManager != null)
                SavedAmbient.AmbientManager.SetActive(SavedAmbient.AmbientManagerActive);
            if (SavedAmbient.Stencil != null)
                SavedAmbient.Stencil.SetActive(SavedAmbient.StencilActive);
            var tod = MonoBehaviourSingleton<TODSkySimple>.Instance;
            if (tod != null && SavedAmbient.TodAmbientBrightness != null)
                tod.AmbientBrightness = SavedAmbient.TodAmbientBrightness;
            var levelSettings = Singleton<LevelSettings>.Instance;
            if (levelSettings != null)
            {
                levelSettings.SkyColor = SavedAmbient.SkyColor;
                levelSettings.EquatorColor = SavedAmbient.EquatorColor;
                levelSettings.GroundColor = SavedAmbient.GroundColor;
                levelSettings.AmbientIntensity = SavedAmbient.AmbientIntensity;
                levelSettings.NightVisionSkyColor = SavedAmbient.NightVisionSkyColor;
                levelSettings.NightVisionEquatorColor = SavedAmbient.NightVisionEquatorColor;
                levelSettings.NightVisionGroundColor = SavedAmbient.NightVisionGroundColor;
                levelSettings.NightVisionAmbientIntensity = SavedAmbient.NightVisionAmbientIntensity;
                levelSettings.ApplySettings();
            }
        }

        private static void CaptureLights()
        {
            if (LightsCaptured)
                return;
            var cullingManager = CullingManager.Instance;
            if (cullingManager == null)
                return;
            var indices = cullingManager.GetField<CullingManager, List<int>>("_activeSpheresIndices");
            var objects = cullingManager.GetField<CullingManager, CullingManager.CullingObjectData[]>("_objectsData");
            var baseLightField = AccessTools.Field(typeof(CullingAdvancedLightObject), "_light");
            if (!LightsCaptured)
            {
                SavedSimpleLights.Clear();
                SavedAdvancedLights.Clear();
            }

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                ICullable cullingObject = objects[index].CullingObject;
                if (cullingObject is CullingAdvancedLightObject advancedLight)
                {
                    Behaviour baseLight = baseLightField.GetValue(advancedLight) as Behaviour;
                    if (!BlackoutTargets.IsFacilityComponent(baseLight))
                        continue;
                    if (!LightsCaptured)
                    {
                        SavedAdvancedLights.Add(new AdvancedLightState { Culling = advancedLight, Multiplier = 1f, Light = baseLight, Enabled = baseLight != null && baseLight.enabled });
                    }

                    continue;
                }

                if (cullingObject is CullingLightObject cullingLight)
                {
                    Light light = cullingLight.GetLight();
                    if (!BlackoutTargets.IsFacilityComponent(light))
                        continue;
                    if (!LightsCaptured)
                    {
                        SavedSimpleLights.Add(new SimpleLightState { Culling = cullingLight, Multiplier = cullingLight.IntensityMultiplier, Light = light, Enabled = light != null && light.enabled, Intensity = light != null ? light.intensity : 0f });
                    }
                }
            }

            LightsCaptured = true;
        }

        public static IEnumerator ApplyBlackoutCadence(Func<bool> canContinue)
        {
            int generation = _materialGeneration;
            yield return null;
            if (generation != _materialGeneration || !canContinue())
                yield break;
            DisableAutomaticLights();
            yield return null;
            if (generation != _materialGeneration || !canContinue())
                yield break;
            DisableAmbientLight();
            yield return null;
            if (generation != _materialGeneration || !canContinue())
                yield break;
            DisableElectricSounds();
            yield return null;
            if (generation != _materialGeneration || !canContinue())
                yield break;
            DisableLights();
            yield return null;
            if (generation != _materialGeneration || !canContinue())
                yield break;
            ApplyPreparedMaterials();
        }

        public static void DisableLights()
        {
            DisableLightStage(0, 1);
            ManimalLabsBlackoutCompatibility.SetBlackout(true);
        }

        private static void DisableLightStage(int stage, int stages)
        {
            CaptureLights();
            for (int i = stage; i < SavedSimpleLights.Count; i += stages)
            {
                var state = SavedSimpleLights[i];
                if (state.Culling != null)
                    state.Culling.IntensityMultiplier = 0f;
                if (state.Light != null)
                    state.Light.enabled = false;
            }

            for (int i = stage; i < SavedAdvancedLights.Count; i += stages)
            {
                var state = SavedAdvancedLights[i];
                if (state.Culling != null)
                    state.Culling.IntensityMultiplier = 0f;
                if (state.Light != null)
                    state.Light.enabled = false;
            }
        }

        public static void RestoreLights()
        {
            if (!LightsCaptured)
                return;
            foreach (SimpleLightState state in SavedSimpleLights)
            {
                if (state.Culling != null)
                    state.Culling.IntensityMultiplier = state.Multiplier;
                if (state.Light != null)
                {
                    state.Light.intensity = state.Intensity;
                    state.Light.enabled = state.Enabled;
                }
            }

            foreach (AdvancedLightState state in SavedAdvancedLights)
            {
                if (state.Culling != null)
                    state.Culling.IntensityMultiplier = state.Multiplier;
                if (state.Light != null)
                    state.Light.enabled = state.Enabled;
            }
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
                    if (lightsTriggers != null)
                    {
                        if (SavedAutomaticLights == null)
                        {
                            SavedAutomaticLights = new GameObjectState
                            {
                                Object = lightsTriggers.gameObject,
                                Active = lightsTriggers.gameObject.activeSelf
                            };
                        }

                        lightsTriggers.gameObject.SetActive(false);
                    }

                    break;
                }
            }
        }

        public static void RestoreAutomaticLights()
        {
            if (SavedAutomaticLights != null && SavedAutomaticLights.Object != null)
                SavedAutomaticLights.Object.SetActive(SavedAutomaticLights.Active);
        }

        public static void DisableElectricSounds()
        {
            var spatialAudioSystem = MonoBehaviourSingleton<SpatialAudioSystem>.Instance;
            if (spatialAudioSystem != null)
            {
                var audioRoomStorage = spatialAudioSystem.GetField<SpatialAudioSystem, AudioRoomStorage>("_audioRoomStorage");
                var allRooms = audioRoomStorage.GetField<AudioRoomStorage, List<SpatialAudioRoom>>("_validSpatialAudioRooms");
                var currentRooms = audioRoomStorage.GetField<AudioRoomStorage, List<ISpatialAudioRoom>>("list_3");
                if (!ElectricSoundsCaptured)
                    SavedRoomTones.Clear();
                foreach (var room in allRooms)
                {
                    object ambientData = room?.AmbientData;
                    if (ambientData == null)
                        continue;
                    Type type = ambientData.GetType();
                    PropertyInfo property = type.GetProperty("RoomTone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo field = property == null ? type.GetField("RoomTone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
                    MemberInfo member = (MemberInfo)property ?? field;
                    if (member == null)
                        continue;
                    object value = property != null ? property.GetValue(ambientData, null) : field.GetValue(ambientData);
                    if (!ElectricSoundsCaptured)
                    {
                        SavedRoomTones.Add(new RoomToneState { Target = ambientData, Member = member, Value = value });
                    }

                    if (property != null)
                        property.SetValue(ambientData, null, null);
                    else
                        field.SetValue(ambientData, null);
                }

                foreach (var room in currentRooms)
                    room?.StopRoomToneSound();
            }

            var sceneName = "Laboratory_Scripts";
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!ElectricSoundsCaptured)
                SavedElectricObjects.Clear();
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                if (sceneRootGO.name != "Laboratory_Sounds")
                    continue;
                var sceneRootTransform = sceneRootGO.transform;
                var childrenCount = sceneRootTransform.childCount;
                for (var i = 0; i < childrenCount; i++)
                {
                    var child = sceneRootTransform.GetChild(i);
                    var childName = child.name;
                    if (childName.Contains("server") || childName.Contains("generator") || childName.Contains("electric") || childName.Contains("airvent") || childName.Contains("turbine"))
                    {
                        if (!ElectricSoundsCaptured)
                        {
                            SavedElectricObjects.Add(new GameObjectState { Object = child.gameObject, Active = child.gameObject.activeSelf });
                        }

                        child.gameObject.SetActive(false);
                    }
                }

                break;
            }

            ElectricSoundsCaptured = true;
        }

        public static void RestoreElectricSounds()
        {
            if (!ElectricSoundsCaptured)
                return;
            foreach (RoomToneState state in SavedRoomTones)
            {
                if (state.Target == null || state.Member == null)
                    continue;
                PropertyInfo property = state.Member as PropertyInfo;
                FieldInfo field = state.Member as FieldInfo;
                if (property != null)
                    property.SetValue(state.Target, state.Value, null);
                else if (field != null)
                    field.SetValue(state.Target, state.Value);
            }

            foreach (GameObjectState state in SavedElectricObjects)
            {
                if (state.Object != null)
                    state.Object.SetActive(state.Active);
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
                    foreach (var(rootIndex, doorPaths)in roots)
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

        public static readonly int _EmissionPower = Shader.PropertyToID("_EmissionPower");
        public static readonly int _EmissionColor = Shader.PropertyToID("_EmissionColor");
        public static readonly int _SpecVals = Shader.PropertyToID("_SpecVals");
        public static IEnumerator PrepareMaterials()
        {
            int generation = _materialGeneration;
            while (_materialsPreparing && generation == _materialGeneration)
                yield return null;
            if (_materialsReady || generation != _materialGeneration)
                yield break;
            _materialsPreparing = true;
            var renderers = new List<Renderer>(8300);
            var materials = new List<Material>(20);
            int processed = 0;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || !scene.name.StartsWith("Laboratory_", StringComparison.Ordinal) || scene.name == "Laboratory_AI" || scene.name == "Laboratory_Sound" || scene.name == "Laboratory_Culling")
                        continue;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (root == null || root.name.StartsWith(TakeoverSirens.RootPrefix, StringComparison.Ordinal) || root.name.StartsWith("ISB_ContainmentBreachFire_", StringComparison.Ordinal))
                            continue;
                        root.GetComponentsInChildren(true, renderers);
                        foreach (var renderer in renderers)
                        {
                            if (generation != _materialGeneration)
                                yield break;
                            if (!BlackoutTargets.IsFacilityComponent(renderer))
                                continue;
                            renderer.GetSharedMaterials(materials);
                            foreach (var material in materials)
                            {
                                if (material == null || material.shader == null || (material.name.StartsWith(DecontaminationScreenRules.MaterialPrefix, StringComparison.Ordinal) || material.name.StartsWith("ISB_Prop_", StringComparison.Ordinal)))
                                    continue;
                                if (renderer.name.StartsWith("Emmisive_plane", StringComparison.OrdinalIgnoreCase) && material.name.Replace(" (Instance)", string.Empty) == "zapravka_light" && material.shader.name == "Decal/Ultra Deferred Decal Of God 3000")
                                    PreparedLightDecals.Add(material);
                                switch (material.shader.name)
                                {
                                    case "p0/Reflective/Bumped Specular SMap":
                                    case "p0/Reflective/Bumped Emissive Specular SMap":
                                    case "p0/Reflective/Bumped Animated Emissive Specular SMap":
                                    case "Standard":
                                    case "Decal/Ultra Deferred Decal Of God 3000":
                                        PreparedMaterials.Add(material);
                                        break;
                                }
                            }

                            materials.Clear();
                            if (++processed >= 256)
                            {
                                processed = 0;
                                yield return null;
                            }
                        }

                        renderers.Clear();
                    }
                }

                if (generation == _materialGeneration)
                    _materialsReady = true;
            }
            finally
            {
                if (generation == _materialGeneration)
                    _materialsPreparing = false;
            }
        }

        public static void ApplyPreparedMaterials(int stage = 0, int stages = 1)
        {
            int index = 0;
            foreach (var material in PreparedLightDecals)
            {
                if (index++ % stages != stage || material == null || SavedLightDecals.ContainsKey(material))
                    continue;
                SavedLightDecals.Add(material, material.GetShaderPassEnabled("DEFERRED"));
                material.SetShaderPassEnabled("DEFERRED", false);
            }

            index = 0;
            foreach (var material in PreparedMaterials)
            {
                if (index++ % stages != stage)
                    continue;
                if (material == null || material.shader == null || SavedMaterials.ContainsKey(material) || (material.name.StartsWith(DecontaminationScreenRules.MaterialPrefix, StringComparison.Ordinal) || material.name.StartsWith("ISB_Prop_", StringComparison.Ordinal)))
                    continue;
                var saved = new MaterialState
                {
                    HasSpec = material.HasProperty(_SpecVals),
                    HasEmissionPower = material.HasProperty(_EmissionPower),
                    HasEmissionColor = material.HasProperty(_EmissionColor)
                };
                if (saved.HasSpec)
                    saved.Spec = material.GetVector(_SpecVals);
                if (saved.HasEmissionPower)
                    saved.EmissionPower = material.GetFloat(_EmissionPower);
                if (saved.HasEmissionColor)
                    saved.EmissionColor = material.GetColor(_EmissionColor);
                SavedMaterials.Add(material, saved);
                switch (material.shader.name)
                {
                    case "p0/Reflective/Bumped Specular SMap":
                        if (saved.HasSpec)
                            material.SetVector(_SpecVals, saved.Spec * 0.4f);
                        break;
                    case "p0/Reflective/Bumped Emissive Specular SMap":
                    case "p0/Reflective/Bumped Animated Emissive Specular SMap":
                        if (saved.HasSpec)
                            material.SetVector(_SpecVals, saved.Spec * 0.4f);
                        if (saved.HasEmissionPower)
                            material.SetFloat(_EmissionPower, 0f);
                        break;
                    default:
                        if (saved.HasEmissionColor)
                            material.SetColor(_EmissionColor, Color.clear);
                        break;
                }
            }
        }

        private static void RestoreLightDecals()
        {
            foreach (var pair in SavedLightDecals)
                if (pair.Key != null)
                    pair.Key.SetShaderPassEnabled("DEFERRED", pair.Value);
            SavedLightDecals.Clear();
        }

        public static void RestoreMaterials()
        {
            RestoreLightDecals();
            foreach (KeyValuePair<Material, MaterialState> pair in SavedMaterials)
            {
                Material material = pair.Key;
                MaterialState state = pair.Value;
                if (material == null || state == null)
                    continue;
                if (state.HasSpec)
                    material.SetVector(_SpecVals, state.Spec);
                if (state.HasEmissionPower)
                    material.SetFloat(_EmissionPower, state.EmissionPower);
                if (state.HasEmissionColor)
                    material.SetColor(_EmissionColor, state.EmissionColor);
            }

            SavedMaterials.Clear();
        }

        public static void RestoreFacilityPower()
        {
            RestoreAutomaticLights();
            RestoreAmbientLight();
            RestoreElectricSounds();
            RestoreLights();
            RestoreMaterials();
            ManimalLabsBlackoutCompatibility.SetBlackout(false);
        }

        public static void ResetRestorationState()
        {
            _materialGeneration++;
            RestoreLightDecals();
            PreparedLightDecals.Clear();
            PreparedMaterials.Clear();
            _materialsReady = _materialsPreparing = false;
            ManimalLabsBlackoutCompatibility.Reset();
            SavedAmbient.Captured = false;
            SavedAmbient.AmbientManager = null;
            SavedAmbient.Stencil = null;
            SavedAmbient.TodAmbientBrightness = null;
            SavedSimpleLights.Clear();
            SavedAdvancedLights.Clear();
            SavedElectricObjects.Clear();
            SavedRoomTones.Clear();
            SavedMaterials.Clear();
            SavedAutomaticLights = null;
            LightsCaptured = false;
            ElectricSoundsCaptured = false;
        }
#if DEBUG
        public static Dictionary<string, Dictionary<int, List<List<int>>>> RecordAutomaticDoors(string outputPath, ManualLogSource logger)
        {
            var triggersList = new List<TriggerZone>(10);
            var namesList = new List<string>(15);
            var result = new Dictionary<string, Dictionary<int, List<List<int>>>>();
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
