namespace ISBSpecialForcesServer;
public static class BlackDivisionPresetPolicy
{
    public const string ModGuid = "com.blackdiv.tacticaltoaster";
    public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["848420"] = "blackDivLead",
        ["848421"] = "blackDivAssault",
        ["848422"] = "blackDivBreacher",
        ["848423"] = "blackDivSupport",
        ["848424"] = "bossWedge",
        ["848426"] = "blackDivIb",
        ["blackDivLead"] = "blackDivLead",
        ["blackDivAssault"] = "blackDivAssault",
        ["blackDivBreacher"] = "blackDivBreacher",
        ["blackDivSupport"] = "blackDivSupport",
        ["bossWedge"] = "bossWedge",
        ["blackDivIb"] = "blackDivIb"
    };
    public static bool IsRole(string role) => role != null && Aliases.ContainsKey(role);
    public static bool Enabled(bool installed, BotPresetConfig? config) => installed && config?.enabled == true;
}
