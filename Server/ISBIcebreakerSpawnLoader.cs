using ISBSpecialForcesServer.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace ISBSpecialForces;
[Injectable(InjectionType = InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 90010)]
public sealed class ISBIcebreakerSpawnLoader(ISBSpawnController spawnController) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        spawnController.AdjustIcebreakerSpawns();
        return Task.CompletedTask;
    }
}
