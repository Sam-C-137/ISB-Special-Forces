using System.Runtime.CompilerServices;
using EFT;
using UnityEngine;
#if SPT40
using NativeTarget = GoalTargetClass;
#else
using NativeTarget = BotGoalTarget;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBSearchMemory
    {
        private sealed class Selection { internal float Until; }
        private static readonly ConditionalWeakTable<BotOwner, Selection> Selections = new ConditionalWeakTable<BotOwner, Selection>();

        internal static bool SelectPrefix(NativeTarget __instance, ref PlaceForCheck __0, out PlaceForCheck __state)
        {
            __state = null;
#if SPT40
            var bot = __instance.BotOwner_0;
#else
            var bot = __instance._owner;
#endif
            if (!ISBRuntimeBindings.IsAuthority() || !ISBNativeBrain.Owns(bot)) return true;
            var previous = __instance.GoalTarget;
            if (previous == __0 && previous?.IsCome != true) return true;
            var selection = Selections.GetValue(bot, _ => new Selection());
            if (__0 != null && !__0.IsCome && previous != null && !previous.IsCome && previous.Reacheble &&
                !previous.IsOld() && bot.Memory.GoalEnemy == null && bot.Mover.HasPathAndNoComplete &&
                Time.time < selection.Until && !(__0.IsDanger && !previous.IsDanger))
                return false;
            if (previous?.IsCome == true) __state = previous;
            if (__0?.IsCome == true) __0 = null;
            selection.Until = Time.time + 3f;
            return true;
        }

        internal static void SelectPostfix(PlaceForCheck __state)
        {
            if (__state == null) return;
#if SPT40
            __state.IsCome_1 = true;
#else
            __state._isCome = true;
#endif
        }
    }
}
