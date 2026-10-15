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
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(PlayerPhysicalClass), nameof(PlayerPhysicalClass.OnWeightUpdated));
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

        internal static float FilterWeight(float weight, PlayerPhysicalClass physical) => ForPlayer(weight, physical?.Player_0);
        internal static float ForPlayer(float weight, Player player)
        {
            var settings = player?.Profile?.Info?.Settings;
            return player != null && player.IsAI && settings != null && WildSpawnTypeExtensions.IsISB(settings.Role) ? 0f : weight;
        }
    }

    internal class ISBBotWeightBridgePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.PropertyGetter(typeof(BasePhysicalClass.PlayerBridge), "TotalWeight");
        [PatchPostfix]
        private static void PatchPostfix(BasePhysicalClass.PlayerBridge __instance, ref float __result)
        {
            __result = ISBBotWeightPatch.ForPlayer(__result, __instance.Player_0);
        }
    }
}
