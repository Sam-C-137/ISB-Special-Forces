using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Generators;
using System.Threading;
using HarmonyLib;
using ISB.MixedAmmo;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader)]
public class MixedAmmoPatch : IOnLoad
{
    private static ILogger<MixedAmmoPatch> _logger;
    private static readonly HashSet<string> ISBRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISBSpecialForces",
        "ISBBossFirefly",
        "ISBFollowerVipper",
        "ISBFollowerLoghan",
        "ISBFollowerSpearthrower",
        "ISBBossCommander",
        "ISBFollowerCommander",
        "ISBTeamLeader",
        "ISBSecondLeader",
        "ISBScout",
        "ISBHeavy",
        "ISBFollowerZofia",
        "ISBAssassin",
        "ISBMarksman",
        "ISBFollowerEvernight",
        "ISBFollowerNemesis"
    };
    private static readonly AsyncLocal<string?> CurrentRole = new();
    private static readonly AsyncLocal<string?> CurrentBotKey = new();
    public MixedAmmoPatch(ILogger<MixedAmmoPatch> logger)
    {
        _logger = logger;
    }

    public Task OnLoad()
    {
        var harmony = new Harmony("com.samc137.isbspecialforces.mixedammo");
        var weaponGen = AccessTools.Method(typeof(BotWeaponGenerator), "GenerateWeaponByTpl");
        var extraMags = AccessTools.Method(typeof(BotWeaponGenerator), "AddExtraMagazinesToInventory");
        var fillMag = AccessTools.Method(typeof(ItemHelper), "FillMagazineWithCartridge");
        if (weaponGen is null || extraMags is null || fillMag is null)
        {
            return Task.CompletedTask;
        }

        harmony.Patch(weaponGen, prefix: new HarmonyMethod(AccessTools.Method(typeof(MixedAmmoPatch), nameof(WeaponGenPrefix))));
        harmony.Patch(extraMags, prefix: new HarmonyMethod(AccessTools.Method(typeof(MixedAmmoPatch), nameof(ExtraMagsPrefix))), postfix: new HarmonyMethod(AccessTools.Method(typeof(MixedAmmoPatch), nameof(ExtraMagsPostfix))));
        harmony.Patch(fillMag, postfix: new HarmonyMethod(AccessTools.Method(typeof(MixedAmmoPatch), nameof(FillMagazinePostfix))));
        return Task.CompletedTask;
    }

    private static void WeaponGenPrefix(MongoId weaponParentId, BotGenerationDetails botGenerationDetails)
    {
        var role = botGenerationDetails?.RoleLowercase;
        if (role is not null && ISBRoles.Contains(role))
        {
            CurrentRole.Value = role;
            CurrentBotKey.Value = weaponParentId.ToString();
        }
        else
        {
            CurrentRole.Value = null;
            CurrentBotKey.Value = null;
        }
    }

    private static void ExtraMagsPrefix(BotBaseInventory inventory, string botRole)
    {
        if (botRole is not null && ISBRoles.Contains(botRole))
        {
            CurrentRole.Value = botRole;
            CurrentBotKey.Value = inventory?.Equipment?.ToString() ?? botRole;
        }
        else
        {
            CurrentRole.Value = null;
            CurrentBotKey.Value = null;
        }
    }

    private static void ExtraMagsPostfix()
    {
        CurrentRole.Value = null;
        CurrentBotKey.Value = null;
    }

    private static void FillMagazinePostfix(ItemHelper __instance, List<Item> magazineWithChildCartridges, TemplateItem magTemplate, MongoId cartridgeTpl, double minSizeMultiplier)
    {
        if (minSizeMultiplier < 1)
        {
            return;
        }

        var role = CurrentRole.Value;
        if (role is null)
        {
            return;
        }

        if (magazineWithChildCartridges is null || magazineWithChildCartridges.Count < 2)
        {
            return;
        }

        if (magTemplate?.Properties?.Cartridges?.FirstOrDefault()?.MaxCount is null)
        {
            return;
        }

        var caliber = __instance.GetItem(cartridgeTpl).Value?.Properties?.Caliber;
        if (string.IsNullOrEmpty(caliber))
        {
            return;
        }

        if (!ISBMixedAmmoState.TryGet(role, caliber, out var mix))
        {
            return;
        }

        var botKey = CurrentBotKey.Value ?? role;
        if (!ISBMixedAmmoState.RollForBot(botKey, caliber, mix.chance))
        {
            return;
        }

        var magazineId = magazineWithChildCartridges[0].Id;
        var filled = 0;
        for (var i = magazineWithChildCartridges.Count - 1; i >= 1; i--)
        {
            var item = magazineWithChildCartridges[i];
            if (item.ParentId is not null && magazineId.Equals(item.ParentId) && string.Equals(item.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase))
            {
                filled += (int)(item.Upd?.StackObjectsCount ?? 0);
                magazineWithChildCartridges.RemoveAt(i);
            }
        }

        if (filled <= 0)
        {
            return;
        }

        var stacks = MixedAmmoStackBuilder.MagCustomStackVV(magazineId, filled, mix);
        magazineWithChildCartridges.AddRange(stacks);
    }
}
