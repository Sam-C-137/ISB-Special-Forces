using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 20)]
public sealed class BlackoutEventCompatibility(ISBSpawnController spawns, ILogger<BlackoutEventCompatibility> logger) : IOnLoad
{
    private static ISBSpawnController? _spawns;
    public Task OnLoad()
    {
        var type = AccessTools.TypeByName("BlackoutServer.BlackoutSpawnController");
        if (type == null)
            return Task.CompletedTask;
        var getter = AccessTools.PropertyGetter(type, "CurrentRaidBlackout");
        if (getter == null)
        {
            logger.LogError("ISB: installed Vultify Blackout version has no compatible raid state getter.");
            return Task.CompletedTask;
        }

        _spawns = spawns;
        new Harmony("com.samc137.isbspecialforces.blackoutcompatibility").Patch(getter, postfix: new HarmonyMethod(typeof(BlackoutEventCompatibility), nameof(FilterRoll)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static void FilterRoll(ref bool __result)
    {
        if (__result && _spawns?.GetLabsPreBlackoutEventStatus(null).active == true)
            __result = false;
    }
}
