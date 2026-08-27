using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using MoreBotsAPI.Components;
using ISBSpecialForces.Components;
using ISBSpecialForces.Patches;
using ISBSpecialForces.Behavior.Orders.Controllers;

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

        private static readonly List<(string Guid, string DisplayName, List<int> TargetEnums, bool PriorityTargets)> ColabHuntTargets = new List<(string, string, List<int>, bool)>
        {
            ("com.blackdiv.tacticaltoaster", "Black Division", new List<int> { 848420, 848421, 848422, 848423 }, true),
        };

        private void EnableAishiLoyaltyConditionPatches()
        {
            try
            {
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
                LogSource.LogError(
                    $"[ISB AISHI LOYALTY] Universal post-raid patch failed to load: {ex}");
            }
        }

        private void Awake()
        {
            LogSource = Logger;
            ISBLocale.Initialize();

            this.GetOrAddComponent<ISBCheckpointManager>();
            this.GetOrAddComponent<ShorelineItemSecretManager>();
            this.GetOrAddComponent<ISBLocationSecretManager>();
            this.GetOrAddComponent<ISBBlackDivClashManager>();
            this.GetOrAddComponent<ISBBlackDivRigSecretManager>();
            this.GetOrAddComponent<ISBOrderManager>();
            this.GetOrAddComponent<ISBAishiRaidResultUiFallback>();
            this.GetOrAddComponent<ISBAishiReputationWorker>();

            new TarkovInitPatch().Enable();
            new ISBDogtagSidePatch().Enable();
            new ISBDogtagLevelPatch().Enable();
            new ISBDogtagDeathTimePatch().Enable();
            new ISBDogtagStatusPatch().Enable();
            new ISBDogtagWeaponPatch().Enable();
            new BotOwnerActivatePatch().Enable();
            new BotsControllerInitPatch().Enable();
            new GoonsRoguesSecretPatch().Enable();
            new ISBKillCountSecretPatch().Enable();
            new CommanderTeleportPatch().Enable();
            new ISBForcedHostilityAddEnemyPatch().Enable();
            new ISBHuntFirstTargetPlayerLastPatch().Enable();
            new ISBHuntNewTargetPlayerLastPatch().Enable();

            EnableAishiLoyaltyConditionPatches();
            EnablePitFireTeamCompatibility();
            EnableAishiRaidResultPatch();

            var isbEnums = new List<int> { 13701, 13709, 13710 }
                .ConvertAll(x => (WildSpawnType)x);

            var huntTargets = new List<WildSpawnType> { WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR };
            var priorityRoles = new HashSet<WildSpawnType>();

            foreach (var colab in ColabHuntTargets)
            {
                if (!Chainloader.PluginInfos.ContainsKey(colab.Guid))
                    continue;

                var colabEnums = colab.TargetEnums.ConvertAll(x => (WildSpawnType)x);
                huntTargets.AddRange(colabEnums);

                if (colab.PriorityTargets)
                {
                    foreach (var role in colabEnums)
                        priorityRoles.Add(role);
                }

                LogSource.LogInfo($"[ISB SOF] {colab.DisplayName} detected. Its bots were added as ISB hunt targets{(colab.PriorityTargets ? " with priority" : string.Empty)}.");
            }

            MonoBehaviourSingleton<HuntManager>.Instance.AddHuntRoles(isbEnums, huntTargets);

            MonoBehaviourSingleton<HuntManager>.Instance.AddHuntSides(isbEnums, new List<EPlayerSide>
            {
                EPlayerSide.Usec,
                EPlayerSide.Bear,
            });

            if (priorityRoles.Count > 0)
            {
                MonoBehaviourSingleton<HuntManager>.Instance.OnBotHuntInit += manager =>
                {
                    var hunter = manager.GetComponent<Player>();

                    if (hunter == null || !WildSpawnTypeExtensions.IsISB(hunter.Profile.Info.Settings.Role))
                        return;

                    foreach (var player in Singleton<GameWorld>.Instance.AllAlivePlayersList)
                    {
                        if (player.HealthController == null || !player.HealthController.IsAlive)
                            continue;

                        if (priorityRoles.Contains(player.Profile.Info.Settings.Role))
                        {
                            manager.priorityTargets.Add(player);
                        }
                    }
                };
            }

            MonoBehaviourSingleton<HuntManager>.Instance.OnBotHuntInit += manager =>
            {
                ISBHuntTargetPriority.PrepareManager(manager);
            };

            LogSource.LogInfo("[ISB SOF] Hunt priority Black Division -> AI targets -> player, stale-enemy Hunt release.");
        }
    }
}
