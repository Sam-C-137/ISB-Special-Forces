using HarmonyLib;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Controllers;
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class ISBContainmentRewardsPatch(SaveServer saveServer, MailSendService mailSendService, RewardHelper rewardHelper) : IOnLoad
{
    private static SaveServer _profiles = null!;
    private static MailSendService _mail = null!;
    private static RewardHelper _rewards = null!;
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        _profiles = saveServer;
        _mail = mailSendService;
        _rewards = rewardHelper;
        var harmony = new Harmony("com.samc137.isbspecialforces.containment-rewards");
        harmony.Patch(AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort)), postfix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(AfterAssort)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(RewardHelper), nameof(RewardHelper.ApplyRewards)), prefix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(BeforeRewards)), postfix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(AfterRewards)));
        harmony.Patch(AccessTools.Method(typeof(RewardHelper), "AddAchievementToProfile"), prefix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(BeforeAchievement)));
        harmony.Patch(AccessTools.Method(typeof(ProfileController), nameof(ProfileController.GetCompleteProfile)), prefix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(BeforeProfile)));
        harmony.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.CompleteQuest)), postfix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(AfterQuest)));
        harmony.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.FailQuest)), postfix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(AfterQuest)));
        return Task.CompletedTask;
    }

    private static void BeforeProfile(MongoId __0) => ReconcileVictoria(__0);
    private static void AfterQuest(MongoId __2) => ReconcileVictoria(__2);
    internal static void ReconcileVictoria(MongoId session)
    {
        var profile = _profiles.GetProfile(session);
        if (profile?.CharacterData?.PmcData == null) return;
        lock (profile)
        {
            if (!ContainmentAchievements.NeedsVictoria(profile.CharacterData.PmcData)) return;
            profile.CharacterData.PmcData.Achievements ??= [];
            _rewards.AddAchievementToProfile(profile, ContainmentAchievements.Victoria);
            _profiles.SaveProfileAsync(session, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static void AfterAssort(MongoId __0, MongoId __1, TraderAssort __result)
    {
        if (__1.ToString() != "690766de550bc322a810ea1e" || __result == null)
            return;
        ContainmentAchievements.FilterAssort(_profiles.GetProfile(__0)?.CharacterData?.PmcData, __result);
    }

    private static void BeforeRewards(ref IEnumerable<Reward> __0, string __1, MongoId __4)
    {
        __0 = ContainmentAchievements.ExecutableRewards(__0, __1, __4);
    }

    private static bool IsContainment(MongoId id) => ContainmentAchievements.IsManaged(id);
    private static bool BeforeAchievement(SptProfile __0, MongoId __1) =>
        (__1 != ContainmentAchievements.Victoria || ContainmentAchievements.QuestlineComplete(__0.CharacterData.PmcData)) &&
        (!IsContainment(__1) || __0.CharacterData.PmcData.Achievements?.ContainsKey(__1) != true);
    private static void AfterRewards(string __1, SptProfile __2, MongoId __4, List<Item> __result)
    {
        if (__1 != "achievement" || !IsContainment(__4) || __result == null || __result.Count == 0)
            return;
        var session = __2.ProfileInfo?.ProfileId ?? throw new InvalidOperationException("ISB: achievement reward profile has no session ID.");
        _mail.SendLocalisedSystemMessageToPlayer(session, "670547bb5fa0b1a7c30d5836 0", [..__result], [], 604800);
        __result.Clear();
    }
}
