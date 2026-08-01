using ISBSpecialForcesServer;
using ISBSpecialForcesServer.Controllers;
using ISBSpecialForces.Services;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils;
using System.Linq;
using System.Reflection;
using MoreBotsServer.Models;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Services;
using WTTServerCommonLib;
using WTTServerCommonLib.Services;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace ISBSpecialForces;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.samc137.isbspecialforces";
    public override string Name { get; init; } = "ISB Special Forces";
    public override string Author { get; init; } = "SamC137";
    public override List<string>? Contributors { get; init; } = new() { };
    public override SemanticVersioning.Version Version { get; init; } = new(1, 0, 0);
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        { "com.morebotsapi.tacticaltoaster", new SemanticVersioning.Range(">=2.0.1") },
        { "com.wtt.commonlib", new SemanticVersioning.Range(">=2.0.23") },
        { "com.wtt.contentbackport", new SemanticVersioning.Range(">=1.1.0") },
        { "com.samc137.aishi", new SemanticVersioning.Range(">=1.0.0") }
    };
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 80085)]
public class ISBSpecialForces : IOnLoad
{
    public static MainConfig ModConfig = new();

    private readonly ModHelper _modHelper;

    public ISBSpecialForces(ModHelper modHelper)
    {
        _modHelper = modHelper;
    }

    Task IOnLoad.OnLoad()
    {
        var pathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        ModConfig = _modHelper.GetJsonDataFromFile<MainConfig>(pathToMod, "config.jsonc");
        return Task.CompletedTask;
    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadBots)]
public class ISBSpecialForces2(
    MoreBotsServer.MoreBotsAPI moreBotsLib,
    MoreBotsServer.Services.FactionService factionService,
    MoreBotsServer.Services.MoreBotsCustomBotTypeService customBotTypeService,
    MoreBotsServer.Services.LoadoutService loadoutService,
    WTTServerCommonLib.WTTServerCommonLib commonLib,
    IReadOnlyList<SptMod> modList,
    ISBSpawnController ISBSpawnController,
    CustomBotLoadoutItemsService customBotLoadoutItemsService,
    ILogger<ISBSpecialForces2> logger
) : IOnLoad
{
    public async Task OnLoad()
    {
        var typeList = new List<string> {
            "ISBSpecialForces",
            "ISBBossCommander",
            "ISBFollowerCommander",
            "ISBTeamLeader",
            "ISBSecondLeader"
        };

        var assembly = Assembly.GetExecutingAssembly();

        await moreBotsLib.LoadBotsShared(assembly, "ISBSpecialForces", typeList);

        await loadoutService.LoadLoadoutsWithTemplate(assembly, "ISBSpecialForces");

        await customBotTypeService.LoadBotTypeReplace(Assembly.GetExecutingAssembly(), "ISBSpecialForces", typeList);

        await customBotTypeService.LoadBotTypeReplaceByTypes(Assembly.GetExecutingAssembly(), typeList);

        factionService.AddEnemyByFaction(typeList, "savage");
        factionService.AddEnemyByFaction(typeList, "usec");
        factionService.AddEnemyByFaction(typeList, "bear");
        factionService.AddEnemyByFaction(typeList, "cultists");
        factionService.AddEnemyByFaction(typeList, "infected");

        factionService.AddEnemyByFaction("savage", "ISB");
        factionService.AddEnemyByFaction("usec", "ISB");
        factionService.AddEnemyByFaction("bear", "ISB");
        factionService.AddEnemyByFaction("cultists", "ISB");
        factionService.AddEnemyByFaction("infected", "ISB");

        factionService.AddFriendlyByFaction("rogues", "ISB");
        factionService.AddFriendlyByFaction(typeList, "rogues");

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.blackdiv.tacticaltoaster"))
        {
            factionService.AddEnemyByFaction(typeList, "blackdiv");
            factionService.AddEnemyByFaction("blackdiv", "ISB");
        }

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.ruafcomehome.tacticaltoaster"))
        {
            factionService.AddFriendlyByFaction(typeList, "ruaf");
            factionService.AddFriendlyByFaction("ruaf", "ISB");
        }

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.ruafcomehome.tacticaltoaster"))
        {
            factionService.AddFriendlyByFaction(typeList, "remnant");
            factionService.AddFriendlyByFaction("remnant", "ISB");
        }

        await commonLib.CustomLocaleService.CreateCustomLocales(Assembly.GetExecutingAssembly());

        await commonLib.CustomAchievementService.CreateCustomAchievements(assembly);

        var specialLoadoutMods = new List<(string Guid, string DisplayName, string LoadoutFolder)>
        {
            ("com.wtt.armory",                      "WTT - Armory",                 "ArmoryLoadouts"),
            ("com.epicrangetime.aio",               "Epic's All in One",            "AllinOneLoadouts"),
            ("com.wtt.ecoattachmentemporium",       "Eco's Attachment Emporium",    "ECOLoadouts"),
            ("com.c11.truenorth4",                  "C11 - True North",             "TrueNorth-Loadouts"),
            ("com.massivesoft.massivesoftweapons",  "MassiveSoftWeapons",           "MassivesoftWeaponsLoadouts"),
            ("com.spy.jsoc-swr",                    "Spy's JSOC-SWR",               "KS-1Loadouts"),
            ("com.crackbone.artem-wtt",             "WTT - Artem",                  "ArtemLoadouts"),
            ("com.salco.salcosarmory",              "SALCO's ARMORY",               "SalcoIndustriesLoadouts"),
            ("com.emilanderss0n.tgc",               "Tactical Gear Component",      "TGCLoadouts"),
            ("com.manimal.csgas",                   "Manimal's CS Gas",             "ManimalGasLoadouts"),
        };

        var detectedSpecialLoadoutMods = specialLoadoutMods
            .Where(m => modList.Any(mod => mod.ModMetadata.ModGuid == m.Guid))
            .ToList();

        if (detectedSpecialLoadoutMods.Count > 0)
        {
            var namesJoined = string.Join("\", \"", detectedSpecialLoadoutMods.Select(m => m.DisplayName));

            logger.LogInformation(
                $"\x1b[38;2;150;60;220m[ISB SOF] Looks like we have some really cool mods installed on the server. Let's see. Hm...\"{namesJoined}\" " +
                $"{(detectedSpecialLoadoutMods.Count == 1 ? "is" : "are")} installed. Aishi and ISB are going to pick up a few things from them. " +
                $"From now on, all ISB operators will be able to equip special items from these mods. Good luck! ÒwÓ\x1b[0m"
            );

            foreach (var mod in detectedSpecialLoadoutMods)
            {
                var relativeLoadoutDirectory = Path.Join("db", "ExtraLoadouts", mod.LoadoutFolder);

                await commonLib.CustomBotLoadoutService.CreateCustomBotLoadouts(assembly,
                    relativeLoadoutDirectory);

                customBotLoadoutItemsService.MergeItems(
                    assembly,
                    relativeLoadoutDirectory,
                    mod.DisplayName);
            }
        }

        ISBSpawnController.AdjustAllISBSpawns();

        logger.LogInformation("\x1b[38;2;200;80;220m[ISB SOF Loaded] \"Control, this is Snake Two. All personnel are ready. The game to hunt some scorpions is about to begin.\"\x1b[0m");
    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadFactions)]
public class ISBSpecialForcesLoadFaction(
    MoreBotsServer.Services.FactionService factionService
) : IOnLoad
{
    public async Task OnLoad()
    {
        factionService.Factions.Add("ISB", new Faction()
        {
            Name = "ISB",
            BotTypes =
            {
                (WildSpawnType) 13701,
                (WildSpawnType) 13707,
                (WildSpawnType) 13708,
                (WildSpawnType) 13709,
                (WildSpawnType) 13710,
            }
        });

        await Task.CompletedTask;
    }
}

[Injectable]
public class CustomDynamicRouter : DynamicRouter
{
    private static HttpResponseUtil _httpResponseUtil;
    private static ConfigController _configController;
    private static AishiLoyaltyConditionController _aishiLoyaltyConditionController;
    private static ISBRevengeController _isbRevengeController;

    public CustomDynamicRouter(
        JsonUtil jsonUtil,
        HttpResponseUtil httpResponseUtil,
        ConfigController configController,
        AishiLoyaltyConditionController aishiLoyaltyConditionController,
        ISBRevengeController isbRevengeController) : base(jsonUtil, GetCustomRoutes())
    {
        _httpResponseUtil = httpResponseUtil;
        _configController = configController;
        _aishiLoyaltyConditionController = aishiLoyaltyConditionController;
        _isbRevengeController = isbRevengeController;
    }

    private static List<RouteAction> GetCustomRoutes()
    {
        return [
            new RouteAction(
                "/ISB/checkpoints",
                async (url, info, sessionID, output) =>
                {
                    var result = _configController.ModConfig;
                    return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
                }
            ),

            new RouteAction(
                "/ISB/aishiLoyaltyCondition",
                async (url, info, sessionID, output) =>
                {
                    var result = _aishiLoyaltyConditionController.BuildResponse(sessionID);
                    return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
                }
            ),

            new RouteAction(
                "/ISB/forcedHostility",
                async (url, info, sessionID, output) =>
                {
                    var result = _configController.ModConfig.forcedHostility ?? new ForcedHostilityConfig();
                    return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
                }
            ),

            new RouteAction(
                "/ISB/revenge/status",
                async (url, info, sessionID, output) =>
                {
                    var result = _isbRevengeController.GetStatus(sessionID);
                    return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
                }
            )
        ];
    }
}

[Injectable(TypePriority = -100)]
public class CustomStaticRouter : StaticRouter
{
    private static HttpResponseUtil _httpResponseUtil;
    private static ISBSpawnController _ISBSpawnController;
    private static AishiLoyaltyConditionController _aishiLoyaltyConditionController;
    private static ISBRevengeController _isbRevengeController;
    private static JsonUtil _jsonUtil;

    public CustomStaticRouter(
        ISBSpawnController ISBSpawnController,
        AishiLoyaltyConditionController aishiLoyaltyConditionController,
        ISBRevengeController isbRevengeController,
        JsonUtil jsonUtil,
        HttpResponseUtil httpResponseUtil) : base(jsonUtil, GetCustomRoutes())
    {
        _httpResponseUtil = httpResponseUtil;
        _ISBSpawnController = ISBSpawnController;
        _aishiLoyaltyConditionController = aishiLoyaltyConditionController;
        _isbRevengeController = isbRevengeController;
        _jsonUtil = jsonUtil;
    }

    private static List<RouteAction> GetCustomRoutes()
    {
        return
        [

            new RouteAction<GetRaidConfigurationRequestData>(
                "/client/raid/configuration",
                async (url, info, sessionID, output) =>
                {
                    _ISBSpawnController.SetRaidHourFromConfig(info);
                    return await new ValueTask<string>(output ?? string.Empty);
                }
            ),

            new RouteAction(
                "/client/match/local/end",
                async (url, info, sessionID, output) =>
                {
                    _aishiLoyaltyConditionController.PrepareRaidEnd(sessionID);
                    _isbRevengeController.EndRaid(sessionID);
                    _ISBSpawnController.AdjustAllISBSpawns();
                    return await new ValueTask<object>(output ?? string.Empty);
                }
            ),

            new RouteAction<ISBRevengeRaidStartRequest>(
                "/ISB/revenge/raid/start",
                async (url, info, sessionID, output) =>
                {
                    _isbRevengeController.BeginRaid(sessionID, info?.isScavRaid ?? false);
                    return await new ValueTask<string>(_httpResponseUtil.NullResponse());
                }
            ),

            new RouteAction<ISBRevengeRegisterRequest>(
                "/ISB/revenge/register",
                async (url, info, sessionID, output) =>
                {
                    _isbRevengeController.Register(sessionID, info);
                    return await new ValueTask<string>(_httpResponseUtil.NullResponse());
                }
            ),

            new RouteAction<ISBAishiRaidStartRequest>(
                "/ISB/aishi/raid/start",
                async (url, info, sessionID, output) =>
                {
                    _aishiLoyaltyConditionController.BeginRaid(sessionID);
                    return await new ValueTask<string>(_httpResponseUtil.NullResponse());
                }
            ),

            new RouteAction<ISBAishiBetrayalRequest>(
                "/ISB/aishi/betrayal",
                async (url, info, sessionID, output) =>
                {
                    var result = _aishiLoyaltyConditionController.ApplyBetrayalPenalty(sessionID, info);
                    return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
                }
            ),

            new RouteAction<ISBAishiMailRequest>(
                "/ISB/aishimail",
                async (url, info, sessionID, output) =>
                {
                    _aishiLoyaltyConditionController.SendAishiMessage(sessionID, info?.isbKills ?? 0);
                    return await new ValueTask<string>(_httpResponseUtil.NullResponse());
                }
            )
        ];
    }
}
