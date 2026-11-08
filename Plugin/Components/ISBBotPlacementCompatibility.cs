using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using ISB.Compatibility;
using System;
using System.Linq;

namespace ISBSpecialForces.Components
{
    internal static class ISBBotPlacementCompatibility
    {
        internal const string PluginGuid = "com.acidphantasm.botplacementsystem";
        private const string HarmonyId = "com.samc137.isb.botplacement.compatibility";
        private static bool _initialized;

        internal static void Initialize()
        {
            if (_initialized || !Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin) || plugin?.Instance == null)
                return;
            var type = plugin.Instance.GetType().Assembly.GetType("BotPlacementSystemClient.Patches.BossProgressiveRegressivePatch", true);
            var target = AccessTools.Method(type, "PatchPostfix") ?? throw new MissingMethodException(type.FullName, "PatchPostfix");
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(typeof(ISBBotPlacementCompatibility), nameof(Prefix)) { priority = Priority.First });
            if (Harmony.GetPatchInfo(target)?.Prefixes.Any(patch => patch.owner == HarmonyId) != true)
                throw new InvalidOperationException("ISB: Bot Placement System compatibility was not installed.");
            _initialized = true;
        }

        internal static bool ShouldSkip(bool laboratory, bool authority, string spawnId, int profileCount) =>
            laboratory && authority && profileCount == 0 && spawnId?.StartsWith("isb_population_", StringComparison.Ordinal) == true;

        private static bool Prefix(BotCreationData data)
        {
            var world = Singleton<GameWorld>.Instance;
            return !ShouldSkip(world != null && string.Equals(world.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase),
                ISBFikaAuthority.IsAiAuthority, data?.SpawnParams?.Id_spawn, data?.Profiles?.Count ?? 0);
        }
    }
}
