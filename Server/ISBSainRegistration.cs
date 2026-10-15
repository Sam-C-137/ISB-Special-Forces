using System.Text.Json;
using MoreBotsServer.Interop;
using MoreBotsServer.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace ISBSpecialForces;
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 2)]
public sealed class ISBSainRegistration(SainInteropRegistration moreBots) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = typeof(ISBSainRegistration).Assembly.GetManifestResourceStream("ISBSpecialForces.ISBSainRoles.json") ?? throw new InvalidOperationException("ISB SAIN role metadata is missing.");
        var definitions = JsonSerializer.Deserialize<MoreBotsSainBotTypeRegistration[]>(stream) ?? throw new InvalidOperationException("ISB SAIN role metadata is empty.");
        foreach (var role in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            moreBots.RegisterBotType(role);
        }

        return Task.CompletedTask;
    }
}
