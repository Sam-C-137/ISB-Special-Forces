using ISBSpecialForcesServer.Controllers;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Routers;
#if ISB_SPT41
using SPTarkov.Server.Core.Helpers.Server;
#else
using SPTarkov.Server.Core.Helpers;
#endif
using System.Reflection;

namespace ISBSpecialForcesServer;
public sealed class LabsVariantState
{
    public bool enabled { get; set; }
    public bool unlocked { get; set; }
    public bool campaignActive { get; set; }
    public string selected { get; set; } = "standard";
    public string profile { get; set; } = "";
    public string lastQuest { get; set; } = "";
    public bool accepted { get; set; } = true;
    public string? message { get; set; }
}

public static class LabsVariantPolicy
{
    public static bool IsChoice(string? mode) => mode is "standard" or "containment" or "blackout";
    public static bool Unlocked(bool enabled, int? finalStatus) => enabled && finalStatus == 4;
    public static bool Campaign(bool eventEnabled, bool questForcesEvent, bool unlocked = false) => eventEnabled && questForcesEvent && !unlocked;
    public static string Resolve(bool enabled, bool unlocked, bool campaign, string? selection) => !enabled ? "legacy" : unlocked ? (IsChoice(selection) ? selection! : "standard") : campaign ? "containment" : "legacy";
}

[Injectable(InjectionType.Singleton)]
public sealed class LabsVariantController(ConfigController config, ISBQuestTruceController quests, FikaRaidContext fika)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _selected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _raids = new(StringComparer.Ordinal);
    public LabsVariantState State(string session)
    {
        var c = config.ModConfig.labsContainmentBreachEvent;
        var enabled = c.labsVariantsEnabled;
        var last = quests.TryGetQuestStatus(session, c.containmentBreachLastQuest);
        var unlocked = LabsVariantPolicy.Unlocked(enabled, last);
        var forced = c.questForceConditions?.ShouldForceEvent(id => quests.TryGetQuestStatus(session, id)) == true;
        lock (_sync)
            return new LabsVariantState
            {
                enabled = enabled,
                unlocked = unlocked,
                campaignActive = LabsVariantPolicy.Campaign(c.enabled, forced, unlocked),
                selected = _selected.GetValueOrDefault(session, "standard"),
                profile = session,
                lastQuest = c.containmentBreachLastQuest
            };
    }

    public LabsVariantState Select(string session, string? mode)
    {
        lock (_sync)
        {
            var state = State(session);
            var raid = fika.EventSession(session) ?? session;
            if (!state.unlocked || !LabsVariantPolicy.IsChoice(mode) || _raids.ContainsKey(raid))
            {
                state.accepted = false;
                state.message = !state.unlocked ? "Conclua a última quest da operação para liberar as versões da Labs." : _raids.ContainsKey(raid) ? "A versão desta incursão já foi confirmada." : "Versão de Labs inválida.";
                return state;
            }

            _selected[session] = mode!;
            state.selected = mode!;
            return state;
        }
    }

    public string BeginRaid(string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
            return "legacy";
        var raid = fika.EventSession(session) ?? session;
        var owner = fika.QuestOwner(raid) ?? session;
        lock (_sync)
        {
            if (_raids.TryGetValue(raid, out var existing))
                return existing;
            var s = State(owner);
            return _raids[raid] = LabsVariantPolicy.Resolve(s.enabled, s.unlocked, s.campaignActive, s.selected);
        }
    }

    public string RaidMode(string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
            return "legacy";
        lock (_sync)
            return _raids.GetValueOrDefault(fika.EventSession(session) ?? session, "legacy");
    }

    public void EndRaid(string? session)
    {
        if (string.IsNullOrWhiteSpace(session) || fika.EventSession(session) != session)
            return;
        lock (_sync)
            _raids.Remove(session);
    }
}

public sealed class LabsVariantRequest : IRequestData
{
    public string? mode { get; set; }
}

#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
#else
[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
#endif
public sealed class LabsVariantRouter(JsonUtil json, HttpResponseUtil http, LabsVariantController variants, ISBSpawnController spawns, ISBSpawnProgressController progress) : StaticRouter(json, [new RouteAction("/ISB/labsVariants/status", 
#if ISB_SPT41
            (url, info, session, output, cancellationToken) =>
#else
(url, info, session, output) => 
#endif
new ValueTask<object>(http.NoBody(variants.State(session)))), new RouteAction("/ISB/labsVariants/openSelection", 
#if ISB_SPT41
            (url, info, session, output, cancellationToken) =>
#else
(url, info, session, output) =>
#endif
{
    spawns.EndRaid(session);
    progress.CancelUnstarted(session);
    return new ValueTask<object>(http.NoBody(variants.State(session)));
}), new RouteAction<LabsVariantRequest>("/ISB/labsVariants/select", 
#if ISB_SPT41
            (url, info, session, output, cancellationToken) =>
#else
(url, info, session, output) => 
#endif
new ValueTask<string>(http.NoBody(variants.Select(session, info?.mode))))]);
#if ISB_SPT41
[Injectable(TypePriority = OnLoadOrder.PostLoad + 90101)]
#else
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 90101)]
#endif
public sealed class LabsVariantBanners(ModHelper mods, ImageRouter images, 
#if ISB_SPT41
    SPTarkov.Server.Core.Models.Spt.Tables.LocationTable locations
#else
SPTarkov.Server.Core.Services.DatabaseService database
#endif
) : IOnLoad
{
#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken token)
#else
    public Task OnLoad()
#endif
    {
        var root = mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        foreach (var mode in new[]
        {
            "standard",
            "containment",
            "blackout"
        }

        )
        {
            var file = Path.Combine(root, "db", "banners", "isb_labs_" + mode + ".png");
            if (File.Exists(file))
            {
                images.AddRoute("/files/banners/isb_labs_" + mode, file);
                images.AddRoute("/files/banners/isb_labs_" + mode + "_v2", file);
                images.AddRoute("/files/banners/isb_labs_" + mode + "_v4", file);
            }
        }

#if ISB_SPT41
        var labs = locations.GetDictionary().GetValueOrDefault("laboratory")?.Base;
#else
        var labs = database.GetLocations().GetDictionary().GetValueOrDefault("laboratory")?.Base;
#endif
        if (labs?.Banners != null)
        {
            var cover = new SPTarkov.Server.Core.Models.Eft.Common.Banner
            {
                Id = "isb_labs_standard",
                Picture = new SPTarkov.Server.Core.Models.Eft.Common.Pic
                {
                    File = "isb_labs_standard_v4.png",
                    Path = "banners/isb_labs_standard_v4.png",
                    Rcid = "",
                    Type = "png"
                }
            };
            if (labs.Banners.Count == 0)
                labs.Banners.Add(cover);
            else
                labs.Banners[0] = cover;
        }

        return Task.CompletedTask;
    }
}
