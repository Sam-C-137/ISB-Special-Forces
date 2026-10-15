using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISB.Compatibility;

namespace ISBSpecialForces.Patches
{
    internal partial class TarkovInitPatch
    {
        private sealed class EventSainLayer
        {
            internal readonly List<WildSpawnType> ActiveRoles = new List<WildSpawnType>();
            internal List<WildSpawnType> Roles;
        }

        private sealed class EventSainExclusion
        {
            internal string Layer, Brain;
            internal WildSpawnType Role;
        }

        private static readonly List<EventSainLayer> EventSainLayers = new List<EventSainLayer>();
        private static readonly List<EventSainExclusion> EventSainExclusions = new List<EventSainExclusion>();
        private static readonly string[] EventSainBrains =
        {
            "PMC",
            "ExUsec",
            "Assault",
            "PmcUsec",
            "PmcBear",
            "PmcUSEC",
            "PmcBEAR"
        };
        private static bool _eventSainRegistered;
        private static IList<WildSpawnType> _sainStrictExclusions;
        private static PropertyInfo _strictProperty;
        private static object _originalStrictList;
        private static readonly List<WildSpawnType> SavedStrictExclusions = new List<WildSpawnType>();
        private static readonly List<EventSainExclusion> RestoredEventSainLayers = new List<EventSainExclusion>();
        internal static void UpdateBlackDivisionEventSain(bool active)
        {
            foreach (var layer in EventSainLayers)
                layer.ActiveRoles.Clear();
            foreach (var exclusion in EventSainExclusions)
                BrainManager.RestoreLayer(exclusion.Layer, new List<string> { exclusion.Brain }, new List<WildSpawnType> { exclusion.Role });
            EventSainExclusions.Clear();
            foreach (var exclusion in RestoredEventSainLayers)
                BrainManager.RemoveLayer(exclusion.Layer, new List<string> { exclusion.Brain }, new List<WildSpawnType> { exclusion.Role });
            RestoredEventSainLayers.Clear();
            if (_sainStrictExclusions != null)
                foreach (var role in SavedStrictExclusions)
                    if (!_sainStrictExclusions.Contains(role))
                        _sainStrictExclusions.Add(role);
            if (_strictProperty != null && ReferenceEquals(_strictProperty.GetValue(null), _sainStrictExclusions))
                _strictProperty.SetValue(null, _originalStrictList);
            _strictProperty = null;
            _originalStrictList = null;
            SavedStrictExclusions.Clear();
            _sainStrictExclusions = null;
            if (!active || !Components.BlackDivisionEventCheckpoints.Installed || !Chainloader.PluginInfos.TryGetValue("me.sol.sain", out var sain) || sain?.Instance == null)
                return;
            try
            {
                var assembly = sain.Instance.GetType().Assembly;
                var spawnController = assembly.GetType("SAIN.Components.BotController.BotSpawnController");
                _sainStrictExclusions = GetStaticMember(spawnController, "StrictExclusionList") as IList<WildSpawnType>;
                if (_sainStrictExclusions == null)
                    throw new InvalidOperationException("SAIN strict exclusion list is unavailable.");
                if (_sainStrictExclusions.IsReadOnly)
                {
                    _strictProperty = spawnController.GetProperty("StrictExclusionList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (_strictProperty?.GetSetMethod(true) == null)
                        throw new InvalidOperationException("SAIN strict exclusion list cannot be scoped to the event.");
                    _originalStrictList = _sainStrictExclusions;
                    _sainStrictExclusions = new List<WildSpawnType>(_sainStrictExclusions);
                    _strictProperty.SetValue(null, _sainStrictExclusions);
                }

                foreach (int id in BlackDivisionCheckpointPolicy.Roles)
                {
                    var role = (WildSpawnType)id;
                    if (!_sainStrictExclusions.Contains(role))
                        continue;
                    SavedStrictExclusions.Add(role);
                    _sainStrictExclusions.Remove(role);
                }

                if (!_eventSainRegistered)
                {
                    var solo = FindType(assembly, "CombatSoloLayer");
                    var squad = FindType(assembly, "CombatSquadLayer");
                    if (solo == null || squad == null)
                        throw new InvalidOperationException("SAIN combat layers missing.");
                    foreach (var brain in EventSainBrains.Distinct(StringComparer.Ordinal))
                    {
                        foreach (var type in new[]
                        {
                            FindType(assembly, "SAINAvoidThreatLayer"),
                            squad,
                            solo
                        }

                        )
                        {
                            if (type == null)
                                continue;
                            var roles = new List<WildSpawnType>();
                            foreach (int id in BlackDivisionCheckpointPolicy.Roles)
                                if (!HasLayerRegistration(type, brain, (WildSpawnType)id))
                                    roles.Add((WildSpawnType)id);
                            if (roles.Count == 0)
                                continue;
                            var registration = new EventSainLayer
                            {
                                Roles = roles
                            };
                            int priority = type == solo ? ReadSainPriority(assembly, "SAINCombatSoloLayerPriority", 20) : type == squad ? ReadSainPriority(assembly, "SAINCombatSquadLayerPriority", 22) : 80;
                            BrainManager.AddCustomLayer(type, new List<string> { brain }, priority, registration.ActiveRoles);
                            EventSainLayers.Add(registration);
                        }
                    }

                    _eventSainRegistered = true;
                }

                foreach (var layer in EventSainLayers)
                    layer.ActiveRoles.AddRange(layer.Roles);
                if (GetStaticMember(FindType(assembly, "BigBrainHandler"), "SAINLayerNames")is IEnumerable<string> sainNames)
                    foreach (var name in sainNames)
                        foreach (var brain in EventSainBrains)
                            foreach (int id in BlackDivisionCheckpointPolicy.Roles)
                            {
                                var role = (WildSpawnType)id;
                                if (!BrainManager.ExcludeLayersReadOnly.Any(e => e.excludeLayerName == name && e.ExcludeLayerBrains.Contains(brain) && e.ExcludeLayerRoles.Contains(role)))
                                    continue;
                                RestoredEventSainLayers.Add(new EventSainExclusion { Layer = name, Brain = brain, Role = role });
                                BrainManager.RestoreLayer(name, new List<string> { brain }, new List<WildSpawnType> { role });
                            }

                foreach (var brain in EventSainBrains.Distinct(StringComparer.Ordinal))
                    foreach (string name in SainVanillaCombatLayers)
                        foreach (int id in BlackDivisionCheckpointPolicy.Roles)
                        {
                            var role = (WildSpawnType)id;
                            bool excluded = BrainManager.ExcludeLayersReadOnly.Any(e => e.excludeLayerName == name && e.ExcludeLayerBrains.Contains(brain) && e.ExcludeLayerRoles.Contains(role));
                            if (excluded)
                                continue;
                            EventSainExclusions.Add(new EventSainExclusion { Layer = name, Brain = brain, Role = role });
                            BrainManager.RemoveLayer(name, new List<string> { brain }, new List<WildSpawnType> { role });
                        }
            }
            catch (Exception ex)
            {
                UpdateBlackDivisionEventSain(false);
                Plugin.LogSource.LogError("[ISB] Containment Black Division SAIN setup failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
