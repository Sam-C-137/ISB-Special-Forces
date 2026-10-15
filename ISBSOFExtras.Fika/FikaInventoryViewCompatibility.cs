using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.InventoryLogic;
using Fika.Core.Main.Players;
using HarmonyLib;

namespace ISBSSOFExtra.Fika
{
    internal static class FikaInventoryViewCompatibility
    {
        private static Harmony _harmony;

        internal static void Enable()
        {
            if (_harmony != null)
                return;
            var harmony = new Harmony("com.samc137.isbsofextras.fika.inventory-views");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ObservedPlayer), "RefreshSlotViews"),
                    transpiler: new HarmonyMethod(typeof(FikaInventoryViewCompatibility), nameof(ReplaceSlotKeys)));
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

        private static IEnumerable<CodeInstruction> ReplaceSlotKeys(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo original = AccessTools.PropertyGetter(typeof(Slot), nameof(Slot.FullId));
            MethodInfo replacement = AccessTools.Method(typeof(FikaInventoryViewCompatibility), nameof(GetSlotViewKey));
            int count = 0;
            foreach (var instruction in code)
            {
                if (!instruction.Calls(original))
                    continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                count++;
            }
            if (count != 4)
                throw new InvalidOperationException("Unsupported Fika inventory view layout: " + count + " slot keys.");
            return code;
        }

        private static string GetSlotViewKey(Slot slot)
        {
            return slot.ParentItem.Id + "/" + slot.ID;
        }
    }
}
