using ISB.Compatibility;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ISBSpecialForces;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 3)]
public sealed class ChaosBotRegistration(BotTable bots, BotConfig config, ICloner cloner) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceKey = "isbscout";
        var key = GhostBotVariant.ChaosName.ToLowerInvariant();
        if (!bots.Types.TryGetValue(key, out var bot)) throw new InvalidOperationException("ISB: Chaos Insurgency shared bot template is missing.");
        if (bot?.BotDifficulty == null) throw new InvalidOperationException("ISB: Chaos Insurgency requires a valid base bot template.");
        var role = (WildSpawnType)GhostBotVariant.ChaosValue;
        var enemies = Enum.GetValues<WildSpawnType>().Concat(Enumerable.Range(13701, 16).Select(value => (WildSpawnType)value))
            .Concat(ISBSpecialForcesServer.BlackDivisionPresetPolicy.Aliases.Keys.Where(value => int.TryParse(value, out _)).Select(value => (WildSpawnType)int.Parse(value)))
            .Where(value => value != role).Distinct().ToList();
        foreach (var difficulty in bot.BotDifficulty.Values)
        {
            if (difficulty.Mind == null) continue;
            difficulty.Mind.FriendlyBotTypes = [role];
            difficulty.Mind.WarnBotTypes = [];
            difficulty.Mind.EnemyBotTypes = new(enemies);
            difficulty.Mind.RevengeBotTypes = [];
        }
        foreach (var other in bots.Types.Where(pair => pair.Key != key).Select(pair => pair.Value))
            foreach (var difficulty in other.BotDifficulty?.Values.AsEnumerable() ?? [])
            {
                if (difficulty.Mind == null) continue;
                difficulty.Mind.FriendlyBotTypes?.Remove(role);
                difficulty.Mind.WarnBotTypes?.Remove(role);
                difficulty.Mind.EnemyBotTypes ??= [];
                if (!difficulty.Mind.EnemyBotTypes.Contains(role)) difficulty.Mind.EnemyBotTypes.Add(role);
            }
        if (!config.PresetBatch.ContainsKey(key)) config.PresetBatch[key] = config.PresetBatch.GetValueOrDefault(sourceKey, 10);
        if (!config.Bosses.Contains(key, StringComparer.OrdinalIgnoreCase)) config.Bosses.Add(key);
        if (!config.Equipment.ContainsKey(key) && config.Equipment.TryGetValue(sourceKey, out var equipment)) config.Equipment[key] = cloner.Clone(equipment)!;
        if (!config.Durability.BotDurabilities.ContainsKey(key) && config.Durability.BotDurabilities.TryGetValue(sourceKey, out var durability)) config.Durability.BotDurabilities[key] = cloner.Clone(durability)!;
        if (!config.ItemSpawnLimits.ContainsKey(key) && config.ItemSpawnLimits.TryGetValue(sourceKey, out var limits)) config.ItemSpawnLimits[key] = cloner.Clone(limits)!;
        if (!config.LootItemResourceRandomization.ContainsKey(key) && config.LootItemResourceRandomization.TryGetValue(sourceKey, out var resources)) config.LootItemResourceRandomization[key] = cloner.Clone(resources)!;
        return Task.CompletedTask;
    }
}
