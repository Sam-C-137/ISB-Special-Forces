using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ISBSpecialForcesServer;

[Injectable(InjectionType.Singleton)]
public class ISBLogger
{
    private readonly bool _enableLogs;
    private readonly ISptLogger<ISBLogger> _logger;
    public ISBLogger(
        ISptLogger<ISBLogger> logger,
        ConfigController configController)
    {
        _enableLogs = configController.ModConfig.debug.logs;
        _logger = logger;
    }

    public void Info(string message)
    {
        if (_enableLogs)
        {
            _logger.Info($"[ISB SOF] {message}");
        }
    }
    public void Warn(string message)
    {
        _logger.Warning($"[ISB SOF] WARNING: {message}");
    }
    public void Error(string message)
    {
        _logger.Error($"[ISB SOF] ERROR: {message}");
    }

    public void PostSptLoad()
    {
        _logger.Success("\x1b[38;2;200;80;220m[ISB SOF] “Control, this is Snake Two. All personnel are ready. The game to hunt some scorpions is about to begin.”\x1b[0m"); ;
    }
}