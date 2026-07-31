using Comfort.Common;
using EFT;
using HarmonyLib;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;
using ISBSpecialForces.Behavior.Orders.Components;
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
            ISBForcedHostilityState.OnBotActivated(__instance);

            if (WildSpawnTypeExtensions.IsISB(__instance.Profile.Info.Settings.Role))
            {
                __instance.GetOrAddISBManager();
                __instance.GetOrAddOrderAgent();
                ApplyAishiLoyaltyCondition(__instance);
            }

            ISBPitFireTeamCompatibility.SyncBot(__instance);
        }

        private static void ApplyAishiLoyaltyCondition(BotOwner botOwner)
        {
            var group = botOwner.BotsGroup;

            if (group == null)
                return;

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld == null)
                return;

            if (ISBAishiLoyaltyConditionState.Broken)
            {
                ISBAishiLoyaltyConditionState.ApplyHostilityToGroupOnce(group);
                return;
            }

            var mainPlayer = gameWorld.MainPlayer;

            if (mainPlayer == null)
                return;

            if (mainPlayer.Side == EPlayerSide.Savage)
            {
                group.AddEnemy(mainPlayer, EBotEnemyCause.initial);
                ISBAishiLoyaltyConditionState.AnnounceScavHostility();
                return;
            }

            if (!ISBAishiLoyaltyConditionState.Holding)
                return;

            var role = botOwner.Profile.Info.Settings.Role;

            if (!ISBAishiLoyaltyConditionState.IsProtectedFrom(role))
                return;

            if (!ISBAishiLoyaltyConditionState.IsEligiblePlayer(mainPlayer))
                return;

            if (ISBRevengeBridge.HasPendingRevenge(group, mainPlayer))
            {
                Plugin.LogSource.LogInfo($"[ISB] '{botOwner.Profile.Nickname}' remembers the last raid. No Aishi protection for you.");
                return;
            }

            group.RemoveEnemy(mainPlayer, EBotEnemyCause.initial);
            group.AddNeutral(mainPlayer);
        }
    }
}
