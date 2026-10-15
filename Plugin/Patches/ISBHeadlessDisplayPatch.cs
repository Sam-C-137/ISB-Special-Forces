using System.Reflection;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBHeadlessDisplayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(GameGraphicsClass).GetMethod(nameof(GameGraphicsClass.CheckAltEnter));

        [PatchPrefix]
        private static bool Prefix() => ISBFikaAuthority.CanPresentLocally;
    }
}
