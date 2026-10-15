using System.Reflection;
using EFT.Settings.Graphics;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;

namespace ISBSpecialForces.Patches
{
    internal sealed class ISBHeadlessDisplayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(GraphicsSettingsController).GetMethod(nameof(GraphicsSettingsController.CheckAltEnter));

        [PatchPrefix]
        private static bool Prefix() => ISBFikaAuthority.CanPresentLocally;
    }
}
