using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ISBSpecialForces.Services;

[Injectable(InjectionType = InjectionType.Singleton)]
public sealed class CustomBotLoadoutItemsService(
    DatabaseService databaseService,
    ModHelper modHelper,
    JsonUtil jsonUtil,
    ILogger<CustomBotLoadoutItemsService> logger)
{
    public void MergeItems(Assembly assembly, string relativeLoadoutDirectory, string sourceDisplayName)
    {
        var modDirectory = modHelper.GetAbsolutePathToModFolder(assembly);
        var loadoutDirectory = Path.Combine(modDirectory, relativeLoadoutDirectory);

        if (!Directory.Exists(loadoutDirectory))
        {
            logger.LogWarning(
                "[ISB SOF] Custom inventory.items directory was not found: {Directory}",
                loadoutDirectory);
            return;
        }

        var botTypes = databaseService.GetTables()?.Bots?.Types;
        if (botTypes is null)
        {
            return;
        }

        var loadoutFiles = Directory
            .EnumerateFiles(loadoutDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase));

        foreach (var filePath in loadoutFiles)
        {
            CustomLoadoutFile? loadout;
            try
            {
                loadout = jsonUtil.Deserialize<CustomLoadoutFile>(File.ReadAllText(filePath));
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "[ISB SOF] Failed to read inventory.items from {FilePath}.",
                    filePath);
                continue;
            }

            var sourceItems = loadout?.Inventory?.Items;
            if (sourceItems is null || !sourceItems.HasAnyEntries())
            {
                continue;
            }

            var botTypeName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            if (!botTypes.TryGetValue(botTypeName, out var botType)
                || botType?.BotInventory?.Items is null)
            {
                logger.LogWarning(
                    "[ISB SOF] Could not merge inventory.items from {FileName}: bot type '{BotType}' was not found.",
                    Path.GetFileName(filePath),
                    botTypeName);
                continue;
            }

            var targetItems = botType.BotInventory.Items;

            targetItems.Backpack ??= new Dictionary<MongoId, double>();
            targetItems.Pockets ??= new Dictionary<MongoId, double>();
            targetItems.SecuredContainer ??= new Dictionary<MongoId, double>();
            targetItems.SpecialLoot ??= new Dictionary<MongoId, double>();
            targetItems.TacticalVest ??= new Dictionary<MongoId, double>();

            var mergedEntries = 0;
            mergedEntries += MergePool(sourceItems.Backpack, targetItems.Backpack, botTypeName, "Backpack");
            mergedEntries += MergePool(sourceItems.Pockets, targetItems.Pockets, botTypeName, "Pockets");
            mergedEntries += MergePool(sourceItems.SecuredContainer, targetItems.SecuredContainer, botTypeName, "SecuredContainer");
            mergedEntries += MergePool(sourceItems.SpecialLoot, targetItems.SpecialLoot, botTypeName, "SpecialLoot");
            mergedEntries += MergePool(sourceItems.TacticalVest, targetItems.TacticalVest, botTypeName, "TacticalVest");
        }
    }

    private int MergePool(
        Dictionary<string, double>? source,
        Dictionary<MongoId, double> target,
        string botTypeName,
        string poolName)
    {
        if (source is not { Count: > 0 })
        {
            return 0;
        }

        var mergedEntries = 0;

        foreach (var entry in source)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            if (!MongoId.IsValidMongoId(entry.Key))
            {
                logger.LogWarning(
                    "[ISB SOF] Ignored invalid item tpl '{ItemTpl}' in inventory.items.{Pool} for {BotType}.",
                    entry.Key,
                    poolName,
                    botTypeName);
                continue;
            }

            MongoId itemTpl = entry.Key;
            target[itemTpl] = entry.Value;
            mergedEntries++;
        }

        return mergedEntries;
    }

    public sealed class CustomLoadoutFile
    {
        [JsonPropertyName("inventory")]
        public CustomLoadoutInventory? Inventory { get; init; }
    }

    public sealed class CustomLoadoutInventory
    {
        [JsonPropertyName("items")]
        public CustomItemPools? Items { get; init; }
    }

    public sealed class CustomItemPools
    {
        [JsonPropertyName("Backpack")]
        public Dictionary<string, double>? Backpack { get; init; }

        [JsonPropertyName("Pockets")]
        public Dictionary<string, double>? Pockets { get; init; }

        [JsonPropertyName("SecuredContainer")]
        public Dictionary<string, double>? SecuredContainer { get; init; }

        [JsonPropertyName("SpecialLoot")]
        public Dictionary<string, double>? SpecialLoot { get; init; }

        [JsonPropertyName("TacticalVest")]
        public Dictionary<string, double>? TacticalVest { get; init; }

        public bool HasAnyEntries()
        {
            return Backpack is { Count: > 0 }
                || Pockets is { Count: > 0 }
                || SecuredContainer is { Count: > 0 }
                || SpecialLoot is { Count: > 0 }
                || TacticalVest is { Count: > 0 };
        }
    }
}
