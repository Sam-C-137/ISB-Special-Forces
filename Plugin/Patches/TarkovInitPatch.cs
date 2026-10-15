using BepInEx.Bootstrap;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InputSystem;
using ISBSpecialForces.Behavior.Layers;
using ISBSpecialForces.Behavior.BlackDivision;
using MoreBotsAPI.Behavior.Layers;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal partial class TarkovInitPatch : ModulePatch
    {
        private static readonly int[] ISBRoleIds =
        {
            13701,
            13702,
            13703,
            13704,
            13705,
            13706,
            13707,
            13708,
            13709,
            13710,
            13711,
            13712,
            13713,
            13714,
            13715,
            13716
        };
        private static readonly string[] SainVanillaCombatLayers =
        {
            "Help",
            "AdvAssaultTarget",
            "Hit",
            "Simple Target",
            "Pmc",
            "AssaultHaveEnemy",
            "Assault Building",
            "Enemy Building",
            "PushAndSup",
            "Pursuit",
            "Request",
            "KnightFight",
            "PmcBear",
            "PmcUsec"
        };
        private static bool _sainLayersApplied;
        protected override MethodBase GetTargetMethod()
        {
            return typeof(TarkovApplication).GetMethod(nameof(TarkovApplication.Init), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix()
        {
            ApplySainLayers();
            BrainManager.AddCustomLayer(typeof(ISBSpecialForces.Behavior.DeadManSwitchPanic), new List<string> { "ArenaFighter", "Assault", "BigPipe", "BirdEye", "BoarSniper", "BossBoar", "BossBully", "BossGluhar", "BossKojaniy", "BossKolontay", "BossPartisan", "BossSanitar", "BossTest", "BossZryachiy", "CursAssault", "ExUsec", "FlKlnAslt", "Fl_Zraychiy", "FollowerBully", "FollowerGluharAssault", "FollowerGluharProtect", "FollowerGluharScout", "FollowerKojaniy", "FollowerSanitar", "Gifter", "HelperAgro", "InfectedAssault", "InfectedFast", "InfectedShooting", "InfectedSlow", "Killa", "KillaAgro", "Knight", "KolonSec", "Marksman", "Obdolbs", "Oni", "PMC", "PeaceZryachiy", "PmcBear", "PmcUsec", "PrizrakSt", "Prst event", "RavangeZryachiy", "SctPredvst", "SectantPriest", "SectantWarrior", "Tagilla", "TagillaAgro", "TagillaFollower", "Zombie[?]", "PmcUSEC", "PmcBEAR" }, 75);
            var brainList = new List<string>()
            {
                "PMC",
                "ExUsec",
                "Assault",
                "PmcUsec",
                "PmcBear",
                "PmcUSEC",
                "PmcBEAR"
            };
            var typesList = GetISBRoles();
            var soloPriority = 20;
            var squadPriority = 22;
            if (Chainloader.PluginInfos.TryGetValue("me.sol.sain", out var sain) && sain?.Instance != null)
            {
                var assembly = sain.Instance.GetType().Assembly;
                soloPriority = ReadSainPriority(assembly, "SAINCombatSoloLayerPriority", 20);
                squadPriority = ReadSainPriority(assembly, "SAINCombatSquadLayerPriority", 22);
            }

            var tacticalPriority = GetTacticalPriority(soloPriority, squadPriority);
            BrainManager.AddCustomLayer(typeof(ISBTacticalLayer), brainList, tacticalPriority + 2, typesList);
            BrainManager.AddCustomLayer(typeof(GoToCheckpointLayer), brainList, tacticalPriority + 1, typesList);
            if (Components.BlackDivisionEventCheckpoints.Installed)
            {
                var blackDivisionRoles = new List<int>(ISB.Compatibility.BlackDivisionCheckpointPolicy.Roles).ConvertAll(role => (WildSpawnType)role);
                BrainManager.AddCustomLayer(typeof(GoToCheckpointLayer), brainList, tacticalPriority + 1, blackDivisionRoles);
            }

            BrainManager.AddCustomLayer(typeof(ISBHuntLayer), brainList, tacticalPriority, typesList);
            BrainManager.AddCustomLayer(typeof(RevengeHunt), brainList, 74, typesList);
            BrainManager.RemoveLayers(["AdvAssaultTarget"], brainList, typesList);
        }

        internal static int GetTacticalPriority(int solo, int squad) => Math.Min(77, Math.Max(solo, squad) + 1);
        internal static void ApplySainLayers()
        {
            if (_sainLayersApplied || !Chainloader.PluginInfos.TryGetValue("me.sol.sain", out var sainInfo) || sainInfo?.Instance == null)
                return;
            try
            {
                var sainAssembly = sainInfo.Instance.GetType().Assembly;
                var roles = GetISBRoles();
                try
                {
                    TryRegisterThroughSain(sainAssembly, roles);
                }
                catch (Exception)
                {
                }

                if (HasRequiredSainLayers(sainAssembly, roles))
                {
                    _sainLayersApplied = true;
                    return;
                }

                if (TryRegisterSainLayersDirectly(sainAssembly, roles) && HasRequiredSainLayers(sainAssembly, roles))
                {
                    _sainLayersApplied = true;
                    return;
                }

                Plugin.LogSource.LogError("[ISB SOF] SAIN was detected, but its PMC combat layer types could not be registered for ISB roles.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ISB SOF] SAIN PMC compatibility registration failed: {ex.GetBaseException().Message}");
            }
        }

        private static bool TryRegisterThroughSain(Assembly sainAssembly, List<WildSpawnType> roles)
        {
            var handlerType = FindType(sainAssembly, "BigBrainHandler");
            var assignmentType = handlerType?.GetNestedType("BrainAssignment", BindingFlags.Public | BindingFlags.NonPublic);
            if (assignmentType == null)
                return false;
            var pmcBrain = new List<string>()
            {
                "PMC"
            };
            var methods = assignmentType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "AddCustomLayersToBrainsAndRoles", StringComparison.Ordinal))
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length != 3 || !parameters[0].ParameterType.IsAssignableFrom(typeof(List<string>)) || !parameters[1].ParameterType.IsAssignableFrom(typeof(List<WildSpawnType>)) || parameters[2].ParameterType != typeof(bool))
                    continue;
                method.Invoke(null, new object[] { pmcBrain, roles, false });
                foreach (var toggleMethod in methods)
                {
                    if (!string.Equals(toggleMethod.Name, "ToggleVanillaLayersForBrainsAndRoles", StringComparison.Ordinal))
                        continue;
                    var toggleParameters = toggleMethod.GetParameters();
                    if (toggleParameters.Length == 4 && toggleParameters[0].ParameterType.IsAssignableFrom(typeof(List<string>)) && toggleParameters[1].ParameterType.IsAssignableFrom(typeof(List<WildSpawnType>)) && toggleParameters[2].ParameterType.IsAssignableFrom(typeof(List<string>)) && toggleParameters[3].ParameterType == typeof(bool))
                    {
                        toggleMethod.Invoke(null, new object[] { pmcBrain, roles, new List<string>(SainVanillaCombatLayers), false });
                        break;
                    }
                }

                return true;
            }

            MethodInfo addRaidersMethod = null;
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "addCustomLayersToRaiders", StringComparison.OrdinalIgnoreCase))
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(typeof(List<WildSpawnType>)))
                {
                    addRaidersMethod = method;
                    break;
                }
            }

            if (addRaidersMethod == null)
                return false;
            addRaidersMethod.Invoke(null, new object[] { roles });
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "ToggleVanillaLayersForRaiders", StringComparison.OrdinalIgnoreCase))
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType.IsAssignableFrom(typeof(List<WildSpawnType>)) && parameters[1].ParameterType == typeof(bool))
                {
                    method.Invoke(null, new object[] { roles, false });
                    break;
                }
            }

            return true;
        }

        private static bool TryRegisterSainLayersDirectly(Assembly sainAssembly, List<WildSpawnType> roles)
        {
            var pmcBrain = new List<string>()
            {
                "PMC"
            };
            var debugLayer = FindType(sainAssembly, "DebugLayer");
            var avoidThreatLayer = FindType(sainAssembly, "SAINAvoidThreatLayer");
            var extractLayer = FindType(sainAssembly, "ExtractLayer");
            var combatSquadLayer = FindType(sainAssembly, "CombatSquadLayer");
            var combatSoloLayer = FindType(sainAssembly, "CombatSoloLayer");
            if (combatSquadLayer == null || combatSoloLayer == null)
                return false;
            var extractPriority = ReadSainPriority(sainAssembly, "SAINExtractLayerPriority", 24);
            var squadPriority = ReadSainPriority(sainAssembly, "SAINCombatSquadLayerPriority", 22);
            var soloPriority = ReadSainPriority(sainAssembly, "SAINCombatSoloLayerPriority", 20);
            RegisterLayerForMissingRoles(debugLayer, pmcBrain, 99, roles);
            RegisterLayerForMissingRoles(avoidThreatLayer, pmcBrain, 80, roles);
            RegisterLayerForMissingRoles(extractLayer, pmcBrain, extractPriority, roles);
            RegisterLayerForMissingRoles(combatSquadLayer, pmcBrain, squadPriority, roles);
            RegisterLayerForMissingRoles(combatSoloLayer, pmcBrain, soloPriority, roles);
            BrainManager.RemoveLayers(new List<string>(SainVanillaCombatLayers), pmcBrain, roles);
            return true;
        }

        private static void RegisterLayerForMissingRoles(Type layerType, List<string> brainNames, int priority, List<WildSpawnType> roles)
        {
            if (layerType == null)
                return;
            var missingRoles = new List<WildSpawnType>();
            foreach (var role in roles)
            {
                if (!HasLayerRegistration(layerType, "PMC", role))
                    missingRoles.Add(role);
            }

            if (missingRoles.Count > 0)
                BrainManager.AddCustomLayer(layerType, brainNames, priority, missingRoles);
        }

        private static bool HasRequiredSainLayers(Assembly sainAssembly, List<WildSpawnType> roles)
        {
            var combatSquadLayer = FindType(sainAssembly, "CombatSquadLayer");
            var combatSoloLayer = FindType(sainAssembly, "CombatSoloLayer");
            if (combatSquadLayer == null || combatSoloLayer == null)
                return false;
            foreach (var role in roles)
            {
                if (!HasLayerRegistration(combatSquadLayer, "PMC", role) || !HasLayerRegistration(combatSoloLayer, "PMC", role))
                    return false;
            }

            return true;
        }

        private static bool HasLayerRegistration(Type layerType, string brainName, WildSpawnType role)
        {
            foreach (var pair in BrainManager.CustomLayersReadOnly)
            {
                var info = pair.Value;
                if (info.customLayerType != layerType)
                    continue;
                var hasBrain = false;
                foreach (var brain in info.CustomLayerBrains)
                {
                    if (string.Equals(brain, brainName, StringComparison.Ordinal))
                    {
                        hasBrain = true;
                        break;
                    }
                }

                if (!hasBrain)
                    continue;
                foreach (var registeredRole in info.CustomLayerRoles)
                {
                    if (registeredRole.Equals(role))
                        return true;
                }
            }

            return false;
        }

        private static int ReadSainPriority(Assembly sainAssembly, string memberName, int fallback)
        {
            try
            {
                var pluginType = FindType(sainAssembly, "SAINPlugin");
                var preset = GetStaticMember(pluginType, "LoadedPreset");
                var globalSettings = GetMember(preset, "GlobalSettings");
                var general = GetMember(globalSettings, "General");
                var layers = GetMember(general, "Layers");
                var value = GetMember(layers, memberName);
                if (value != null)
                    return Convert.ToInt32(value);
            }
            catch
            {
            }

            return fallback;
        }

        private static object GetStaticMember(Type type, string name)
        {
            if (type == null)
                return null;
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property != null)
                return property.GetValue(null);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null);
        }

        private static object GetMember(object instance, string name)
        {
            if (instance == null)
                return null;
            var type = instance.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(instance);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(instance);
        }

        private static Type FindType(Assembly assembly, string typeName)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                        return type;
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (var type in ex.Types)
                {
                    if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                        return type;
                }
            }

            return null;
        }

        private static List<WildSpawnType> GetISBRoles()
        {
            var roles = new List<WildSpawnType>(ISBRoleIds.Length);
            foreach (var roleId in ISBRoleIds)
                roles.Add((WildSpawnType)roleId);
            return roles;
        }
    }
}
