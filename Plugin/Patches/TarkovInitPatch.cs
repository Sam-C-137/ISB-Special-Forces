using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InputSystem;
using MoreBotsAPI.Behavior.Layers;
using ISBSpecialForces.Behavior.Layers;
using ISBSpecialForces.Behavior.Orders.Layers;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class TarkovInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(TarkovApplication).GetMethod(nameof(TarkovApplication.Init), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        protected static void PatchPostfix(IAssetsManager assetsManager, InputTree inputTree)
        {
            var brainList = new List<string>() { "PMC", "ExUsec", "Assault", "PmcUsec", "PmcBear", "PmcUSEC", "PmcBEAR" };
            var typesList = new List<int>() { 13701, 13709, 13710, 13707, 13708 }.ConvertAll(x => (WildSpawnType)x);

            BrainManager.AddCustomLayer(typeof(GoToCheckpointLayer), brainList, 25, typesList);
            BrainManager.AddCustomLayer(typeof(ISBOrderFlankLayer), brainList, 81, typesList);
            BrainManager.AddCustomLayer(typeof(ISBOrderAssaultLayer), brainList, 82, typesList);
            BrainManager.AddCustomLayer(typeof(ISBOrderPursuitLayer), brainList, 83, typesList);

            BrainManager.AddCustomLayer(typeof(HuntTargetLayer), brainList, 10, typesList);
            BrainManager.RemoveLayers(["AdvAssaultTarget"], brainList, typesList);
        }
    }
}