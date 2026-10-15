using System.Text.Json;
using MoreBotsServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace ISBSpecialForces;
[Injectable(TypePriority = (OnLoadOrder.PreSptModLoader + 80085) + 1)]
public sealed class ISBBotTypeRegistration(MoreBotsCustomBotTypeService customBotTypeService) : IOnLoad
{
    public Task OnLoad()
    {
        using var stream = typeof(ISBBotTypeRegistration).Assembly.GetManifestResourceStream("ISBSpecialForces.ISBBotTypes.json") ?? throw new InvalidOperationException("ISB bot type metadata is missing.");
        var names = JsonSerializer.Deserialize<Dictionary<int, string>>(stream) ?? throw new InvalidOperationException("ISB bot type metadata is empty.");
        customBotTypeService.AddCustomWildSpawnTypeNames(names.ToDictionary(pair => pair.Key, pair => pair.Value.ToLowerInvariant()));
        return Task.CompletedTask;
    }
}
