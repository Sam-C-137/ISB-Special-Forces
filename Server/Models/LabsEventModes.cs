namespace ISBSpecialForcesServer;
public static class LabsEventModes
{
    private static readonly string[] WhiteTusks = ["ISBBossFirefly", "ISBFollowerVipper", "ISBFollowerLoghan", "ISBFollowerSpearthrower", "ISBFollowerZofia", "ISBFollowerEvernight", "ISBFollowerNemesis"];
    private static readonly string[] Ghosts = ["blackDivGhostBusterLead", "blackDivGhostBuster01", "blackDivGhostBuster02", "blackDivGhostBuster03", "blackDivGhostBuster04"];
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
    public static void Validate(LabsPreBlackoutEventConfig config)
    {
        if (config.noISBMode && config.noBlackDivMode)
            throw new InvalidDataException("ISB: noISBMode and noBlackDivMode cannot be enabled together.");
    }

    public static string Role(string role, LabsPreBlackoutEventConfig config, Func<double> draw)
    {
        if (string.IsNullOrEmpty(role))
            return role;
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
        if (config.whiteTuskOnly && isb)
            return WhiteTusks[Pick(WhiteTusks.Length)];
        if (config.ghostBusterOnly && bd)
            return Ghosts[Pick(Ghosts.Length)];
        return role;
    }

    public static SpawnFormationConfig Formation(SpawnFormationConfig source, LabsPreBlackoutEventConfig? config, Func<double> draw, Func<int, int, int> count)
    {
        if (config == null || !config.whiteTuskOnly && !config.noISBMode && !config.noBlackDivMode && !config.ghostBusterOnly)
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
