using ISBSpecialForcesServer.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace ISBSpecialForces;
[Injectable(InjectionType = InjectionType.Singleton, TypePriority = OnLoadOrder.PostSptModLoader + 90010)]
public sealed class ISBIcebreakerSpawnLoader(ISBSpawnController spawnController) : IOnLoad
{
    public Task OnLoad()
    {
        spawnController.AdjustIcebreakerSpawns();
        return Task.CompletedTask;
    }
}
