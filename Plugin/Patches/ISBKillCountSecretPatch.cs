using Comfort.Common;
using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBKillCountSecretPatch : ModulePatch
    {
        private const string TriggerId = "isb_secret_massacre_hunt";
        private const int RequiredKills = 8;

        private static readonly HashSet<WildSpawnType> CountedRoles = new HashSet<WildSpawnType>
        {
            (WildSpawnType)13701,
            (WildSpawnType)13709,
            (WildSpawnType)13710,
        };

        private static readonly HashSet<string> killed = new HashSet<string>();

        public static void Reset()
        {
            killed.Clear();
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

            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld?.MainPlayer == null || aggressor.ProfileId != gameWorld.MainPlayer.ProfileId)
                return;

            if (!CountedRoles.Contains(__instance.Profile.Info.Settings.Role))
                return;

            killed.Add(__instance.ProfileId);

            Plugin.LogSource.LogInfo($"[ISB BOSS'S HATRED] ISB Teammates Eliminated: {killed.Count}/{RequiredKills}.");

            if (killed.Count >= RequiredKills)
                ISBSecretsManager.Fire(TriggerId);
        }
    }
}
