using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;

namespace ISBSpecialForces.Patches
{
    internal static class ISBIncendiaryCompatibility
    {
        internal const string PluginGuid = "com.sage.incendiarygrenade";
        private static bool _registered;
        private static Type _escapeLayer;
        private static FieldInfo _wrappedLayer;
        private static HashSet<WildSpawnType> _roles;

        internal static void Initialize(ManualLogSource log)
        {
            if (_registered || !Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
                return;
            try
            {
                var layer = plugin.Instance.GetType().Assembly.GetType("IncendiaryGrenade.Bots.EscapeFireLayer");
                if (layer == null || !typeof(CustomLayer).IsAssignableFrom(layer) ||
                    layer.GetConstructor(new[] { typeof(BotOwner), typeof(int) }) == null)
                    throw new InvalidOperationException("The installed Incendiary Grenade escape layer is incompatible.");
                var existing = BrainManager.CustomLayersReadOnly.Values.Where(info => info.customLayerType == layer).ToArray();
                if (existing.Length == 0)
                    throw new InvalidOperationException("Incendiary Grenade has not registered its escape layer.");
                var roles = WildSpawnTypeExtensions.ISBTypeEnums
                    .Concat(new[] { 848420, 848421, 848422, 848423, 848424, 848426, ISB.Compatibility.GhostBotVariant.ChaosValue })
                    .Select(value => (WildSpawnType)value).Distinct().ToArray();
                foreach (var brain in new[] { "ISB", "blackDiv" })
                {
                    var missing = roles.Where(role => !existing.Any(info =>
                        info.CustomLayerBrains.Contains(brain) && info.CustomLayerRoles.Contains(role))).ToList();
                    if (missing.Count > 0)
                        BrainManager.AddCustomLayer(layer, new List<string> { brain }, existing[0].customLayerPriority, missing);
                }
                _registered = true;
                PreserveFireEscape(layer, roles);
            }
            catch (Exception error)
            {
                log?.LogWarning("ISB Incendiary Grenade compatibility: " + error.GetBaseException().Message);
            }
        }

        private static void PreserveFireEscape(Type layer, WildSpawnType[] roles)
        {
            var interrupt = AccessTools.TypeByName("pitTeam.Patches.HostilePeacefulLayerInterrupt");
            var classify = interrupt == null ? null : AccessTools.Method(interrupt, "IsCombatLayer");
            if (classify == null)
                return;
            var wrapper = typeof(BrainManager).Assembly.GetType("DrakiaXYZ.BigBrain.Internal.CustomLayerWrapper");
            _wrappedLayer = wrapper == null ? null : AccessTools.Field(wrapper, "customLayer");
            if (_wrappedLayer == null || classify.ReturnType != typeof(bool))
                throw new InvalidOperationException("The PIT Fireteam layer classifier is incompatible.");
            _escapeLayer = layer;
            _roles = new HashSet<WildSpawnType>(roles);
            new Harmony("com.isb.incendiarycompatibility").Patch(classify,
                postfix: new HarmonyMethod(typeof(ISBIncendiaryCompatibility), nameof(ClassifyFireEscape)));
        }

        private static void ClassifyFireEscape(object __0, ref bool __result)
        {
            if (__result || __0 == null || !_wrappedLayer.DeclaringType.IsInstanceOfType(__0))
                return;
            var layer = _wrappedLayer.GetValue(__0) as CustomLayer;
            if (layer?.GetType() == _escapeLayer && layer.BotOwner?.Profile?.Info?.Settings != null &&
                _roles.Contains(layer.BotOwner.Profile.Info.Settings.Role))
                __result = true;
        }
    }
}
