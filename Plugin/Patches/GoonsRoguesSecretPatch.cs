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
        private static readonly Dictionary<string, HashSet<string>> goonsKilled = new Dictionary<string, HashSet<string>>();
        private static readonly Dictionary<string, HashSet<string>> roguesKilled = new Dictionary<string, HashSet<string>>();
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
            if (!ISBFikaAuthority.IsAiAuthority || __instance?.Profile?.Info?.Settings == null || !ISBSecretsManager.IsHumanAggressor(aggressor))
                return;
            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;
            if (RaidChangesUtil.LocationId == null || RaidChangesUtil.LocationId.ToLower() != "lighthouse")
                return;
            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;
            var role = __instance.Profile.Info.Settings.Role;
            if (!GoonRoles.Contains(role) && role != WildSpawnType.exUsec)
                return;
            if (!goonsKilled.TryGetValue(aggressor.ProfileId, out var playerGoons))
                goonsKilled[aggressor.ProfileId] = playerGoons = new HashSet<string>();
            if (!roguesKilled.TryGetValue(aggressor.ProfileId, out var playerRogues))
                roguesKilled[aggressor.ProfileId] = playerRogues = new HashSet<string>();
            if (GoonRoles.Contains(role))
                playerGoons.Add(__instance.ProfileId);
            else if (role == WildSpawnType.exUsec)
                playerRogues.Add(__instance.ProfileId);
            else
                return;
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB BOSS'S HATRED] Rogues Eliminated: The Goons {playerGoons.Count}/{RequiredGoons}, Regular Operatives (ExUsec) {playerRogues.Count}/{RequiredRogues}.");
            }

            if (playerGoons.Count >= RequiredGoons && playerRogues.Count >= RequiredRogues)
                ISBSecretsManager.Fire(TriggerId);
        }
    }
}
