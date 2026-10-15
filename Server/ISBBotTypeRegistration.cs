using System.Text.Json;
using MoreBotsServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ISBSpecialForces;
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class ISBBotTypeRegistration(MoreBotsCustomBotTypeService customBotTypeService, ClientEnumDefinitions clientEnumDefinitions) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = typeof(ISBBotTypeRegistration).Assembly.GetManifestResourceStream("ISBSpecialForces.ISBBotTypes.json") ?? throw new InvalidOperationException("ISB bot type metadata is missing.");
        var names = JsonSerializer.Deserialize<Dictionary<int, string>>(stream) ?? throw new InvalidOperationException("ISB bot type metadata is empty.");
        customBotTypeService.AddCustomWildSpawnTypeNames(names.ToDictionary(pair => pair.Key, pair => pair.Value.ToLowerInvariant()));
        clientEnumDefinitions.AddRange(new ModMetadata().ModGuid, names.Select(pair => new EnumEntryDefinition { EnumType = "EFT.WildSpawnType", ConstantName = pair.Value, ConstantValue = pair.Key, JsonEnumName = pair.Value.ToLowerInvariant() }));
        clientEnumDefinitions.Add(new ModMetadata().ModGuid, new EnumEntryDefinition { EnumType = "EFT.Counters.EFenceStandingSource", ConstantName = "ISBKill", ConstantValue = 1000 });
        return Task.CompletedTask;
    }
}
