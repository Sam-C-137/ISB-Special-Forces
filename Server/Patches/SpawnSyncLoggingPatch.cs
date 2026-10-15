using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
#if ISB_SPT41
using SptLogMessage = SPTarkov.Common.Models.Logging.SptLogMessage;
#else
using SptLogMessage = SPTarkov.Server.Core.Utils.Logger.SptLogMessage;

#endif
namespace ISBSpecialForcesServer.Patches;
#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.PostLoad + 5)]
#else
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 5)]
#endif
public sealed class SpawnSyncLoggingPatch : IOnLoad
{
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
        var handler = AccessTools.TypeByName(
#if ISB_SPT41
            "SPTarkov.Common.Logger.Handlers.ConsoleLogHandler"
#else
        "SPTarkov.Server.Core.Utils.Logger.Handlers.ConsoleLogHandler"
#endif
        ) ?? throw new MissingMemberException("ISB sync console handler unavailable.");
        new Harmony("com.samc137.isbspecialforces.spawnsynclogging").Patch(AccessTools.Method(handler, "Log"), prefix: new HarmonyMethod(typeof(SpawnSyncLoggingPatch), nameof(AllowMessage)));
        return Task.CompletedTask;
    }

    private static bool AllowMessage(SptLogMessage message)
    {
        if (message.Exception != null)
            return true;
        var text = message.Message;
        return text != "[Client Request] /ISB/spawn/report" && text != "[Client Request] /ISB/spawn/progress";
    }
}
