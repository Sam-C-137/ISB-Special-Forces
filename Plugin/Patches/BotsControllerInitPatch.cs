using EFT;
using ISBSpecialForces.Components;
using ISBSpecialForces.Behavior.Tactics;
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

        [PatchPostfix]
        protected static void PatchPostfix(BotsController __instance)
        {
            Behavior.ISB.ISBNativeSquad.Reset();
            TarkovInitPatch.UpdateBlackDivisionEventSain(false);
            MonoBehaviourSingleton<ISBCameraHuntController>.Instance?.ResetRaid();
            var tacticalBrainManager = MonoBehaviourSingleton<ISBTacticalController>.Instance;
            tacticalBrainManager?.ResetRaid();
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo("[ISB] BotsController initialized, initializing checkpoint manager...");
            }

            var checkpointManager = MonoBehaviourSingleton<ISBCheckpointManager>.Instance;
            if (checkpointManager != null)
            {
                checkpointManager.InitRaid();
                TarkovInitPatch.UpdateBlackDivisionEventSain(checkpointManager.BlackDivisionContainmentActive && ISBFikaAuthority.IsAiAuthority);
            }
            else
            {
                Plugin.LogSource.LogError("[ISB] ISBCheckpointManager instance is missing. Something failed during plugin startup.");
            }

            ISBRevengeBridge.InitRaid();
            MonoBehaviourSingleton<ISBRevengeHuntController>.Instance?.ResetRaid();
            ISBSpawnProgressBridge.Init(Comfort.Common.Singleton<GameWorld>.Instance, false);
            ISBAishiLoyaltyConditionState.InitRaid();
            ISBForcedHostilityState.InitRaid();
            var pitFireCompatibility = UnityEngine.Object.FindObjectOfType<ISBPitFireTeamCompatibility>();
            pitFireCompatibility?.ResetRaid();
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
