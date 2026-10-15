using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Models.Enums;
#if ISB_SPT41
using SPTarkov.Server.Core.Services.Profile;
using ProfileValidatorService = SPTarkov.Server.Core.Services.Profile.ProfileMigrationService;
#else
using SPTarkov.Server.Core.Services;
#endif
using SPTarkov.Server.Core.Utils;

namespace ISBSpecialForces.Services;

internal static class EventQuestProfileIds
{
    private static readonly Dictionary<string, string> Ids = Load();
    internal static string Current(string value)
    {
        if (Ids.TryGetValue(value, out var id)) return id;
        var separator = value.IndexOf('/');
        if (separator == 24 && Ids.TryGetValue(value[..separator], out id))
            return id + value[separator..];
        const string prefix = "ContainmentBreach_";
        return value.StartsWith(prefix, StringComparison.Ordinal) && Ids.TryGetValue(value[prefix.Length..], out id) ? prefix + id : value;
    }
    private static Dictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ISBSpecialForces.EventQuestIdMigration")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    private static int StatusPriority(JsonObject quest) => (QuestStatusEnum)quest["status"]!.GetValue<int>() switch
    {
        QuestStatusEnum.Success => 60,
        QuestStatusEnum.Fail => 50,
        QuestStatusEnum.AvailableForFinish => 40,
        QuestStatusEnum.Started => 30,
        QuestStatusEnum.FailRestartable => 20,
        QuestStatusEnum.AvailableForStart => 10,
        _ => 0
    };

    private static long StartTime(JsonObject quest) => long.TryParse(quest["startTime"]?.ToString(), out var time) ? time : 0;

    internal static int Repair(JsonNode? node)
    {
        var changed = 0;
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && Ids.TryGetValue(text, out var current))
                {
                    obj[pair.Key] = current;
                    changed++;
                }
                else changed += Repair(pair.Value);
                var key = Current(pair.Key);
                if (key != pair.Key)
                {
                    var old = obj[pair.Key];
                    obj.Remove(pair.Key);
                    if (!obj.ContainsKey(key)) obj[key] = old;
                    changed++;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && Ids.TryGetValue(text, out var current))
                {
                    array[i] = current;
                    changed++;
                }
                else changed += Repair(array[i]);
            }
            if (array.Count > 0 && array.All(n => n is JsonObject q && q.ContainsKey("qid") && q.ContainsKey("status")))
            {
                foreach (var group in array.Cast<JsonObject>().GroupBy(q => q["qid"]!.GetValue<string>()).Where(g => g.Count() > 1).ToArray())
                {
                    var keep = group.OrderByDescending(StatusPriority).ThenByDescending(StartTime).First();
                    foreach (var other in group.Where(q => q != keep).ToArray())
                    {
                        if (other["completedConditions"] is JsonArray completed)
                        {
                            keep["completedConditions"] ??= new JsonArray();
                            var merged = keep["completedConditions"]!.AsArray();
                            foreach (var condition in completed)
                                if (!merged.Any(c => JsonNode.DeepEquals(c, condition))) merged.Add(condition?.DeepClone());
                        }
                        array.Remove(other);
                        changed++;
                    }
                }
            }
        }
        return changed;
    }
}

[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 2)]
public sealed class EventQuestProfileMigration(SaveServer saves, JsonUtil json) : IOnLoad
{
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken token)
#else
    public Task OnLoad()
#endif
    {
        var harmony = new Harmony("com.samc137.isb.event-quest-identities");
        harmony.Patch(AccessTools.Method(typeof(ProfileValidatorService), nameof(ProfileValidatorService.MigrateAndValidateProfile)), prefix: new HarmonyMethod(typeof(EventQuestProfileMigration), nameof(BeforeValidation)));
        foreach (var pair in saves.GetProfiles().ToArray())
        {
            var node = JsonNode.Parse(json.Serialize(pair.Value))!;
            if (EventQuestProfileIds.Repair(node) == 0) continue;
            var migrated = json.Deserialize<SptProfile>(node.ToJsonString())!;
            foreach (var property in typeof(SptProfile).GetProperties().Where(p => p.CanRead && p.CanWrite))
                property.SetValue(pair.Value, property.GetValue(migrated));
        }
        return Task.CompletedTask;
    }
    private static void BeforeValidation(JsonObject __0) => EventQuestProfileIds.Repair(__0);
}
