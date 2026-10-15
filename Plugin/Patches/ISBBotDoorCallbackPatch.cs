using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBBotDoorCallbackPatch : ModulePatch
    {
        private static readonly System.Type CallbackType = typeof(BotDoorOpener).GetNestedType("CG_RunEnteringDoorSequence", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo Opener = AccessTools.Field(CallbackType, "botDoorOpener_0");
        private static readonly AccessTools.FieldRef<BotDoorOpener, BotOwner> Owner = AccessTools.FieldRefAccess<BotDoorOpener, BotOwner>("_owner");
        private static readonly AccessTools.FieldRef<BotDoorOpener, bool> Entering = AccessTools.FieldRefAccess<BotDoorOpener, bool>("_enteringDoorSequence");

        protected override MethodBase GetTargetMethod() => AccessTools.Method(CallbackType, "method_0");

        [PatchPrefix]
        private static bool Prefix(object __instance)
        {
            var opener = (BotDoorOpener)Opener.GetValue(__instance);
            BotOwner owner = opener == null ? null : Owner(opener);
            if (owner != null && !owner.IsDead && owner.Memory != null && owner.Mover != null && owner.Steering != null && owner.AITaskManager != null && owner.Settings?.FileSettings?.Move != null)
                return true;
            if (opener != null)
                Entering(opener) = false;
            return false;
        }
    }
}
