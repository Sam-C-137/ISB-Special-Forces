using SPTarkov.Server.Core.Helpers.Server;
using ISBSpecialForcesServer;
using ISBSpecialForcesServer.Controllers;
using ISBSpecialForces.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Reflection;
using MoreBotsServer.Models;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Services;
using WTTServerCommonLib;
using WTTServerCommonLib.Services;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace ISBSpecialForces;
public record ModMetadata : IModMetadata, SPTarkov.Server.Web.IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.samc137.isbspecialforces";
    public string Name { get; init; } = "ISB Special Forces";
    public string Author { get; init; } = "SamC137";
    public List<string>? Contributors { get; init; } = new()
    {
    };
    public SemanticVersioning.Version Version { get; init; } = new(2, 0, 1);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        {
            "com.morebotsapi.tacticaltoaster",
            new SemanticVersioning.Range("~2.1.1")
        },
        {
            "com.wtt.commonlib",
            new SemanticVersioning.Range(">=2.0.24")
        },
        {
            "com.wtt.contentbackport",
            new SemanticVersioning.Range(">=1.1.5")
        },
        {
            "com.samc137.aishi",
            new SemanticVersioning.Range(">=1.0.4")
        }
    };
    public string? Url { get; init; }
    public bool HasPrepatcher { get; init; } = true;
    public string License { get; init; } = "MIT";
    public string? WWWRootUrl { get; init; } = "isb-assets";
#if ISB_FINAL_BETA
    public string? HomePage { get; init; } = null;
#else
    public string? HomePage { get; init; } = "/isb";
#endif
    public string? HomePageDescription { get; init; } = "Configuration for Spawns, Formations, Blackout and Containement Breach Events, Dogtags and others";
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public class ISBSpecialForces : IOnLoad
{
    public static MainConfig ModConfig = new();
    private readonly ModHelper _modHelper;
    public ISBSpecialForces(ModHelper modHelper)
    {
        _modHelper = modHelper;
    }

    Task IOnLoad.OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        ModConfig = _modHelper.GetJsonDataFromFile<MainConfig>(pathToMod, "config.jsonc");
        ModConfig.MigrateLegacyIcebreakerLocation();
        return Task.CompletedTask;
    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadBots)]
public class ISBBotRegistration(MoreBotsServer.MoreBotsAPI moreBotsLib, MoreBotsServer.Services.FactionService factionService, MoreBotsServer.Services.MoreBotsCustomBotTypeService customBotTypeService, MoreBotsServer.Services.LoadoutService loadoutService, SPTarkov.Server.Core.Models.Spt.Config.BotConfig botConfig, ModHelper modHelper, IReadOnlyList<SptMod> modList) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var typeList = new List<string>
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
        var assembly = Assembly.GetExecutingAssembly();
        await moreBotsLib.LoadBotsShared(assembly, "ISBSpecialForces", typeList);
        var maxBatch = ISBSpecialForces.ModConfig.maxBotPresetBatch;
        if (maxBatch < 1 || maxBatch > 150)
        {
            maxBatch = 25;
        }

        var configFolder = Path.Combine(modHelper.GetAbsolutePathToModFolder(assembly), "db", "bots", "sharedConfig");
        foreach (var role in typeList)
        {
            var count = botConfig.PresetBatch.TryGetValue(role, out var sharedCount) ? sharedCount : maxBatch;
            var roleConfig = Path.Combine(configFolder, role + ".json");
            try
            {
                if (File.Exists(roleConfig))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(roleConfig), new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (document.RootElement.TryGetProperty("presetBatch", out var value) && value.TryGetInt32(out var roleCount) && roleCount > 0)
                        count = roleCount;
                }
            }
            catch (Exception ex)when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
            {
            }

            count = Math.Clamp(count, 1, maxBatch);
            botConfig.PresetBatch[role] = count;
            botConfig.PresetBatch[role.ToLowerInvariant()] = count;
        }

        var loadoutDirectory = Path.Combine(modHelper.GetAbsolutePathToModFolder(assembly), "db", "bots", "loadouts");
        if (Directory.Exists(loadoutDirectory) && Directory.EnumerateFiles(loadoutDirectory, "*.json", SearchOption.AllDirectories).Any())
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
    }
}

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class ISBSpecialForces2(SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable templateTable, ModHelper modHelper, WTTServerCommonLib.WTTServerCommonLib commonLib, IReadOnlyList<SptMod> modList, ISBSpawnController ISBSpawnController, ConfigController configController, CustomBotLoadoutItemsService customBotLoadoutItemsService, ISBLogger startupLog, ILogger<ISBSpecialForces2> logger) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = Assembly.GetExecutingAssembly();
        await commonLib.CustomLocaleService.CreateCustomLocales(assembly);
        await commonLib.CustomQuestService.CreateCustomQuests(assembly);
        await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly);
        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.ruafcomehome.tacticaltoaster"))
        {
            await commonLib.CustomQuestService.CreateCustomQuests(assembly, Path.Join("db", "ISBAndRuaf", "Quests"));
            await commonLib.CustomHideoutRecipeService.CreateHideoutRecipes(assembly, Path.Join("db", "ISBAndRuaf", "Recipes"));
            await commonLib.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("db", "ISBAndRuaf", "Itens"));
            await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, Path.Join("db", "ISBAndRuaf", "Assort"));
        }

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.blackdiv.tacticaltoaster"))
        {
            await commonLib.CustomQuestService.CreateCustomQuests(assembly, Path.Join("db", "ISBAndBlackDiv", "Quests"));
            await commonLib.CustomHideoutRecipeService.CreateHideoutRecipes(assembly, Path.Join("db", "ISBAndBlackDiv", "Recipes"));
            await commonLib.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("db", "ISBAndBlackDiv", "Itens"));
            ContainmentQuestItems.FilterRespirators(templateTable.Quests, id => templateTable.Items.ContainsKey(id));
            await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, Path.Join("db", "ISBAndBlackDiv", "Assort"));
            if (templateTable.Items.ContainsKey("6a5d6a5f4ed8c025a0a2cff0"))
            {
                await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, Path.Join("db", "ISBAndBlackDiv", "GasAssort"));
            }
        }

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.untargh.tacticaltoaster"))
        {
            await commonLib.CustomQuestService.CreateCustomQuests(assembly, Path.Join("db", "ISBAndUntar", "Quests"));
            await commonLib.CustomHideoutRecipeService.CreateHideoutRecipes(assembly, Path.Join("db", "ISBAndUntar", "Recipes"));
            await commonLib.CustomItemServiceExtended.CreateCustomItems(assembly, Path.Join("db", "ISBAndUntar", "Itens"));
            await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, Path.Join("db", "ISBAndUntar", "Assort"));
        }

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.manimal.labsboiler") && templateTable.Quests.ContainsKey("c9304d1a70414b398f840595"))
        {
            await commonLib.CustomQuestService.CreateCustomQuests(assembly, Path.Join("db", "ISBAndManimalLabs", "Quests"));
            await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, Path.Join("db", "ISBAndManimalLabs", "Assort"));
            await commonLib.CustomQuestZoneService.CreateCustomQuestZones(assembly, Path.Join("db", "ISBAndManimalLabs", "Zones"));
        }

        if (modList.Any(mod => string.Equals(mod.ModMetadata.ModGuid, "com.luna.lunnayalunalotus", StringComparison.OrdinalIgnoreCase)) && templateTable.Quests.ContainsKey("c9304d1a70414b398f840595"))
        {
            await commonLib.CustomQuestService.CreateCustomQuests(assembly, Path.Join("db", "ISBAndLotus", "Quests"));
            await commonLib.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, Path.Join("db", "ISBAndLotus", "Assort"));
        }

        await commonLib.CustomAchievementService.CreateCustomAchievements(assembly);
        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.blackdiv.tacticaltoaster"))
            await commonLib.CustomAchievementService.CreateCustomAchievements(assembly, Path.Join("db", "ISBAndBlackDiv", "Achievements"));
        var specialLoadoutMods = new List<(string[][] RequirementGroups, string DisplayName, string LoadoutFolder)>
        {
            (new[] { new[] { "com.wtt.armory" } }, "WTT - Armory", "ArmoryLoadouts"),
            (new[] { new[] { "com.epicrangetime.aio" } }, "Epic's All in One", "AllinOneLoadouts"),
            (new[] { new[] { "com.wtt.ecoattachmentemporium" } }, "Eco's Attachment Emporium", "ECOLoadouts"),
            (new[] { new[] { "com.c11.truenorth4" } }, "C11 - True North", "TrueNorth-Loadouts"),
            (new[] { new[] { "com.massivesoft.massivesoftweapons" } }, "MassiveSoftWeapons", "MassivesoftWeaponsLoadouts"),
            (new[] { new[] { "com.spy.jsoc-swr" } }, "Spy's JSOC-SWR", "KS-1Loadouts"),
            (new[] { new[] { "com.crackbone.artem-wtt" } }, "WTT - Artem", "ArtemLoadouts"),
            (new[] { new[] { "com.emilanderss0n.tgc" } }, "Tactical Gear Component", "TGCLoadouts"),
            (new[] { new[] { "com.manimal.csgas" } }, "Manimal's CS Gas", "ManimalGasLoadouts"),
            (new[] { new[] { "com.wtt.cag" } }, "WTT - Clothing and Gear", "CAGLoadouts"),
            (new[] { new[] { "com.wtt.waac" } }, "WAAC", "WAACSoloLoadouts"),
            (new[] { new[] { "com.wtt.waac", "com.20fpsguy.femalevoice" } }, "WAAC + Female PMC Voice", "WAACLoadouts"),
            (new[] { new[] { "com.wtt.waac", "com.20fpsguy.femalevoice", "dlc.femaleclothes" } }, "WAAC + Female PMC Voice + Female Clothes", "WAACWomLoadouts")
        };
        bool HasInstalledMod(string guid) => modList.Any(mod => string.Equals(mod.ModMetadata.ModGuid, guid, StringComparison.OrdinalIgnoreCase));
        string selectedWaacLoadoutFolder = null;
        if (HasInstalledMod("com.wtt.waac"))
        {
            if (HasInstalledMod("com.20fpsguy.femalevoice") && HasInstalledMod("dlc.femaleclothes"))
                selectedWaacLoadoutFolder = "WAACWomLoadouts";
            else if (HasInstalledMod("com.20fpsguy.femalevoice"))
                selectedWaacLoadoutFolder = "WAACLoadouts";
            else
                selectedWaacLoadoutFolder = "WAACSoloLoadouts";
        }

        var detectedSpecialLoadoutMods = specialLoadoutMods.Where(m => m.RequirementGroups.Any(group => group.All(HasInstalledMod))).Where(m => !m.LoadoutFolder.StartsWith("WAAC", StringComparison.OrdinalIgnoreCase) || string.Equals(m.LoadoutFolder, selectedWaacLoadoutFolder, StringComparison.OrdinalIgnoreCase)).Where(m => configController.ModConfig?.IsCollabLoadoutEnabled(m.LoadoutFolder) == true).Where(m =>
        {
            var directory = Path.Combine(modHelper.GetAbsolutePathToModFolder(assembly), "db", "ExtraLoadouts", m.LoadoutFolder);
            return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase));
        }).ToList();
        if (configController.ModConfig?.enableCollabLoadoutsAndAppearances == true && detectedSpecialLoadoutMods.Count > 0)
        {
            var namesJoined = string.Join("\", \"", detectedSpecialLoadoutMods.Select(m => m.DisplayName));
            logger.LogInformation($"\x1b[38;2;150;60;220m[ISB SOF] Looks like we have some really cool mods installed on the server. Let's see. Hm...\"{namesJoined}\" " + $"{(detectedSpecialLoadoutMods.Count == 1 ? "is" : "are")} installed. Aishi and ISB are going to pick up a few things from them. " + $"From now on, all ISB operators will be able to equip special items from these mods. Good luck! ÒwÓ\x1b[0m");
            foreach (var mod in detectedSpecialLoadoutMods)
            {
                var relativeLoadoutDirectory = Path.Join("db", "ExtraLoadouts", mod.LoadoutFolder);
                await commonLib.CustomBotLoadoutService.CreateCustomBotLoadouts(assembly, relativeLoadoutDirectory);
                customBotLoadoutItemsService.MergeItems(assembly, relativeLoadoutDirectory, mod.DisplayName);
            }
        }

        ISBSpawnController.AdjustAllISBSpawns();
        startupLog.Info($"ContainmentBreach configuration loaded. Office integration: {modList.Any(mod => string.Equals(mod.ModMetadata.ModGuid, "com.manimal.labsboiler", StringComparison.OrdinalIgnoreCase))}; Lotus integration: {modList.Any(mod => string.Equals(mod.ModMetadata.ModGuid, "com.luna.lunnayalunalotus", StringComparison.OrdinalIgnoreCase))}.");
        startupLog.Info("ContainmentBreach map systems ready: facility controls, isolated office readers, per-raid SSD downloads and static loot corpses.");
        logger.LogInformation("\x1b[38;2;200;80;220m[ISB SOF Loaded] \"Control, this is Snake Two. All personnel are ready. The game to hunt some scorpions is about to begin.\"\x1b[0m");
    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadFactions)]
public class ISBSpecialForcesLoadFaction(MoreBotsServer.Services.FactionService factionService) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        factionService.Factions.Add("ISB", new Faction() { Name = "ISB", BotTypes = { (WildSpawnType)13701, (WildSpawnType)13702, (WildSpawnType)13703, (WildSpawnType)13704, (WildSpawnType)13705, (WildSpawnType)13706, (WildSpawnType)13707, (WildSpawnType)13708, (WildSpawnType)13709, (WildSpawnType)13710, (WildSpawnType)13711, (WildSpawnType)13712, (WildSpawnType)13713, (WildSpawnType)13714, (WildSpawnType)13715, (WildSpawnType)13716, } });
        await Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class CustomDynamicRouter : DynamicRouter
{
    private static HttpResponseUtil _httpResponseUtil;
    private static ConfigController _configController;
    private static ISBSpawnController _ISBSpawnController;
    private static AishiLoyaltyConditionController _aishiLoyaltyConditionController;
    private static ISBRevengeController _isbRevengeController;
    private static ISBSpecialForcesServer.Controllers.ISBSpawnProgressController _spawnProgress;
    public CustomDynamicRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, ConfigController configController, ISBSpawnController ISBSpawnController, AishiLoyaltyConditionController aishiLoyaltyConditionController, ISBRevengeController isbRevengeController, ISBSpecialForcesServer.Controllers.ISBSpawnProgressController spawnProgress) : base(jsonUtil, GetCustomRoutes())
    {
        _httpResponseUtil = httpResponseUtil;
        _configController = configController;
        _spawnProgress = spawnProgress;
        _ISBSpawnController = ISBSpawnController;
        _aishiLoyaltyConditionController = aishiLoyaltyConditionController;
        _isbRevengeController = isbRevengeController;
    }

    private static List<RouteAction> GetCustomRoutes()
    {
        return[new RouteAction("/ISB/checkpoints", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _configController.ModConfig;
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        }), new RouteAction("/ISB/labsPreBlackoutEvent/status", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _ISBSpawnController.GetLabsPreBlackoutEventStatus(sessionID);
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        }), new RouteAction("/ISB/ghostbots/names", async (url, info, sessionID, output, cancellationToken) => await new ValueTask<object>(_configController.GetGhostBotNamesJson())), new RouteAction("/ISB/scp173/config", async (url, info, sessionID, output, cancellationToken) => await new ValueTask<object>(_configController.GetScp173ConfigJson(sessionID, _spawnProgress))), new RouteAction("/ISB/containment/chance", async (url, info, sessionID, output, cancellationToken) => await new ValueTask<string>(_httpResponseUtil.NoBody(new { chance = _configController.GetContainmentChance() }))), new RouteAction("/ISB/sainDefaults", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _configController.GetSainDefaultsJson();
            return await new ValueTask<string>(result);
        }), new RouteAction("/ISB/dogtags", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _configController.ModConfig.dogtags ?? new DogtagConfig();
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        }), new RouteAction("/ISB/aishiLoyaltyCondition", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string prefix = "/ISB/aishiLoyaltyCondition/";
            var locationId = url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Uri.UnescapeDataString(url[prefix.Length..].Split('?')[0]) : null;
            var result = _aishiLoyaltyConditionController.BuildResponse(sessionID, locationId);
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        }), new RouteAction("/ISB/forcedHostility", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _configController.ModConfig.forcedHostility ?? new ForcedHostilityConfig();
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        }), new RouteAction("/ISB/revenge/status", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _isbRevengeController.GetStatus(sessionID);
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        })];
    }
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class CustomStaticRouter : StaticRouter
{
    private static HttpResponseUtil _httpResponseUtil;
    private static ISBSpawnController _ISBSpawnController;
    private static AishiLoyaltyConditionController _aishiLoyaltyConditionController;
    private static ISBRevengeController _isbRevengeController;
    private static ISBSpawnProgressController _spawnProgress;
    private static ConfigController _configController;
    private static JsonUtil _jsonUtil;
    public CustomStaticRouter(ISBSpawnController ISBSpawnController, AishiLoyaltyConditionController aishiLoyaltyConditionController, ISBRevengeController isbRevengeController, ConfigController configController, JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, ISBSpawnProgressController spawnProgress) : base(jsonUtil, GetCustomRoutes())
    {
        _httpResponseUtil = httpResponseUtil;
        _spawnProgress = spawnProgress;
        _ISBSpawnController = ISBSpawnController;
        _aishiLoyaltyConditionController = aishiLoyaltyConditionController;
        _isbRevengeController = isbRevengeController;
        _configController = configController;
        _spawnProgress = spawnProgress;
        _jsonUtil = jsonUtil;
    }

    private static List<RouteAction> GetCustomRoutes()
    {
        return[new RouteAction<ContainmentChanceRequest>("/ISB/containment/updateChance", async (url, info, sessionID, output, cancellationToken) => await new ValueTask<string>(_httpResponseUtil.NoBody(new { chance = _configController.SetContainmentChance(info.chance) }))), new RouteAction<ISBRevengeRaidStartRequest>("/ISB/revenge/raid/start", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isbRevengeController.BeginRaid(sessionID, info?.isScavRaid ?? false, info?.locationId, _spawnProgress.CurrentToken(sessionID));
            return await new ValueTask<string>(_httpResponseUtil.NullResponse());
        }), new RouteAction<ISBRevengeRegisterRequest>("/ISB/revenge/register", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isbRevengeController.Register(sessionID, info);
            return await new ValueTask<string>(_httpResponseUtil.NullResponse());
        }), new RouteAction<ISBRevengeKillRequest>("/ISB/revenge/kill", async (url, info, sessionID, output, cancellationToken) =>
        {
            _isbRevengeController.RegisterKill(sessionID, info);
            return await new ValueTask<string>(_httpResponseUtil.NullResponse());
        }), new RouteAction<ISBAishiParticipantsRequest>("/ISB/aishi/participants", async (url, info, sessionID, output, cancellationToken) => await new ValueTask<string>(_httpResponseUtil.NoBody(_aishiLoyaltyConditionController.Participants(sessionID, info)))), new RouteAction<ISBAishiParticipantReport>("/ISB/aishi/participant/report", async (url, info, sessionID, output, cancellationToken) => await new ValueTask<string>(_httpResponseUtil.NoBody(_aishiLoyaltyConditionController.ReportParticipant(sessionID, info)))), new RouteAction<ISBAishiRaidStartRequest>("/ISB/aishi/raid/start", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _aishiLoyaltyConditionController.BeginRaid(sessionID, info?.hasRequiredItem ?? false, info?.isScavRaid ?? false, info?.locationId, _spawnProgress.CurrentToken(sessionID));
            return await new ValueTask<string>(_httpResponseUtil.NullResponse());
        }), new RouteAction<ISBAishiBetrayalRequest>("/ISB/aishi/betrayal", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _aishiLoyaltyConditionController.ApplyBetrayalPenalty(sessionID, info);
            return await new ValueTask<string>(_httpResponseUtil.NoBody(result));
        }), new RouteAction<ISBAishiMailRequest>("/ISB/aishimail", async (url, info, sessionID, output, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _aishiLoyaltyConditionController.SendAishiMessage(sessionID, info?.isbKills ?? 0);
            return await new ValueTask<string>(_httpResponseUtil.NullResponse());
        })];
    }
}

public sealed class ContainmentChanceRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    public float chance { get; set; }
}
