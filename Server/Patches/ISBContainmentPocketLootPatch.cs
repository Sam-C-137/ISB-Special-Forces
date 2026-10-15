using System.Runtime.CompilerServices;
using HarmonyLib;
using ISB.Compatibility;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 10)]
public class ISBContainmentPocketLootPatch : IOnLoad
{
    public const string HarmonyId = "com.samc137.isbspecialforces.containmentpocketloot.server";
    private static ConfigController _config = null!;
    private static ISBSpawnController _spawns = null!;
    private static ItemHelper _items = null!;
    private static ILogger<ISBContainmentPocketLootPatch> _logger = null!;
    private static readonly ConditionalWeakTable<BotBase, object> Applied = new();
    private static int _reportedFailure;
    public ISBContainmentPocketLootPatch(ConfigController config, ISBSpawnController spawns, ItemHelper items, ILogger<ISBContainmentPocketLootPatch> logger)
    {
        _config = config;
        _spawns = spawns;
        _items = items;
        _logger = logger;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot));
        if (target == null)
        {
            _logger.LogError("[ISB] ContainmentBreach loot: bot generation method was not found.");
            return Task.CompletedTask;
        }

        new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(ISBContainmentPocketLootPatch), nameof(Postfix))) { priority = Priority.Last, after = [ISBBotPresetGenerationPatch.HarmonyId, ISBDogtagGenerationPatch.HarmonyId] });
        return Task.CompletedTask;
    }

    private static void Postfix(MongoId sessionId, BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        if (__result?.Inventory?.Items == null || botGenerationDetails == null)
            return;
        var role = GhostBotGenerationPatch.RequestedRole(botGenerationDetails) ?? __result.Info?.Settings?.Role;
        var anomalies = _config.AnomalousSpawns;
        bool anomalyEnabled = AnomalousItemSpawns.EligibleBot(anomalies, role);
        if (!ContainmentPocketLoot.IsBlackDivision(role) && !anomalyEnabled)
            return;
        var global = _config.ModConfig.blackDivisionLoot;
        var config = _config.ModConfig.labsContainmentBreachEvent?.blackDivisionPocketLoot;
        var session = sessionId.ToString();
        bool active = (config?.enabled == true || anomalyEnabled) && !string.IsNullOrWhiteSpace(session) && _spawns.GetLabsPreBlackoutEventStatus(session).active;
        string? location = active ? "laboratory" : botGenerationDetails.Location;
        if (global?.enabled != true && !active)
            return;
        lock (__result)
        {
            if (Applied.TryGetValue(__result, out _))
                return;
            Applied.Add(__result, new object ());
            try
            {
                TemplateItem? Resolve(MongoId tpl)
                {
                    var result = _items.GetItem(tpl);
                    return result.Key ? result.Value : null;
                }

                (int Width, int Height) Size(Item item)
                {
                    var size = _items.GetItemSize(__result.Inventory.Items, item.Id);
                    return (size?.Width ?? 0, size?.Height ?? 0);
                }

                int globalAdded = ContainmentPocketLoot.Apply(__result, active, location, global, Resolve, Size, template => _items.GenerateUpdForItem(template), Random.Shared, eventOnly: false, requestedRole: role);
                int eventAdded = active ? ContainmentPocketLoot.Apply(__result, true, location, config, Resolve, Size, template => _items.GenerateUpdForItem(template), Random.Shared, requestedRole: role) : 0;
                int anomalousAdded = active && anomalyEnabled ? AnomalousItemSpawns.ApplyBotLoot(__result, true, location, anomalies, Resolve, Size, template => _items.GenerateUpdForItem(template), Random.Shared, requestedRole: role) : 0;
                if (global::ISBSpecialForcesServer.ISBDiagnostics.Enabled)
                {
                    _logger.LogInformation("[ISB BD loot] Role={Role}; location={Location}; event={Active}; globalAdded={Global}; eventAdded={Event}; anomalousAdded={Anomalous}. Counts are placed items, after rolls and container checks.", role, location, active, globalAdded, eventAdded, anomalousAdded);
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _reportedFailure, 1) == 0)
                    _logger.LogError(ex, "[ISB] Could not add configured Black Division inventory loot.");
            }
        }
    }
}
