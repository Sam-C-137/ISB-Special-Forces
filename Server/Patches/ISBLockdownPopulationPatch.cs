using HarmonyLib;
using ISBSpecialForcesServer.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader)]
public sealed class ISBLockdownPopulationPatch : IOnLoad
{
    private static ISBSpawnController? _controller;
    private static ISBSpawnProgressController? _progress;
    public ISBLockdownPopulationPatch(ISBSpawnController controller, ISBSpawnProgressController progress)
    {
        _controller = controller;
        _progress = progress;
    }

    public Task OnLoad()
    {
        var target = AccessTools.Method(typeof(LocationLifecycleService), "GenerateLocationAndLoot") ?? throw new MissingMethodException("LocationLifecycleService.GenerateLocationAndLoot");
        new Harmony("com.samc137.isbspecialforces.lockdown.population").Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(ISBLockdownPopulationPatch), nameof(Prefix))) { priority = Priority.First }, postfix: new HarmonyMethod(AccessTools.Method(typeof(ISBLockdownPopulationPatch), nameof(Postfix))) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    public static void Prefix(MongoId __0, string __1, bool __2)
    {
        _controller?.PrepareLabsLocationForGeneration(__0.ToString(), __1, __2);
    }

    public static void Postfix(MongoId __0, string __1, LocationBase __result)
    {
        _controller?.FilterLockdownRaidPopulation(__0.ToString(), __1, __result);
        _progress?.Apply(__0.ToString(), new string (__1.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()), __result);
    }
}
