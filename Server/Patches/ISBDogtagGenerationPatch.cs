using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Generators.Bot;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Spt.Bots;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class ISBDogtagGenerationPatch : IOnLoad
{
    public const string HarmonyId = "com.samc137.isbspecialforces.dogtags.server";
    private static ConfigController _configController;
    private static TemplateTable _templateTable;
    private static ILogger<ISBDogtagGenerationPatch> _logger;
    private static readonly HashSet<string> ReportedInvalidTemplates = new(StringComparer.OrdinalIgnoreCase);
    public ISBDogtagGenerationPatch(ConfigController configController, TemplateTable templateTable, ILogger<ISBDogtagGenerationPatch> logger)
    {
        _configController = configController;
        _templateTable = templateTable;
        _logger = logger;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot));
        if (target is null)
        {
            _logger.LogWarning("[ISB DOGTAG] BotGenerator.PrepareAndGenerateBot was not found. ISB dogtags are disabled.");
            return Task.CompletedTask;
        }

        var postfix = new HarmonyMethod(AccessTools.Method(typeof(ISBDogtagGenerationPatch), nameof(Postfix)))
        {
            priority = Priority.Last,
            after = [ISBBotPresetGenerationPatch.HarmonyId]
        };
        var harmony = new Harmony(HarmonyId);
        harmony.Patch(target, postfix: postfix);
        return Task.CompletedTask;
    }

    private static void Postfix(BotBase __result, BotGenerationDetails botGenerationDetails)
    {
        var bot = __result;
        var config = _configController?.ModConfig?.dogtags;
        if (bot is null || config is null || !config.enabled || GhostBotGenerationPatch.IsRequest(botGenerationDetails))
            return;
        var role = bot.Info?.Settings?.Role;
        if (!config.TryGetTemplate(role, out var templateId))
            return;
        if (bot.Inventory?.Items is null || bot.Inventory.Equipment is null)
            return;
        MongoId template;
        try
        {
            template = new MongoId(templateId);
        }
        catch (Exception)
        {
            ReportInvalidTemplate(role, templateId, "is not a valid 24-character MongoId");
            return;
        }

        var templateItems = _templateTable?.Items;
        if (templateItems is null || !templateItems.ContainsKey(template))
        {
            ReportInvalidTemplate(role, templateId, "does not exist in the item database");
            return;
        }

        bot.Inventory.Items.RemoveAll(item => string.Equals(item.SlotId, "Dogtag", StringComparison.OrdinalIgnoreCase));
        var upd = new Upd
        {
            SpawnedInSession = true
        };
        if (config.populateIdentityData)
        {
            upd.Dogtag = new UpdDogtag
            {
                AccountId = bot.Aid?.ToString(),
                ProfileId = bot.Id?.ToString(),
                Nickname = bot.Info?.Nickname,
                Side = ParseInternalSide(config.internalSide),
                Level = ISB.Compatibility.ISBDogtagLevels.Choose(templateId, bot.Id?.ToString(), bot.Info?.Level),
                CarriedByGroupMember = false
            };
        }

        bot.Inventory.Items.Add(new Item { Id = new MongoId(), Template = template, ParentId = bot.Inventory.Equipment.Value.ToString(), SlotId = "Dogtag", Upd = upd });
    }

    private static DogtagSide ParseInternalSide(string value)
    {
        if (Enum.TryParse(value, true, out DogtagSide side))
            return side;
        return DogtagSide.Usec;
    }

    private static void ReportInvalidTemplate(string role, string templateId, string reason)
    {
        var key = $"{role}:{templateId}:{reason}";
        if (!ReportedInvalidTemplates.Add(key))
            return;
        _logger.LogWarning($"[ISB DOGTAG] '{role}' dogtag TPL '{templateId}' {reason}. The bot dogtag was left unchanged.");
    }
}
