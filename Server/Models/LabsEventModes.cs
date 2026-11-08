using System.Text.Json.Nodes;

namespace ISBSpecialForcesServer;
public static class LabsEventModes
{
    private static readonly string[] WhiteTusks = ["ISBBossFirefly", "ISBFollowerVipper", "ISBFollowerLoghan", "ISBFollowerSpearthrower", "ISBFollowerZofia", "ISBFollowerEvernight", "ISBFollowerNemesis"];
    private static readonly string[] Ghosts = ["blackDivGhostBusterLead", "blackDivGhostBuster01", "blackDivGhostBuster02", "blackDivGhostBuster03", "blackDivGhostBuster04"];
    internal static bool IsSpecialFormation(SpawnFormationConfig formation) =>
        IsSpecialRole(formation.boss) || formation.followers?.Any(f => f != null && Math.Max(f.min, f.max) > 0 && IsSpecialRole(f.type)) == true;

    private static bool IsSpecialRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return false;
        role = role.Trim();
        return WhiteTusks.Contains(role, StringComparer.OrdinalIgnoreCase)
            || ISB.Compatibility.GhostBotVariant.Is(role) && !ISB.Compatibility.GhostBotVariant.IsChaos(role)
            || string.Equals(role, "blackDivIb", StringComparison.OrdinalIgnoreCase)
            || ISB.Compatibility.ContainmentWedgeLimit.IsWedge(role)
            || int.TryParse(role, out int id) && (id is >= 13702 and <= 13706 or 13715 or 13716 or 848426);
    }

    internal static bool UsesSquadQueue(LabsPreBlackoutEventConfig config) =>
        !config.whiteTuskOnly && !config.ghostBusterOnly;

    internal static void NormalizeSpecialFormations(LabsPreBlackoutEventConfig config)
    {
        Prune(config.isb);
        Prune(config.blackDivision);
        Prune(config.pmcs);
        Prune(config.raiders);
        Prune(config.externalFactions?.chaosInsurgency);
        NormalizeSpecialFormations(config.labsChaos);
    }

    internal static void NormalizeSpecialFormations(LabsBlackoutEventPhaseConfig? phase)
    {
        if (phase == null) return;
        Prune(phase.isb);
        Prune(phase.blackDivision);
        Prune(phase.pmcs);
        Prune(phase.raiders);
        Prune(phase.externalFactions?.chaosInsurgency);
    }

    private static void Prune(LabsPreBlackoutFactionConfig? faction)
    {
        if (faction == null) return;
        faction.patrols?.RemoveAll(formation => formation == null || !IsSpecialFormation(formation));
        faction.checkpoints?.RemoveAll(formation => formation == null || !IsSpecialFormation(formation));
        faction.hunts?.RemoveAll(formation => formation == null || !IsSpecialFormation(formation));
    }

    internal static IReadOnlyList<string> NormalizeSpecialFormations(JsonObject root, bool containment)
    {
        var changed = new List<string>();
        void Queue(JsonObject parent, string name, string prefix)
        {
            if (parent[name] is JsonObject queue && queue["enabled"] is JsonValue value && value.TryGetValue<bool>(out bool enabled) && !enabled)
            {
                queue["enabled"] = true;
                changed.Add(prefix + "/enabled");
            }
        }

        void Phase(JsonObject phase, string prefix)
        {
            void Faction(JsonObject? faction, string path)
            {
                if (faction == null) return;
                foreach (string kind in new[] { "patrols", "checkpoints", "hunts" })
                {
                    if (faction[kind] is not JsonArray rows) continue;
                    bool pruned = false;
                    for (int i = rows.Count - 1; i >= 0; i--)
                    {
                        if (rows[i] is JsonObject row && IsSpecialFormation(row)) continue;
                        rows.RemoveAt(i);
                        pruned = true;
                    }
                    if (pruned) changed.Add(path + "/" + kind);
                }
            }

            foreach (string faction in new[] { "isb", "blackDivision", "pmcs", "raiders" })
                Faction(phase[faction] as JsonObject, prefix + "/" + faction);
            if (phase["externalFactions"] is JsonObject external)
                Faction(external["chaosInsurgency"] as JsonObject, prefix + "/externalFactions/chaosInsurgency");
        }

        if (containment)
        {
            Queue(root, "containementBreachQueue", "/containementBreachQueue");
            Phase(root, "");
            foreach (string name in new[] { "labsChaos", "isbReinforcements" })
                if (root[name] is JsonObject phase) Phase(phase, "/" + name);
        }
        else
        {
            Queue(root, "blackoutQueue", "/blackoutQueue");
            if (root["labsBlackoutEvent"] is JsonObject blackout)
                foreach (string name in new[] { "blackoutActivated", "powerRestored" })
                    if (blackout[name] is JsonObject phase) Phase(phase, "/labsBlackoutEvent/" + name);
            if (root["labsContainmentBreachEvent"] is JsonObject legacy)
                foreach (string path in NormalizeSpecialFormations(legacy, true)) changed.Add("/labsContainmentBreachEvent" + path);
        }
        return changed;
    }

    internal static bool IsSpecialFormation(JsonObject formation)
    {
        static string? Role(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var role) ? role : null;
        static int Count(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<int>(out int count) ? count : 1;
        return IsSpecialRole(Role(formation["boss"])) || formation["followers"] is JsonArray followers &&
            followers.OfType<JsonObject>().Any(follower => IsSpecialRole(Role(follower["type"])) &&
                Math.Max(Count(follower["min"]), Count(follower["max"])) > 0);
    }

    private static readonly string[] Isb = ["ISBSpecialForces", "ISBBossCommander", "ISBFollowerCommander", "ISBTeamLeader", "ISBSecondLeader", "ISBScout", "ISBHeavy", "ISBAssassin", "ISBMarksman"];
    private static readonly HashSet<string> BlackDiv = new(StringComparer.OrdinalIgnoreCase)
    {
        "blackDivLead",
        "blackDivAssault",
        "blackDivBreacher",
        "blackDivSupport",
        "blackDivIb",
        "bossWedge",
        "bossWedgeGhostBuster",
        "848420",
        "848421",
        "848422",
        "848423",
        "848424",
        "848426"
    };
    public static int PerformanceIsbReserve(LabsPreBlackoutEventConfig? config) => config?.performanceMode == true && !config.noISBMode && !config.noBlackDivMode && !config.whiteTuskOnly && !config.ghostBusterOnly && !config.chaosInsurgencyOnly ? config.containementBreachQueue.performanceNormal.isb : 0;

    public static ISB.Compatibility.EventPopulationConfig PopulationQueue(LabsPreBlackoutEventConfig config)
    {
        var queue = config.containementBreachQueue.ForPerformanceMode(config.performanceMode);
        if (!config.chaosInsurgencyOnly)
            return queue;
        return new ISB.Compatibility.EventPopulationConfig
        {
            enabled = queue.enabled,
            maximumBots = queue.maximumBots,
            squadOverflow = queue.squadOverflow,
            reinforcementIntervalSeconds = queue.reinforcementIntervalSeconds,
            reinforcementWindowSeconds = queue.reinforcementWindowSeconds,
            genericRefillDelaySeconds = queue.genericRefillDelaySeconds,
            normal = new() { blackDivision = 0, isb = 0, chaosInsurgency = queue.maximumBots },
            reinforcement = new() { blackDivision = 0, isb = 0, chaosInsurgency = queue.maximumBots },
            performanceMaximumBots = queue.performanceMaximumBots,
            performanceNormal = new() { blackDivision = 0, isb = 0, chaosInsurgency = queue.performanceMaximumBots },
            performanceReinforcement = new() { blackDivision = 0, isb = 0, chaosInsurgency = queue.performanceMaximumBots }
        };
    }

    public static void Validate(LabsPreBlackoutEventConfig config)
    {
        if (config.containementBreachQueue == null)
            throw new ArgumentException("containment_breach_config.jsonc:containementBreachQueue must be an object.");
        config.containementBreachQueue.Validate("containment_breach_config.jsonc:containementBreachQueue");
        if (!double.IsFinite(config.chaosInsurgencyHealthMultiplier) || config.chaosInsurgencyHealthMultiplier is <= 0 or > 5)
            throw new ArgumentException("containment_breach_config.jsonc:chaosInsurgencyHealthMultiplier must be greater than 0 and at most 5.");
        if (config.chaosInsurgencyOnly && (config.whiteTuskOnly || config.ghostBusterOnly))
            throw new InvalidDataException("ISB: chaosInsurgencyOnly cannot be combined with whiteTuskOnly or ghostBusterOnly.");
        if (config.noISBMode && config.noBlackDivMode)
            throw new InvalidDataException("ISB: noISBMode and noBlackDivMode cannot be enabled together.");
        if (config.whiteTuskOnly && (config.noISBMode || config.ghostBusterOnly))
            throw new InvalidDataException("ISB: whiteTuskOnly cannot be combined with noISBMode or ghostBusterOnly.");
        if (config.ghostBusterOnly && config.noBlackDivMode)
            throw new InvalidDataException("ISB: ghostBusterOnly cannot be combined with noBlackDivMode.");
    }

    public static string Role(string role, LabsPreBlackoutEventConfig config, Func<double> draw)
    {
        if (string.IsNullOrEmpty(role))
            return role;
        if (config.chaosInsurgencyOnly)
            return ISB.Compatibility.GhostBotVariant.ChaosName;
        if (ISB.Compatibility.GhostBotVariant.IsChaos(role))
            return ISB.Compatibility.GhostBotVariant.ChaosName;
        bool bd = BlackDiv.Contains(role) || ISB.Compatibility.GhostBotVariant.Is(role);
        bool isb = role.StartsWith("ISB", StringComparison.OrdinalIgnoreCase) || int.TryParse(role, out var number) && number >= 13701 && number <= 13716;
        int Pick(int length) => Math.Clamp((int)(draw() * length), 0, length - 1);
        if (config.noBlackDivMode && bd)
        {
            role = Isb[Pick(Isb.Length)];
            bd = false;
            isb = true;
        }
        else if (config.noISBMode && isb)
        {
            role = "blackDivAssault";
            isb = false;
            bd = true;
        }
        if (config.whiteTuskOnly)
            return WhiteTusks[Pick(WhiteTusks.Length)];
        if (config.ghostBusterOnly)
            return Ghosts[Pick(Ghosts.Length)];
        return role;
    }

    public static SpawnFormationConfig Formation(SpawnFormationConfig source, LabsPreBlackoutEventConfig? config, Func<double> draw, Func<int, int, int> count)
    {
        if (config == null || !config.whiteTuskOnly && !config.noISBMode && !config.noBlackDivMode && !config.ghostBusterOnly && !config.chaosInsurgencyOnly)
            return source;
        Validate(config);
        var result = new SpawnFormationConfig
        {
            id = source.id,
            weight = source.weight,
            enabled = source.enabled,
            chance = source.chance,
            squadCount = source.squadCount,
            boss = Role(source.boss ?? "ISBTeamLeader", config, draw),
            zones = source.zones,
            raidTimeMin = source.raidTimeMin,
            raidTimeMax = source.raidTimeMax,
            checkpointRadius = source.checkpointRadius,
            x = source.x,
            y = source.y,
            z = source.z
        };
        foreach (var follower in source.followers ?? [])
        {
            if (follower == null || string.IsNullOrWhiteSpace(follower.type))
                continue;
            var min = Math.Max(0, Math.Min(follower.min, follower.max));
            var max = Math.Max(min, Math.Max(follower.min, follower.max));
            var total = min == max ? min : count(min, max);
            for (int i = 0; i < total; i++)
                result.followers.Add(new() { type = Role(follower.type, config, draw), min = 1, max = 1 });
        }

        return result;
    }
}
