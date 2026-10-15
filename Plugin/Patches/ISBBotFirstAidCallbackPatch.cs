using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBBotFirstAidCallbackPatch : ModulePatch
    {
        private static readonly AccessTools.FieldRef<BotWeaponManager, bool> Disposed = AccessTools.FieldRefAccess<BotWeaponManager, bool>("_disposed");
        protected override MethodBase GetTargetMethod()
        {
            var callback = typeof(BotFirstAid).GetNestedType("CG_ApplyToSelf", BindingFlags.Public | BindingFlags.NonPublic);
            return AccessTools.Method(callback, "method_1");
        }

        private static bool Available(BotOwner owner)
        {
            return owner != null && owner.WeaponManager != null && !Disposed(owner.WeaponManager) && owner.WeaponManager.Selector != null;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(BotOwner), nameof(BotOwner.WeaponManager));
            MethodInfo guard = AccessTools.Method(typeof(ISBBotFirstAidCallbackPatch), nameof(Available));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    count++;
                    Label alive = generator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Call, guard);
                    yield return new CodeInstruction(OpCodes.Brtrue, alive);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ret);
                    var resume = new CodeInstruction(OpCodes.Nop);
                    resume.labels.Add(alive);
                    yield return resume;
                }
                yield return instruction;
            }
            if (count != 1)
                throw new InvalidOperationException("Unsupported bot first-aid callback layout: " + count + " weapon managers.");
        }
    }
}
