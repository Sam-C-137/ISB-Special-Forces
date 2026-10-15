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
            (WildSpawnType)13702,
            (WildSpawnType)13703,
            (WildSpawnType)13704,
            (WildSpawnType)13705,
            (WildSpawnType)13706,
            (WildSpawnType)13709,
            (WildSpawnType)13710,
            (WildSpawnType)13711,
            (WildSpawnType)13712,
            (WildSpawnType)13713,
            (WildSpawnType)13714,
            (WildSpawnType)13715,
            (WildSpawnType)13716,
        };
        private static readonly Dictionary<string, HashSet<string>> killed = new Dictionary<string, HashSet<string>>();
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
            if (!ISBFikaAuthority.IsAiAuthority || __instance?.Profile?.Info?.Settings == null || !ISBSecretsManager.IsHumanAggressor(aggressor))
                return;
            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;
            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;
            if (!CountedRoles.Contains(__instance.Profile.Info.Settings.Role))
                return;
            if (!killed.TryGetValue(aggressor.ProfileId, out var playerKills))
                killed[aggressor.ProfileId] = playerKills = new HashSet<string>();
            playerKills.Add(__instance.ProfileId);
            if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
            {
                Plugin.LogSource.LogInfo($"[ISB BOSS'S HATRED] ISB Teammates Eliminated: {playerKills.Count}/{RequiredKills}.");
            }

            if (playerKills.Count >= RequiredKills)
                ISBSecretsManager.Fire(TriggerId);
        }
    }
}
