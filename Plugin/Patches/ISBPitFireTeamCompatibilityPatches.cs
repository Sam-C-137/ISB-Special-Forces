using EFT;
using HarmonyLib;
using ISBSpecialForces.Components;
using System;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal static class ISBPitFireTeamDynamicPatcher
    {
        private static bool _enabled;
        public static void Enable(Assembly pitFireAssembly)
        {
            if (_enabled || pitFireAssembly == null)
                return;
            _enabled = true;
            var harmony = new Harmony("com.samc137.isb.pitfireteamcompatibility");
            PatchMatchingMethods(harmony, pitFireAssembly.GetType("pitTeam.Utils.Enemy", false), "MakeEnemy", nameof(BlockOwnerTargetPrefix));
            PatchMatchingMethods(harmony, pitFireAssembly.GetType("pitTeam.Components.pitAIBossPlayer", false), "RegisterContactEnemyForFollower", nameof(BlockOwnerTargetPrefix));
            PatchMatchingMethods(harmony, pitFireAssembly.GetType("pitTeam.Components.pitAIBossPlayer", false), "PrioritizeEnemy", nameof(BlockOwnerBotPrefix));
            var sainEnemyController = AccessTools.TypeByName("SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController") ?? AccessTools.TypeByName("SAIN.Components.Enemy.SAINEnemyController");
            PatchMatchingMethods(harmony, sainEnemyController, "CheckAddEnemy", nameof(BlockSainAcquirePrefix));
        }

        private static void PatchMatchingMethods(Harmony harmony, Type type, string methodName, string prefixName)
        {
            if (type == null)
                return;
            var prefix = typeof(ISBPitFireTeamDynamicPatcher).GetMethod(prefixName, BindingFlags.NonPublic | BindingFlags.Static);
            if (prefix == null)
                return;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                }
                catch
                {
                }
            }
        }

        private static bool BlockOwnerTargetPrefix(object[] __args)
        {
            BotOwner owner = null;
            IPlayer target = null;
            if (__args != null)
            {
                foreach (var argument in __args)
                {
                    if (owner == null && argument is BotOwner botOwner)
                    {
                        owner = botOwner;
                        continue;
                    }

                    if (target == null && argument is IPlayer player)
                        target = player;
                }
            }

            return !ISBPitFireTeamCompatibility.ShouldBlockOwnerEnemy(owner, target);
        }

        private static bool BlockOwnerBotPrefix(object[] __args)
        {
            BotOwner owner = null;
            BotOwner targetBot = null;
            if (__args != null)
            {
                foreach (var argument in __args)
                {
                    if (!(argument is BotOwner botOwner))
                        continue;
                    if (owner == null)
                        owner = botOwner;
                    else if (targetBot == null)
                        targetBot = botOwner;
                }
            }

            return !ISBPitFireTeamCompatibility.ShouldBlockOwnerEnemy(owner, targetBot?.GetPlayer);
        }

        private static bool BlockSainAcquirePrefix(object __instance, object[] __args)
        {
            if (__instance == null)
                return true;
            IPlayer target = null;
            if (__args != null)
            {
                foreach (var argument in __args)
                {
                    if (argument is IPlayer player)
                    {
                        target = player;
                        break;
                    }
                }
            }

            var owner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner ?? AccessTools.Field(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
            return !ISBPitFireTeamCompatibility.ShouldBlockOwnerEnemy(owner, target);
        }
    }
}
