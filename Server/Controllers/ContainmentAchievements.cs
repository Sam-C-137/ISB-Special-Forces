using Path = System.IO.Path;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ISBSpecialForcesServer.Controllers;
internal static class ContainmentAchievements
{
    internal const string Victoria = "1d9f3fb317d15e2a77957327";
    internal const string Veteran = "9df3731674b1b81ebec38ce4";
    internal const string Secure = "c447fc786e04524e87d4ea8a";
    private static MongoId[] _questline = [];
    private static Dictionary<MongoId, MongoId[]> _excludedBySuccess = [];

    internal static void ConfigureQuestline(TemplateTable templates, string modRoot, bool lotusInstalled)
    {
        var required = ReadQuestIds(Path.Combine(modRoot, "db", "ISBAndBlackDiv", "Quests")).ToList();
        foreach (var collaboration in new[] { "ISBAndManimalLabs", "ISBAndLotus" })
            if (collaboration != "ISBAndLotus" || lotusInstalled)
                required.AddRange(ReadQuestIds(Path.Combine(modRoot, "db", collaboration, "Quests"))
                    .Where(id => templates.Quests.ContainsKey(id)));
        _questline = required.Distinct().ToArray();
        if (_questline.Length == 0)
            throw new InvalidDataException("ISB: the Containment Breach questline is empty.");
        var requiredIds = _questline.ToHashSet();
        var exclusions = _questline.ToDictionary(id => id, id => templates.Quests.TryGetValue(id, out var quest)
            ? (quest.Conditions?.Fail ?? []).Where(c => c.ConditionType == "Quest" && c.Status?.SetEquals([QuestStatusEnum.Success]) == true
                && c.Target?.Item is { Length: 24 } target && requiredIds.Contains(new MongoId(target)))
                .Select(c => new MongoId(c.Target!.Item!)).Distinct().ToArray()
            : []);
        _excludedBySuccess = exclusions.Where(pair => pair.Value.Any(other => exclusions.GetValueOrDefault(other)?.Contains(pair.Key) == true))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Where(other => exclusions.GetValueOrDefault(other)?.Contains(pair.Key) == true).ToArray());
        foreach (var achievement in templates.Achievements.Concat(templates.CustomAchievements).Where(a => a.Id == Victoria))
        {
            achievement.Conditions.AvailableForFinish = _questline.Select((id, index) => new QuestCondition
            {
                Id = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("isb.cb.victoria:" + id))).ToLowerInvariant()[..24],
                Index = index,
                ConditionType = "Quest",
                DynamicLocale = false,
                Target = new(null, id.ToString()),
                Status = [QuestStatusEnum.Success, QuestStatusEnum.Fail],
                AvailableAfter = 0,
                VisibilityConditions = []
            }).ToList();
        }
    }

    private static IEnumerable<MongoId> ReadQuestIds(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("ISB: missing Containment Breach quest definitions: " + root);
        foreach (var path in Directory.EnumerateFiles(root, "quest_definitions.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var quest in document.RootElement.EnumerateObject())
                yield return new MongoId(quest.Name);
        }
    }

    internal static bool QuestlineComplete(PmcData? profile)
    {
        if (_questline.Length == 0 || profile?.Quests == null)
            return false;
        var statuses = profile.Quests.GroupBy(q => q.QId).ToDictionary(group => group.Key, group => group.Last().Status);
        return _questline.All(id => statuses.TryGetValue(id, out var status) && status is QuestStatusEnum.Success or QuestStatusEnum.Fail
            || _excludedBySuccess.TryGetValue(id, out var alternatives) && alternatives.Any(other => statuses.GetValueOrDefault(other) == QuestStatusEnum.Success));
    }

    internal static bool NeedsVictoria(PmcData? profile) => QuestlineComplete(profile) && profile?.Achievements?.ContainsKey(Victoria) != true;

    internal static bool HasVictoria(PmcData? profile) => profile?.Achievements?.ContainsKey(Victoria) == true && QuestlineComplete(profile);

    private static readonly Lazy<Dictionary<string, string>> Assorts = new(LoadAssorts);
    private static Dictionary<string, string> LoadAssorts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var path = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "db", "AchievementAssorts.json");
        using var stream = File.Exists(path) ? File.OpenRead(path) : assembly.GetManifestResourceStream("ISBSpecialForces.AchievementAssorts.json")
            ?? throw new InvalidDataException("ISB achievement assort definitions are missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidDataException("ISB achievement assort definitions are empty.");
    }
    internal static bool IsManaged(MongoId id) => id.ToString() is Victoria or Veteran or Secure
        or "6941763c6b2e0c64d04ef254" or "69417646c9c13c9353996c4e" or "6941764b021c3b1630cf28d5"
        or "6a5d3f64a8b2157267046245" or "6a5d3f7ffc741ffbbf091b8e" or "6a5d3f83cfd86f66e72d47a5"
        or "6a5d422c743a1afbb2183f54" or "6a5d431a5243a4c3a0d2afe9"
        or "1c69974a9de656678427b103" or "a18f21d769df5f4e9353d991" or "cebfde1f81cf55d3b3e6bccb";
    internal static IEnumerable<Reward> ExecutableRewards(IEnumerable<Reward> rewards, string source, MongoId id)
    {
        var key = id.ToString();
        if (source != "achievement" || !IsManaged(id))
            return rewards;
        return rewards.Where(r => r.Type != RewardType.CustomizationOffer).ToArray();
    }

    internal static void FilterAssort(PmcData? profile, TraderAssort assort)
    {
        var removed = assort.Items.Where(i => i.ParentId == "hideout" && Assorts.Value.TryGetValue(i.Template.ToString(), out var achievement) && profile?.Achievements?.ContainsKey(new MongoId(achievement)) != true).Select(i => i.Id).ToHashSet();
        if (removed.Count == 0)
            return;
        int previous;
        do
        {
            previous = removed.Count;
            foreach (var item in assort.Items)
                if (item.ParentId is { Length: 24 } parent && removed.Contains(parent))
                    removed.Add(item.Id);
        }
        while (removed.Count != previous);
        assort.Items.RemoveAll(i => removed.Contains(i.Id));
        foreach (var id in removed)
        {
            assort.BarterScheme.Remove(id);
            assort.LoyalLevelItems.Remove(id);
        }
    }
}
