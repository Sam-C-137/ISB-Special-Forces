using EFT;
using ISBSpecialForces.Components;
using ISBSpecialForces.Behavior.Orders.Controllers;
using ISBSpecialForces.Behavior.Orders.State;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class BotsControllerInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotsController).GetMethod(nameof(BotsController.Init), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        protected static void PatchPrefix()
        {
            ISBSpawnRefreshBridge.RefreshCurrentRaid();
        }

        [PatchPostfix]
        protected static void PatchPostfix(BotsController __instance)
        {
            ISBOrderSettings.InitRaid();

            var tacticalBrainManager = MonoBehaviourSingleton<ISBOrderManager>.Instance;
            tacticalBrainManager?.ResetRaid();

            Plugin.LogSource.LogInfo("[ISB] BotsController initialized, initializing checkpoint manager...");

            var checkpointManager = MonoBehaviourSingleton<ISBCheckpointManager>.Instance;

            if (checkpointManager != null)
            {
                checkpointManager.InitRaid();
            }
            else
            {
                Plugin.LogSource.LogError("[ISB] ISBCheckpointManager instance is missing. Something failed during plugin startup.");
            }

            ISBRevengeBridge.InitRaid();
            ISBAishiLoyaltyConditionState.InitRaid();
            ISBForcedHostilityState.InitRaid();
            ISBSecretsManager.ResetRaid();
            GoonsRoguesSecretPatch.Reset();
            ISBKillCountSecretPatch.Reset();
            ISBSecretTeleport.Reset();
            ISBBlackDivCounterattackTeleport.Reset();

            var blackDivClashManager = MonoBehaviourSingleton<ISBBlackDivClashManager>.Instance;
            blackDivClashManager?.ResetRaid();
        }
    }
}
