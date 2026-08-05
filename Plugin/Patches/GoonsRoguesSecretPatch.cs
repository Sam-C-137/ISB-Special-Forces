using Comfort.Common;
using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using SPT.SinglePlayer.Utils.InRaid;
using System.Collections.Generic;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class GoonsRoguesSecretPatch : ModulePatch
    {
        private const string TriggerId = "isb_secret_expurgo_farol_hunt";
        private const int RequiredGoons = 3;
        private const int RequiredRogues = 6;

        private static readonly HashSet<WildSpawnType> GoonRoles = new HashSet<WildSpawnType>
        {
            WildSpawnType.bossKnight,
            WildSpawnType.followerBigPipe,
            WildSpawnType.followerBirdEye,
        };

        private static readonly HashSet<string> goonsKilled = new HashSet<string>();
        private static readonly HashSet<string> roguesKilled = new HashSet<string>();

        public static void Reset()
        {
            goonsKilled.Clear();
            roguesKilled.Clear();
        }

        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetMethod(nameof(Player.OnBeenKilledByAggressor), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(Player __instance, IPlayer aggressor)
        {
            if (__instance == null || aggressor == null)
                return;

            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;

            if (RaidChangesUtil.LocationId == null || RaidChangesUtil.LocationId.ToLower() != "lighthouse")
                return;

            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld?.MainPlayer == null || aggressor.ProfileId != gameWorld.MainPlayer.ProfileId)
                return;

            var role = __instance.Profile.Info.Settings.Role;

            if (GoonRoles.Contains(role))
                goonsKilled.Add(__instance.ProfileId);
            else if (role == WildSpawnType.exUsec)
                roguesKilled.Add(__instance.ProfileId);
            else
                return;

            Plugin.LogSource.LogInfo($"[ISB BOSS'S HATRED] Rogues Eliminated: The Goons {goonsKilled.Count}/{RequiredGoons}, Regular Operatives (ExUsec) {roguesKilled.Count}/{RequiredRogues}.");

            if (goonsKilled.Count >= RequiredGoons && roguesKilled.Count >= RequiredRogues)
                ISBSecretsManager.Fire(TriggerId);
        }
    }
}