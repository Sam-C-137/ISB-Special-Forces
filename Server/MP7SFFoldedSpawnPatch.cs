using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public sealed class MP7SFFoldedSpawnPatch : IOnLoad
{
    internal const string Template = "6a4d24532666085a03d7b1e3";
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var target = AccessTools.GetDeclaredMethods(typeof(BotWeaponGenerator)).Single(m => m.Name == "GenerateWeaponByTpl" && m.ReturnType == typeof(GenerateWeaponResult));
        new Harmony("com.samc137.isb.mp7sf.foldedspawn").Patch(target, postfix: new HarmonyMethod(typeof(MP7SFFoldedSpawnPatch), nameof(Postfix)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static void Postfix(GenerateWeaponResult? __result)
    {
        if (__result?.Weapon == null)
            return;
        foreach (var item in __result.Weapon)
        {
            if (item.Template.ToString() != Template)
                continue;
            item.Upd ??= new Upd();
            item.Upd.Foldable ??= new UpdFoldable();
            item.Upd.Foldable.Folded = true;
        }
    }
}
