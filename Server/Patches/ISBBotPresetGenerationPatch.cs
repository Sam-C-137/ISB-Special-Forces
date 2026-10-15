using SPTarkov.Server.Core.Models.Spt.Bots;
using ISB.Compatibility;
using SPTarkov.Server.Core.Generators;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 5)]
public class ISBBotPresetGenerationPatch : IOnLoad
{
    public const string HarmonyId = "com.samc137.isbspecialforces.botpresets.server";
    private static ISBBotPresetController _controller = null!;
    private static ILogger<ISBBotPresetGenerationPatch> _logger = null!;
    public ISBBotPresetGenerationPatch(ISBBotPresetController controller, ILogger<ISBBotPresetGenerationPatch> logger)
    {
        _controller = controller;
        _logger = logger;
    }

    public Task OnLoad()
    {
        _controller.LoadAllPresets();
        var target = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot));
        if (target is null)
        {
            _logger.LogError("[ISB BOT PRESETS] BotGenerator.PrepareAndGenerateBot was not found. Bot presets are disabled.");
            return Task.CompletedTask;
        }

        var postfix = new HarmonyMethod(AccessTools.Method(typeof(ISBBotPresetGenerationPatch), nameof(Postfix)))
        {
            priority = Priority.Last,
            before = [ISBDogtagGenerationPatch.HarmonyId]
        };
        new Harmony(HarmonyId).Patch(target, postfix: postfix);
        return Task.CompletedTask;
    }

    private static void Postfix(BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        var ghostRole = GhostBotGenerationPatch.RequestedRole(botGenerationDetails);
        if (ghostRole != null)
        {
            if (!_controller.TryApplyPreset(__result, ghostRole))
                throw new InvalidOperationException($"ISB: {ghostRole} needs a valid preset in db/BlackDivisionBotPresets/{ghostRole}/Day or Night for the current raid time.");
            return;
        }

        if (string.Equals(__result?.Info?.Settings?.Role, GhostBotVariant.BaseName, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            _controller.TryApplyPreset(__result);
        }
        catch (Exception)
        {
        }
    }
}
