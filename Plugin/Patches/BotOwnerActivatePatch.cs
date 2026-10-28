using Comfort.Common;
using EFT;
using HarmonyLib;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;
using ISBSpecialForces.Behavior.Tactics;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class BotOwnerActivatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotOwner).GetMethod(nameof(BotOwner.method_10), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        protected static void PatchPostfix(BotOwner __instance)
        {
            if (!ISBFikaAuthority.IsAiAuthority || __instance?.Profile?.Info?.Settings == null)
                return;
            UnityEngine.Profiling.Profiler.BeginSample("ISB.BotActivation");
            try
            {
                ISBSpawnProgressBridge.MarkSpawn(__instance);
                ISBAishiLoyaltyConditionState.EnsureRaidInitialized();
                ISBForcedHostilityState.OnBotActivated(__instance);
                ISBLabsGatePassage.Register(__instance);
                var clashManager = MonoBehaviourSingleton<ISBBlackDivClashManager>.Instance;
                clashManager?.RegisterBot(__instance);
                if (ISBBotWeightPatch.ForPlayer(1f, __instance.GetPlayer) == 0f)
                    __instance.GetPlayer.Physical.OnWeightUpdated();
                if (WildSpawnTypeExtensions.IsISB(__instance.Profile.Info.Settings.Role))
                {
                    __instance.GetOrAddISBManager();
                    ISBTacticalAgent.GetOrAdd(__instance);
                    ApplyAishiLoyaltyCondition(__instance);
                }
                else if (MonoBehaviourSingleton<ISBCheckpointManager>.Instance?.IsBlackDivisionCheckpointBot(__instance) == true)
                {
                    var checkpointBot = __instance.GetOrAddISBManager();
                    if (!checkpointBot.HasAssignedCheckpoint())
                        checkpointBot.Init(__instance);
                }

                ISBPitFireTeamCompatibility.SyncBot(__instance);
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        internal static void ApplyAishiLoyaltyCondition(BotOwner botOwner)
        {
            if (!ISBFikaAuthority.IsAiAuthority || botOwner == null)
                return;
            var group = botOwner.BotsGroup;
            if (group == null)
                return;
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
                return;
            ISBAishiLoyaltyConditionState.ApplyHostilityToGroupOnce(group);
            var players = gameWorld.AllAlivePlayersList;
            if (players == null)
                return;
            var role = botOwner.Profile.Info.Settings.Role;
            if (!ISBAishiLoyaltyConditionState.IsProtectedFrom(role))
                return;
            foreach (var player in players)
            {
                if (player == null || player.IsAI)
                    continue;
                if (player.Side == EPlayerSide.Savage)
                {
                    group.AddEnemy(player, EBotEnemyCause.initial);
                    continue;
                }

                if (!ISBAishiLoyaltyConditionState.ProtectsHuman(player) || ISBRevengeBridge.HasPendingRevenge(group, player))
                    continue;
                group.RemoveEnemy(player, EBotEnemyCause.initial);
                group.AddNeutral(player);
            }
        }
    }
}
