using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services;

namespace ISBSpecialForcesServer.Patches;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public class ISBDogtagGenerationPatch : IOnLoad
{
    private static ConfigController _configController;
    private static DatabaseService _databaseService;
    private static ILogger<ISBDogtagGenerationPatch> _logger;
    private static readonly HashSet<string> ReportedInvalidTemplates = new(StringComparer.OrdinalIgnoreCase);


    public ISBDogtagGenerationPatch(
        ConfigController configController,
        DatabaseService databaseService,
        ILogger<ISBDogtagGenerationPatch> logger)
    {
        _configController = configController;
        _databaseService = databaseService;
        _logger = logger;
    }

    public Task OnLoad()
    {
        var botGeneratorType = AccessTools.TypeByName("SPTarkov.Server.Core.Generators.Bot.BotGenerator")
            ?? AccessTools.TypeByName("SPTarkov.Server.Core.Generators.BotGenerator");

        var target = botGeneratorType is null
            ? null
            : AccessTools.Method(botGeneratorType, "PrepareAndGenerateBot");

        if (target is null)
        {
            _logger.LogWarning("[ISB DOGTAG] BotGenerator.PrepareAndGenerateBot was not found. ISB dogtags are disabled.");
            return Task.CompletedTask;
        }

        var harmony = new Harmony("com.samc137.isbspecialforces.dogtags.server");
        harmony.Patch(
            target,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(ISBDogtagGenerationPatch), nameof(Postfix))));

        return Task.CompletedTask;
    }

    private static void Postfix(BotBase __result)
    {
        var bot = __result;
        var config = _configController?.ModConfig?.dogtags;

        if (bot is null || config is null || !config.enabled)
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

        var templateItems = _databaseService?.GetTables()?.Templates?.Items;

        if (templateItems is null || !templateItems.ContainsKey(template))
        {
            ReportInvalidTemplate(role, templateId, "does not exist in the item database");
            return;
        }

        bot.Inventory.Items.RemoveAll(item =>
            string.Equals(item.SlotId, "Dogtag", StringComparison.OrdinalIgnoreCase));

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
                Level = GetDogtagLevel(role),
                CarriedByGroupMember = false
            };
        }

        bot.Inventory.Items.Add(new Item
        {
            Id = new MongoId(),
            Template = template,
            ParentId = bot.Inventory.Equipment.Value.ToString(),
            SlotId = "Dogtag",
            Upd = upd
        });
    }

    private static int GetDogtagLevel(string role)
    {
        if (string.Equals(role, "13701", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ISBSpecialForces", StringComparison.OrdinalIgnoreCase))
            return Random.Shared.Next(45, 61);

        if (string.Equals(role, "13707", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ISBBossCommander", StringComparison.OrdinalIgnoreCase))
            return Random.Shared.Next(70, 80);

        if (string.Equals(role, "13708", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ISBFollowerCommander", StringComparison.OrdinalIgnoreCase))
            return Random.Shared.Next(70, 80);

        if (string.Equals(role, "13709", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ISBTeamLeader", StringComparison.OrdinalIgnoreCase))
            return Random.Shared.Next(60, 76);

        if (string.Equals(role, "13710", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ISBSecondLeader", StringComparison.OrdinalIgnoreCase))
            return Random.Shared.Next(50, 66);

        return 1;
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
