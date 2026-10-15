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
using ISBSpecialForcesServer.Controllers;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class ISBContainmentRewardsPatch(SaveServer saveServer, MailSendService mailSendService) : IOnLoad
{
    private static SaveServer _profiles = null!;
    private static MailSendService _mail = null!;
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        _profiles = saveServer;
        _mail = mailSendService;
        var harmony = new Harmony("com.samc137.isbspecialforces.containment-rewards");
        harmony.Patch(AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort)), postfix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(AfterAssort)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(RewardHelper), nameof(RewardHelper.ApplyRewards)), prefix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(BeforeRewards)), postfix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(AfterRewards)));
        harmony.Patch(AccessTools.Method(typeof(RewardHelper), "AddAchievementToProfile"), prefix: new HarmonyMethod(typeof(ISBContainmentRewardsPatch), nameof(BeforeAchievement)));
        return Task.CompletedTask;
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
    private static bool BeforeAchievement(SptProfile __0, MongoId __1) => !IsContainment(__1) || __0.CharacterData.PmcData.Achievements?.ContainsKey(__1) != true;
    private static void AfterRewards(string __1, SptProfile __2, MongoId __4, List<Item> __result)
    {
        if (__1 != "achievement" || !IsContainment(__4) || __result == null || __result.Count == 0)
            return;
        var session = __2.ProfileInfo?.ProfileId ?? throw new InvalidOperationException("ISB: achievement reward profile has no session ID.");
        _mail.SendLocalisedSystemMessageToPlayer(session, "670547bb5fa0b1a7c30d5836 0", [..__result], [], 604800);
        __result.Clear();
    }
}
