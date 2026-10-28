using System.Runtime.CompilerServices;
using HarmonyLib;
using ISB.Compatibility;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 4)]
public sealed class GhostBotGenerationPatch(BotConfig botConfig) : IOnLoad
{
    private static readonly ConditionalWeakTable<BotGenerationDetails, Request> Requests = new();
    private sealed record Request(string Role, string? EventRole);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Ready = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> GeneratedRoles = new(StringComparer.Ordinal);
    public static bool DayReady { get => IsReady(GhostBotVariant.Name, false); set => Ready.AddOrUpdate(GhostBotVariant.LeadName, value ? 1 : 0, (_, old) => value ? old | 1 : old & ~1); }
    public static bool NightReady { get => IsReady(GhostBotVariant.Name, true); set => Ready.AddOrUpdate(GhostBotVariant.LeadName, value ? 2 : 0, (_, old) => value ? old | 2 : old & ~2); }

    internal static void SetReady(string role, bool day, bool night) => Ready[GhostBotVariant.CanonicalName(role) ?? role] = (day ? 1 : 0) | (night ? 2 : 0);
    internal static bool IsReady(string role, bool night) => Ready.TryGetValue(GhostBotVariant.CanonicalName(role) ?? role, out var flags) && (flags & (night ? 2 : 1)) != 0;
    internal static string? RequestedRole(BotGenerationDetails details) => details == null ? null : Requests.TryGetValue(details, out var request) ? GhostBotVariant.CanonicalName(request.Role) : GhostBotVariant.CanonicalName(details.Role);
    public static bool PresetsLoaded { get; set; }

    internal static bool IsRequest(BotGenerationDetails details) => details != null && (GhostBotVariant.Is(details.Role) || Requests.TryGetValue(details, out _));
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var role in GhostBotVariant.Names)
        {
            botConfig.PresetBatch[role] = 10;
            botConfig.PresetBatch[role.ToLowerInvariant()] = 10;
        }

        var harmony = new Harmony("com.samc137.isbspecialforces.ghostbots");
        var generator = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot)) ?? throw new MissingMethodException("ISB: bot generator unavailable for GhostBuster.");
        harmony.Patch(generator, prefix: new HarmonyMethod(typeof(GhostBotGenerationPatch), nameof(BeforeGeneration)) { priority = Priority.First }, postfix: new HarmonyMethod(typeof(GhostBotGenerationPatch), nameof(AfterGeneration)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(GhostBotGenerationPatch), nameof(FinishGeneration)));
        var difficulty = AccessTools.Method(typeof(BotController), nameof(BotController.GetBotDifficulty)) ?? throw new MissingMethodException("ISB: difficulty resolver unavailable for GhostBuster.");
        harmony.Patch(difficulty, prefix: new HarmonyMethod(typeof(GhostBotGenerationPatch), nameof(BeforeDifficulty)));
        return Task.CompletedTask;
    }

    private static void BeforeDifficulty(ref string type)
    {
        if (GhostBotVariant.Is(type))
            type = GhostBotVariant.BaseRole(type).ToLowerInvariant();
    }

    private static void BeforeGeneration(BotGenerationDetails botGenerationDetails)
    {
        if (botGenerationDetails == null || !GhostBotVariant.Is(botGenerationDetails.Role))
            return;
        Requests.Add(botGenerationDetails, new Request(botGenerationDetails.Role, botGenerationDetails.EventRole));
        botGenerationDetails.Role = GhostBotVariant.BaseRole(botGenerationDetails.Role).ToLowerInvariant();
        botGenerationDetails.RoleLowercase = botGenerationDetails.Role;
        botGenerationDetails.EventRole = null;
    }

    private static void AfterGeneration(BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        if (IsRequest(botGenerationDetails) && !ContainmentHealth.Apply(__result, true, GhostBotVariant.HealthScale(RequestedRole(botGenerationDetails)), GhostBotVariant.HealthScale(RequestedRole(botGenerationDetails))))
            throw new InvalidOperationException("ISB: invalid health data for GhostBuster.");
    }

    private static void FinishGeneration(BotGenerationDetails botGenerationDetails, BotBase __result, Exception? __exception)
    {
        if (botGenerationDetails == null || !Requests.TryGetValue(botGenerationDetails, out var request))
            return;
        Requests.Remove(botGenerationDetails);
        botGenerationDetails.Role = request.Role;
        botGenerationDetails.RoleLowercase = request.Role.ToLowerInvariant();
        botGenerationDetails.EventRole = request.EventRole;
        if (__exception == null && __result?.Info?.Settings != null)
        {
            __result.Info.Settings.Role = GhostBotVariant.BaseRole(request.Role);
            if (GeneratedRoles.Count >= 8192)
                GeneratedRoles.Clear();
            var profileId = __result.Id.ToString();
            if (!string.IsNullOrEmpty(profileId))
                GeneratedRoles[profileId] = "ScavRole/" + GhostBotVariant.CanonicalName(request.Role);
        }
    }
}
