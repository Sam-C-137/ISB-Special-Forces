using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using MoreBotsAPI.Components;
using ISBSpecialForces.Components;
using ISBSpecialForces.Patches;
using ISBSpecialForces.Behavior.BlackDivision;

namespace ISBSpecialForces
{
    [BepInDependency("com.morebotsapi.tacticaltoaster", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ISBPitFireTeamCompatibility.PitFireTeamGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.blackdiv.tacticaltoaster", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.PluginName, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        private const int ISBCommanderEnum = 13707;
        private const int ISBFollowerCommanderEnum = 13708;
        private static readonly List<(string Guid, string DisplayName, List<int> TargetEnums)> ColabHuntTargets = new List<(string, string, List<int>)>
        {
            ("com.blackdiv.tacticaltoaster", "Black Division", new List<int> { 848420, 848421, 848422, 848423, 848424, 848426 }),
        };
        private void EnableAishiLoyaltyConditionPatches()
        {
            try
            {
                new ISBAishiRaidPlayerReadyPatch().Enable();
                new ISBAishiLoyaltyConditionAddEnemyPatch().Enable();
                new ISBAishiLoyaltyConditionPlayerEnemyPatch().Enable();
                new ISBAishiLoyaltyConditionMarkOfUnknownPatch().Enable();
                new ISBAishiLoyaltyConditionDamagePatch().Enable();
                new ISBAishiLoyaltyConditionKillPatch().Enable();
            }
            catch (System.Exception ex)
            {
                LogSource.LogError($"[ISB SOF] Aishi loyalty condition patches failed to load: {ex.Message}. The loyalty condition system is disabled.");
                Components.ISBAishiLoyaltyConditionState.HardDisable();
            }
        }

        private void EnablePitFireTeamCompatibility()
        {
            if (!ISBPitFireTeamCompatibility.Initialize())
                return;
            this.GetOrAddComponent<ISBPitFireTeamCompatibility>();
        }

        private void EnableAishiRaidResultPatch()
        {
            try
            {
                ISBAishiRaidResultHarmonyPatcher.Enable();
            }
            catch (System.Exception ex)
            {
                LogSource.LogError($"[ISB AISHI LOYALTY] Universal post-raid patch failed to load: {ex}");
            }
        }

        private static BepInEx.Configuration.ConfigEntry<bool> _diagnosticLogs;
        private static BepInEx.Configuration.ConfigEntry<bool> _incrementalMemoryBudget;
        internal static bool IncrementalMemoryBudgetEnabled => _incrementalMemoryBudget?.Value ?? false;
        public static bool DiagnosticsEnabled => _diagnosticLogs?.Value ?? false;

        private void Awake()
        {
            LogSource = Logger;
            _diagnosticLogs = Config.Bind("Logging", "Diagnostic Logs", false, "Enable informational and debug diagnostics for this plugin. Warnings and errors remain enabled.");
            ISBBotTypeRegistration.Initialize();
            _incrementalMemoryBudget = Config.Bind("Performance", "Incremental Memory Budget", false, "Experimental memory control. Requests 0.5 ms incremental GC slices after 768 MB growth above the raid baseline, but native GC can exceed this budget and cause frame spikes. Disabled by default. Each process manages its own memory; enabled or externally manual GC is preserved.");
            this.GetOrAddComponent<ISBIncrementalMemoryGuard>();
            GhostBotSpawnBridge.Initialize();
            new ISBHeadlessDisplayPatch().Enable();
            new ISBOpticLifecyclePatch().Enable();
            new ISBOpticCameraInitPatch().Enable();
            new ISBBotWeaponCallbackPatch().Enable();
            new ISBBotFirstAidCallbackPatch().Enable();
            new ISBBotDoorCallbackPatch().Enable();
            new ISBOpticEnablePatch().Enable();
            new ISBOpticDisablePatch().Enable();
            new ISBOpticReticleTransformPatch().Enable();
            ISBLocale.Initialize();
            new ISBSpawnProgressStartPatch().Enable();
            new ISBAmnestyDescriptionPatch().Enable();
            this.GetOrAddComponent<ISBSpawnProgressBridge>();
            this.GetOrAddComponent<ISBEventPopulationQueue>();
            this.GetOrAddComponent<ISBRevengeHuntController>();
            new ISBNightVisionHelmetPatch().Enable();
            new ISBArenaAchievementIconPatch().Enable();
            this.GetOrAddComponent<ISBCheckpointManager>();
            this.GetOrAddComponent<ShorelineItemSecretManager>();
            this.GetOrAddComponent<ISBLocationSecretManager>();
            this.GetOrAddComponent<ISBBlackDivClashManager>();
            this.GetOrAddComponent<ISBBlackDivRigSecretManager>();
            this.GetOrAddComponent<ISBTacticalController>();
            this.GetOrAddComponent<ISBCameraHuntController>();
            this.GetOrAddComponent<BlackDivisionEventHunt>();
            this.GetOrAddComponent<ISBAishiRaidResultUiFallback>();
            this.GetOrAddComponent<ISBAishiReputationWorker>();
            this.GetOrAddComponent<ISBAishiParticipantBridge>();
            new TarkovInitPatch().Enable();
            new ISBDogtagSidePatch().Enable();
            new ISBDogtagBarterPatch().Enable();
            try
            {
                new ISBDogtagRequirementLabelPatch().Enable();
            }
            catch (System.Exception ex)
            {
                Logger.LogError("ISB dogtag requirement label patch failed: " + ex);
            }

            new ISBDogtagDeathPatch().Enable();
            new BotOwnerActivatePatch().Enable();
            new ISBBotWeightPatch().Enable();
            new ISBBotWeightBridgePatch().Enable();
            new ISBScavFenceStandingPatch().Enable();
            new BotsControllerInitPatch().Enable();
            new GoonsRoguesSecretPatch().Enable();
            new ISBKillCountSecretPatch().Enable();
            new CommanderTeleportPatch().Enable();
            new ISBForcedHostilityAddEnemyPatch().Enable();
            new ISBCameraHuntFirstTargetPatch().Enable();
            new ISBCameraHuntNewTargetPatch().Enable();
            new ISBCameraHuntFollowersPatch().Enable();
            new ISBCameraHuntRegroupPointPatch().Enable();
            EnableAishiLoyaltyConditionPatches();
            EnablePitFireTeamCompatibility();
            EnableAishiRaidResultPatch();
            if (Chainloader.PluginInfos.ContainsKey("me.sol.sain"))
            {
                this.GetOrAddComponent<ISBSAINPresetMigration>();
                if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                {
                    LogSource.LogInfo("[ISB SOF] SAIN combat integration loaded.");
                }
            }

            var isbEnums = new List<int>
            {
                13701,
                13702,
                13703,
                13704,
                13705,
                13706,
                13709,
                13710,
                13711,
                13712,
                13713,
                13714,
                13715,
                13716
            }.ConvertAll(x => (WildSpawnType)x);
            var huntTargets = new List<WildSpawnType>
            {
                WildSpawnType.pmcUSEC,
                WildSpawnType.pmcBEAR
            };
            foreach (var colab in ColabHuntTargets)
            {
                if (!Chainloader.PluginInfos.ContainsKey(colab.Guid))
                    continue;
                var colabEnums = colab.TargetEnums.ConvertAll(x => (WildSpawnType)x);
                huntTargets.AddRange(colabEnums);
                if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                {
                    LogSource.LogInfo("[ISB SOF] Hunt integration loaded: " + colab.DisplayName + ".");
                }
            }

            MonoBehaviourSingleton<HuntManager>.Instance.AddHuntRoles(isbEnums, huntTargets);
            MonoBehaviourSingleton<HuntManager>.Instance.AddHuntSides(isbEnums, new List<EPlayerSide> { EPlayerSide.Usec, EPlayerSide.Bear, });
            LogSource.LogInfo(PluginInfo.PluginName + " v" + PluginInfo.Version + " loaded.");
        }
    }
}
