using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
#if ISB_SPT41
using SPTarkov.Common.Models.Logging;
using Dispatcher = SPTarkov.Common.Logger.SPTLoggerDispatcher;
#else
using SPTarkov.Server.Core.Utils.Logger;
using Dispatcher = SPTarkov.Server.Core.Utils.Logger.SptLoggerQueueManager;
#endif
namespace ISBSpecialForcesServer.Patches;
#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.PostLoad + 5)]
#else
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 5)]
#endif
public sealed class SpawnSyncLoggingPatch(Dispatcher dispatcher) : IOnLoad
{
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
        var handlers = AccessTools.Field(typeof(Dispatcher), "_logHandlers")?.GetValue(dispatcher) as IDictionary<LoggerType, ILogHandler>
            ?? throw new MissingMemberException("ISB sync console dispatcher unavailable.");
        foreach (var entry in handlers.ToArray())
            if (entry.Value.GetType().FullName ==
#if ISB_SPT41
                "SPTarkov.Common.Logger.Handlers.ConsoleLogHandler"
#else
                "SPTarkov.Server.Core.Utils.Logger.Handlers.ConsoleLogHandler"
#endif
            )
                handlers[entry.Key] = new SyncConsoleHandler(entry.Value);
        return Task.CompletedTask;
    }

    private static bool AllowMessage(SptLogMessage message)
    {
#if ISB_SPT41
        if (message.Exception != null || message.LogLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
#else
        if (message.Exception != null || message.LogLevel <= SPTarkov.Server.Core.Models.Spt.Logging.LogLevel.Warn)
#endif
            return true;
        return message.Message != "[Client Request] /ISB/spawn/report" &&
            message.Message != "[Client Request] /ISB/spawn/progress" &&
            message.Message != "[Client Request] /ISB/aishi/participants" &&
            message.Message != "[Client Request] /ISB/miniLabs/state";
    }

    private sealed class SyncConsoleHandler(ILogHandler inner) : ILogHandler
    {
        public LoggerType LoggerType => inner.LoggerType;

        public void Log(SptLogMessage message, BaseSptLoggerReference logger)
        {
            if (AllowMessage(message))
                inner.Log(message, logger);
        }
#if ISB_SPT41
        public ValueTask DisposeAsync() => inner.DisposeAsync();
#endif
    }
}
