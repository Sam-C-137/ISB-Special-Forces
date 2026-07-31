using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace ISBSpecialForcesServer.Patches;

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader)]
public class DurabilityRolePatch : IOnLoad
{
    private static ILogger<DurabilityRolePatch> _logger;

    private static BotConfig _botConfig;

    private static readonly ConcurrentDictionary<string, byte> LoggedRoles = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ISBTypeNames =
    {
        "ISBSpecialForces",
        "ISBBossCommander",
        "ISBFollowerCommander",
        "ISBTeamLeader",
        "ISBSecondLeader"
    };

    private static readonly HashSet<string> ISBRoles = new(ISBTypeNames, StringComparer.OrdinalIgnoreCase);

    private readonly ModHelper _modHelper;

    private readonly JsonUtil _jsonUtil;

    public DurabilityRolePatch(
        ILogger<DurabilityRolePatch> logger,
        ConfigServer configServer,
        ModHelper modHelper,
        JsonUtil jsonUtil)
    {
        _logger = logger;
        _botConfig = configServer.GetConfig<BotConfig>();
        _modHelper = modHelper;
        _jsonUtil = jsonUtil;
    }

    public Task OnLoad()
    {
        ApplyPerTypeDurabilityOverrides();

        var harmony = new Harmony("com.samc137.isbspecialforces.durabilityperrole");
        var original = AccessTools.Method(typeof(DurabilityLimitsHelper), "GetDurabilityRole");

        if (original is null)
        {
            return Task.CompletedTask;
        }

        var prefix = new HarmonyMethod(AccessTools.Method(typeof(DurabilityRolePatch), nameof(Prefix)));

        harmony.Patch(original, prefix: prefix);


        return Task.CompletedTask;
    }

    private void ApplyPerTypeDurabilityOverrides()
    {
        if (_botConfig?.Durability?.BotDurabilities is null)
        {
            return;
        }

        var modPath = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        foreach (var typeName in ISBTypeNames)
        {
            var filePath = Path.Combine(modPath, "db", "bots", "sharedConfig", $"{typeName}.json");

            if (!File.Exists(filePath))
            {
                continue;
            }

            SharedConfigDurabilityFile parsed;

            try
            {
                parsed = _jsonUtil.Deserialize<SharedConfigDurabilityFile>(File.ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                continue;
            }

            if (parsed?.durability is null)
            {
                continue;
            }

            var key = typeName.ToLowerInvariant();
            _botConfig.Durability.BotDurabilities[key] = parsed.durability;

            var weapon = parsed.durability.Weapon;
        }
    }

    private static bool Prefix(string? botRole, ref string __result)
    {
        if (botRole is null || !ISBRoles.Contains(botRole))
        {
            return true;
        }

        var key = botRole.ToLowerInvariant();
        var registered = _botConfig?.Durability?.BotDurabilities?.ContainsKey(key) == true;

        if (LoggedRoles.TryAdd(key, 0))
        {
            _logger.LogInformation($"[ISB DURABILITY PATCH] Weapon/Gear Durability roles done");
        }

        if (!registered)
        {
            return true;
        }

        __result = key;
        return false;
    }

    public class SharedConfigDurabilityFile
    {
        public DefaultDurability? durability { get; set; }
    }
}