using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;

namespace ISBSpecialForcesServer;
[Injectable(InjectionType.Singleton)]
public class ISBLogger
{
    private readonly ISptLogger<ISBLogger> _logger;
    private readonly ConfigController _config;
    public ISBLogger(ISptLogger<ISBLogger> logger, ConfigController config)
    {
        _logger = logger;
        _config = config;
    }

    public bool Enabled => _config.ModConfig.debug?.logs == true;

    public void Info(string message)
    {
        if (Enabled)
            _logger.Info($"[ISB SOF] {message}");
    }

    public void Warn(string message) => _logger.Warning($"[ISB SOF] WARNING: {message}");
    public void Diagnostic(string message)
    {
        if (ISBDiagnostics.Enabled)
            _logger.Info($"[ISB SOF] {message}");
    }

    public void Error(string message)
    {
        _logger.Error($"[ISB SOF] ERROR: {message}");
    }
}
