using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal class ISBBotWeightPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Physical), nameof(Physical.OnWeightUpdated));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> PatchTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            int replaced = 0;
            int weightFields = 0;
            var filter = AccessTools.Method(typeof(ISBBotWeightPatch), nameof(FilterWeight));
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && (field.Name == "TotalWeight" || field.Name == "TotalWeightEliteSkill"))
                    weightFields++;
                if (weightFields == 2 && replaced == 0 && instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method && method.ReturnType == typeof(float) && method.Name == "op_Implicit")
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, filter));
                    replaced++;
                }
            }

            if (replaced != 1 || weightFields != 2)
                throw new InvalidOperationException("ISB: native physical weight getters changed");
            return result;
        }

        internal static float FilterWeight(float weight, Physical physical) => ForPlayer(weight, physical?._player);
        internal static float ForPlayer(float weight, Player player)
        {
            var settings = player?.Profile?.Info?.Settings;
            if (player == null || !player.IsAI || settings == null)
                return weight;
            var spawnId = player.AIData?.BotOwner?.SpawnProfileData?.SpawnParams?.Id_spawn;
            return WildSpawnTypeExtensions.IsISB(settings.Role) || spawnId?.StartsWith("isb_", StringComparison.Ordinal) == true ? 0f : weight;
        }
    }

    internal class ISBBotWeightBridgePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertyGetter(typeof(PhysicalBase.PlayerBridge), "TotalWeight");
        [PatchPostfix]
        private static void PatchPostfix(PhysicalBase.PlayerBridge __instance, ref float __result)
        {
            __result = ISBBotWeightPatch.ForPlayer(__result, __instance._iPlayer);
        }
    }
}
