using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;
using UnityEngine;
using Newtonsoft.Json;
using SPT.Common.Http;
using ISBSpecialForces.Models;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBBrainBootstrap
    {
        private static bool _ready;
        private static bool _installed;
        private static bool _enabled;
        private static BrainDebugConfig _settings = new BrainDebugConfig();
        internal static bool Enabled => _ready;
        internal static bool IsbEnabled => _ready && _settings.enableISBCustomBehavior;
        internal static bool BlackDivEnabled => _ready && _settings.enableBlackDivCustomBehavior;
        internal static bool BlackDivAllMaps => _settings.changeBlackDivBehaviorAllMaps;
        internal static void Initialize(bool enabled)
        {
            _enabled = enabled;
            ISBRuntimeBindings.IsAuthority = () => ISBFikaAuthority.IsAiAuthority;
            ISBRuntimeBindings.IsEligible = bot => _ready && bot?.Profile?.Info?.Settings != null &&
                (IsbEnabled && WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role) || ISBBrainSelection.IsEventBot(bot));
            ISBRuntimeBindings.DefensivePoint = bot => bot.GetManager()?.GetAssignedCheckpoint()?.Position;
            ISBRuntimeBindings.CriticalFailure = error => Plugin.LogSource.LogError("[ISB brain] " + error);
            try
            {
                ISBBrainPatches.Install();
                _installed = true;
            }
            catch (Exception exception)
            {
                Plugin.LogSource.LogError("[ISB brain] Activation patches could not be installed: " + exception);
            }
        }

        internal static bool ConfigureSain()
        {
            _ready = false;
            if (!_installed || !_enabled)
                return false;
            try
            {
                var configuration = JsonConvert.DeserializeObject<MainConfig>(RequestHandler.GetJson("/ISB/checkpoints"));
                _settings = configuration?.debug ?? new BrainDebugConfig();
                if (Chainloader.PluginInfos.TryGetValue("me.sol.sain", out var plugin) && plugin?.Instance != null)
                {
                    var type = plugin.Instance.GetType().Assembly.GetType("SAIN.Components.BotController.BotSpawnController", true);
                    var property = type.GetProperty("StrictExclusionList", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var field = type.GetField("StrictExclusionList", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var exclusions = (property?.GetValue(null) ?? field?.GetValue(null)) as IList<WildSpawnType>;
                    if (exclusions == null)
                        throw new InvalidOperationException("SAIN exclusion list is unavailable.");
                    if (exclusions.IsReadOnly)
                    {
                        if (property?.GetSetMethod(true) == null)
                            throw new InvalidOperationException("SAIN exclusion list cannot be updated.");
                        exclusions = new List<WildSpawnType>(exclusions);
                        property.SetValue(null, exclusions);
                    }
                    foreach (int role in WildSpawnTypeExtensions.ISBTypeEnums)
                        if (_settings.enableISBCustomBehavior && !exclusions.Contains((WildSpawnType)role))
                            exclusions.Add((WildSpawnType)role);
                }
                ISBSainSquads.Configure(plugin?.Instance?.GetType().Assembly, _settings.useSainLayersChance);
                try { ISBSainBridge.Install(); }
                catch (Exception exception)
                {
                    Plugin.LogSource.LogError("[ISB brain] SAIN attributes are unavailable; native settings remain active: " + exception);
                }
                _ready = true;
                return _settings.enableISBCustomBehavior;
            }
            catch (Exception exception)
            {
                Plugin.LogSource.LogError("[ISB brain] Native brain could not be enabled: " + exception);
                return false;
            }
        }
    }
}
