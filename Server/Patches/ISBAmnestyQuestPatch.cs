using HarmonyLib;
using ISBSpecialForcesServer.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
#if ISB_SPT41
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Spt.Tables;
#else
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;

#endif
namespace ISBSpecialForcesServer.Patches;
#if ISB_SPT41
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 1100)]
#else
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostSptModLoader + 1100)]
#endif
public sealed class ISBAmnestyQuestPatch(ISBRevengeController revenge, ProfileHelper profiles, JsonUtil json, EventOutputHolder output, HttpResponseUtil http, 
#if ISB_SPT41
    LocaleTable locales
#else
DatabaseService database
#endif
) : IOnLoad
{
    private readonly ISBRevengeController _revenge = revenge;
    private readonly ProfileHelper _profiles = profiles;
    private static ISBAmnestyQuestPatch _instance = null!;
    private string _template = string.Empty;
    public const int Price = 10000;
    private const string Dollars = "5696686a4bdc2da3298b456a";
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
        _instance = this;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ISBSpecialForces.AmnestyQuest.json")!;
        using var reader = new StreamReader(stream);
        _template = reader.ReadToEnd();
#if ISB_SPT41
        var global = locales.Global;
#else
        var global = database.GetTables().Locales.Global;
#endif
        foreach (var(language, locale)in global)
            locale.AddTransformer(current =>
            {
                current ??= [];
                var pt = string.Equals(language, "po", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "pt", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "pt-br", StringComparison.OrdinalIgnoreCase);
                current["ISB/Amnesty/name"] = "Clean Record";
                current["ISB/Amnesty/description"] = "I gave you a radio like this so you wouldn't go around shooting at my people. Well, you can imagine they must be pretty upset, and I don't blame them. How about doing your part so I can help clear your name with them?";
                current["ISB/Amnesty/startedMessageText"] = "Hand over dollars as compensation for your “betrayal”";
                current["ISB/Amnesty/handover"] = "Hand over dollars as compensation for your “betrayal”";
                current["ISB/Amnesty/successMessageText"] = "That'll be enough. Thanks! I'll make sure you don't get shot again.";
                foreach (var key in new[]
                {
                    "failMessageText",
                    "acceptPlayerMessage",
                    "declinePlayerMessage",
                    "completePlayerMessage",
                    "changeQuestMessageText",
                    "note"
                }

                )
                    current["ISB/Amnesty/" + key] = string.Empty;
                return current;
            });
        var patch = new Harmony("com.samc137.isb.amnesty");
        patch.Patch(AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.GetClientQuests)), postfix: new HarmonyMethod(typeof(ISBAmnestyQuestPatch), nameof(AddQuests)));
        foreach (var type in new[]
        {
            typeof(QuestHelper),
            typeof(QuestRewardHelper)
        }

        )
            patch.Patch(AccessTools.Method(type, "GetQuestFromDb", [typeof(MongoId), typeof(PmcData)]), prefix: new HarmonyMethod(typeof(ISBAmnestyQuestPatch), nameof(ResolveQuest)));
        patch.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest)), prefix: new HarmonyMethod(typeof(ISBAmnestyQuestPatch), nameof(BeforeAccept)));
        patch.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.HandoverQuest)), prefix: new HarmonyMethod(typeof(ISBAmnestyQuestPatch), nameof(BeforeHandover)));
        patch.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.CompleteQuest)), prefix: new HarmonyMethod(typeof(ISBAmnestyQuestPatch), nameof(BeforeComplete)), postfix: new HarmonyMethod(typeof(ISBAmnestyQuestPatch), nameof(AfterComplete)));
        return Task.CompletedTask;
    }

    public static bool IsAmnesty(MongoId id) => id.ToString().StartsWith("495342", StringComparison.Ordinal);
    public static string LegacyConditionId(MongoId id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + "/handover")))[..24].ToLowerInvariant();
    public static string ConditionId(MongoId id) => "495342" + LegacyConditionId(id)[..18];
    public Quest BuildQuest(MongoId id)
    {
        var quest = json.Deserialize<Quest>(_template)!;
        quest.Id = id;
        quest.Conditions.AvailableForFinish![0].Id = ConditionId(id);
        return quest;
    }

    private static bool ResolveQuest(MongoId __0, PmcData __1, ref Quest? __result)
    {
        if (!IsAmnesty(__0))
            return true;
        var owner = _instance._revenge.GetAmnestyOwner(__0);
        __result = owner != null && _instance._profiles.GetPmcProfile(owner)?.Id == __1.Id ? _instance.BuildQuest(__0) : null;
        return false;
    }

    private static void AddQuests(MongoId __0, List<Quest> __result) => _instance.AddVisibleQuests(__0, __result);
    public void AddVisibleQuests(MongoId sessionId, List<Quest> quests)
    {
        var profile = _profiles.GetPmcProfile(sessionId);
        if (profile?.TradersInfo?.ContainsKey(AishiLoyaltyConditionController.AishiTraderId) != true)
            return;
        var state = _revenge.GetAmnestyState(sessionId);
        foreach (var id in state.quests)
        {
            MigrateCondition(profile, id);
            var status = profile.Quests?.FirstOrDefault(q => q.QId == id);
            if (id == state.active && !state.pending)
            {
                if (status?.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish)
                {
                    status.Status = QuestStatusEnum.Success;
                    status.StatusTimers ??= [];
                    status.StatusTimers[QuestStatusEnum.Success] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    status.CompletedConditions ??= [];
                    status.CompletedConditions.Add(ConditionId(id));
                }

                _revenge.ClearForAmnesty(sessionId, id);
            }

            if (status == null && (id != state.active || !state.pending))
                continue;
            if (quests.Any(q => q.Id == id))
                continue;
            var quest = BuildQuest(id);
            quest.SptStatus = status?.Status ?? QuestStatusEnum.AvailableForStart;
            quest.Status = (int)quest.SptStatus;
            quests.Add(quest);
        }
    }

    private bool Active(MongoId sessionId, MongoId id)
    {
        var state = _revenge.GetAmnestyState(sessionId);
        return state.pending && state.active == id;
    }

    private bool Reject(MongoId sessionId, ref ItemEventRouterResponse result)
    {
        result = http.AppendErrorToOutput(output.GetOutput(sessionId), "ISB: this settlement is unavailable or the $10,000 payment is incomplete.");
        return false;
    }

    private static bool BeforeAccept(PmcData __0, AcceptQuestRequestData __1, MongoId __2, ref ItemEventRouterResponse __result)
    {
        if (!IsAmnesty(__1.QuestId))
            return true;
        var status = __0.Quests?.FirstOrDefault(q => q.QId == __1.QuestId)?.Status;
        return _instance.Active(__2, __1.QuestId) && (status == null || status is QuestStatusEnum.AvailableForStart or QuestStatusEnum.Locked) || _instance.Reject(__2, ref __result);
    }

    private static bool BeforeHandover(PmcData __0, HandoverQuestRequestData __1, MongoId __2, ref ItemEventRouterResponse __result)
    {
        if (!IsAmnesty(__1.QuestId))
            return true;
        if (!_instance.Active(__2, __1.QuestId) || __1.ConditionId != ConditionId(__1.QuestId) || __0.Quests?.Any(q => q.QId == __1.QuestId && q.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish) != true || __1.Items == null || !__1.Items.Any() || __1.Items.Select(x => x.Id).Distinct().Count() != __1.Items.Count())
            return _instance.Reject(__2, ref __result);
        foreach (var payment in __1.Items)
        {
            var item = __0.Inventory?.Items?.FirstOrDefault(i => i.Id == payment.Id);
            if (item?.Template != Dollars || payment.Count <= 0 || payment.Count == null || payment.Count > (item.Upd?.StackObjectsCount ?? 1))
                return _instance.Reject(__2, ref __result);
        }

        return true;
    }

    private static void MigrateCondition(PmcData profile, MongoId questId)
    {
        var oldId = LegacyConditionId(questId);
        var newId = ConditionId(questId);
        if (profile.TaskConditionCounters?.TryGetValue(oldId, out var old) == true && old.SourceId == questId)
        {
            if (!profile.TaskConditionCounters.ContainsKey(newId))
            {
                old.Id = newId;
                profile.TaskConditionCounters[newId] = old;
            }

            profile.TaskConditionCounters.Remove(oldId);
        }

        var status = profile.Quests?.FirstOrDefault(q => q.QId == questId);
        if (status?.Status is not (QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish))
            return;
        if (status.CompletedConditions?.Remove(oldId) == true)
            status.CompletedConditions.Add(newId);
        if (profile.TaskConditionCounters?.TryGetValue(newId, out var counter) != true || counter.Value < Price)
        {
            status.CompletedConditions?.Remove(newId);
            status.Status = QuestStatusEnum.Started;
            status.StatusTimers?.Remove(QuestStatusEnum.AvailableForFinish);
        }
    }

    public bool CanComplete(PmcData profile, MongoId sessionId, MongoId questId)
    {
        MigrateCondition(profile, questId);
        return Active(sessionId, questId) && profile.Quests?.Any(q => q.QId == questId && q.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish) == true && profile.TaskConditionCounters?.TryGetValue(ConditionId(questId), out var counter) == true && counter.SourceId == questId && counter.Value >= Price;
    }

    private static bool BeforeComplete(PmcData __0, CompleteQuestRequestData __1, MongoId __2, ref ItemEventRouterResponse __result) => !IsAmnesty(__1.QuestId) || _instance.CanComplete(__0, __2, __1.QuestId) || _instance.Reject(__2, ref __result);
    private static void AfterComplete(PmcData __0, CompleteQuestRequestData __1, MongoId __2)
    {
        if (IsAmnesty(__1.QuestId) && __0.Quests?.Any(q => q.QId == __1.QuestId && q.Status == QuestStatusEnum.Success) == true)
            _instance._revenge.ClearForAmnesty(__2, __1.QuestId);
    }
}
