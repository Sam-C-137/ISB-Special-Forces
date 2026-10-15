using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using Fika.Core.Main.Players;
using HarmonyLib;

namespace ISBSSOFExtra.Fika
{
    internal static class FikaAggressorCompatibility
    {
        private static Harmony _harmony;

        internal static void Enable()
        {
            if (_harmony != null)
                return;
            var harmony = new Harmony("com.samc137.isbsofextras.fika.aggressor");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ObservedPlayer), nameof(ObservedPlayer.ManageAggressor)),
                    transpiler: new HarmonyMethod(typeof(FikaAggressorCompatibility), nameof(GuardMissingAggressor)));
                _harmony = harmony;
            }
            catch
            {
                harmony.UnpatchSelf();
                throw;
            }
        }

        internal static void Disable()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private static IEnumerable<CodeInstruction> GuardMissingAggressor(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            MethodInfo lookup = AccessTools.Method(typeof(GameWorld), nameof(GameWorld.GetAlivePlayerByProfileID));
            MethodInfo exists = AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit");
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(lookup))
                    continue;
                count++;
                Label alive = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Call, exists);
                yield return new CodeInstruction(OpCodes.Brtrue, alive);
                yield return new CodeInstruction(OpCodes.Pop);
                yield return new CodeInstruction(OpCodes.Ret);
                var resume = new CodeInstruction(OpCodes.Nop);
                resume.labels.Add(alive);
                yield return resume;
            }
            if (count != 1)
                throw new InvalidOperationException("Unsupported Fika aggressor lookup layout: " + count + " lookups.");
        }
    }
}
