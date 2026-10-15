using System.Reflection;
using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal class ISBAishiRaidPlayerReadyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(GameWorld).GetMethod("OnGameStarted");
        [PatchPostfix]
        private static void Postfix(GameWorld __instance)
        {
            if (__instance == null || __instance is HideoutGameWorld)
                return;
            ISBRevengeBridge.EnsureRaidInitialized();
            ISBAishiLoyaltyConditionState.EnsureRaidInitialized();
            if (!ISBFikaAuthority.IsAiAuthority)
                return;
            var players = __instance.AllAlivePlayersList;
            if (players == null)
                return;
            foreach (var player in players)
            {
                var bot = player?.AIData?.BotOwner;
                if (bot?.Profile?.Info?.Settings != null && WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role))
                    BotOwnerActivatePatch.ApplyAishiLoyaltyCondition(bot);
            }
        }
    }
}
