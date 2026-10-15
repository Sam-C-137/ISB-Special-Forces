using System.Globalization;
using System.Text.RegularExpressions;

namespace ISBSpecialForces.Web;
public static class IsbDogtagCatalog
{
    public static bool IsSelectable(string id, bool? questItem) => questItem != true && id is not ("6963dede566c81e5c5dc9b4a" or "6966abf2e6001880baf0ada0");
    public static string Label(string id, string? internalName, string localizedName)
    {
        var known = id switch
        {
            "6a461aed7391ab085a093760" => "Black Division Dogtag — Ferrum (Tier 1)",
            "6a461bf82b2264dbe10d0ee6" => "Black Division Dogtag — Green (Tier 2)",
            "6a461c41ec88c6b9a509fb17" => "Black Division Dogtag — Red (Tier 3)",
            "5b9b9020e7ef6f5716480215" => "Dogtag USEC — Twitch",
            "68df9908972cf1e1ec07256a" => "Dogtag BEAR — And Survived",
            "68df991142e96f583d042b8a" => "Dogtag BEAR — In the Darkness",
            "68df991dbeba86572e097ceb" => "Dogtag BEAR — Didn't Escape from Yourself",
            "68df9927a38a5e37d80df6c9" => "Dogtag BEAR — For Humanity",
            "68df99975d4e135b130392ca" => "Dogtag USEC — And Survived",
            "68df99a614ca2428b2017cd8" => "Dogtag USEC — In the Darkness",
            "68df99b2f716906506098308" => "Dogtag USEC — Didn't Escape from Yourself",
            "68df99c05d4e135b130392cc" => "Dogtag USEC — For Humanity",
            "68f0f60a121d878a2303eedb" => "Dogtag BEAR — Prestige 5",
            "68f0f63c645c14a02104142a" => "Dogtag BEAR — Prestige 6",
            "68f0f64f183146ea530330aa" => "Dogtag USEC — Prestige 5",
            "68f0f662859ebec8d501b76a" => "Dogtag USEC — Prestige 6",
            "68f153aa7da590b6df0515da" => "Dogtag BEAR — Twitch",
            "68f15cf222c8979ee308f495" => "Dogtag BEAR — Tarkov 2025 (Square)",
            "68f15dbab2b53abd200b9378" => "Dogtag USEC — Tarkov 2025 (Square)",
            "68f15e26f1aa7e100a0ca208" => "Dogtag BEAR — Tarkov 2025 (Melted)",
            "68f15e53103c5d9d4f022c78" => "Dogtag USEC — Tarkov 2025 (Melted)",
            "68fb41120760c7891606613c" => "Dogtag BEAR — Acolyte",
            "68fb412b0760c7891606613e" => "Dogtag BEAR — Immortal",
            "68fb4143a854bc7ae80fad3e" => "Dogtag USEC — Acolyte",
            "68fb4157b280c103230e3b3c" => "Dogtag USEC — Immortal",
            "6a3548e72a963bcdd8098649" => "Dogtag USEC — Double",
            "6a3549435288a3fe6e085224" => "Dogtag BEAR — Double",
            "6a354a077e4ccdbe95005266" => "Dogtag USEC — Black Division Ferrum",
            "6a354a30652075cf460944c6" => "Dogtag BEAR — Black Division Ferrum",
            "6a354a6240e6797a930312e5" => "Dogtag USEC — Black Division Green",
            "6a354a6f8d465a55c70065c8" => "Dogtag USEC — Black Division Red",
            "6a354a7e6632508bd10476d8" => "Dogtag USEC — Black Division Old",
            "6a354a9a9cd7a91b45027a89" => "Dogtag BEAR — Black Division Green",
            "6a354aa56632508bd10476dc" => "Dogtag BEAR — Black Division Red",
            "6a354abe73339990030ca469" => "Dogtag BEAR — Black Division Old",
            _ => null
        };
        if (known != null)
            return known;
        var name = internalName?.ToLowerInvariant() ?? "";
        if (!name.Contains("dogtag"))
            return localizedName;
        var faction = name.Contains("_bear") ? "BEAR" : name.Contains("_usec") ? "USEC" : null;
        if (faction == null)
            return localizedName;
        var prestige = Regex.Match(name, @"_prestige_0*(\d+)(?:_|$)", RegexOptions.CultureInvariant);
        if (prestige.Success && int.TryParse(prestige.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            return $"Dogtag {faction} — Prestige {level}";
        if (name.EndsWith("_eod", StringComparison.Ordinal))
            return $"Dogtag {faction} — Edge of Darkness";
        if (name.EndsWith("_tue", StringComparison.Ordinal))
            return $"Dogtag {faction} — The Unheard Edition";
        if (name == "dogtags_bear" || name == "dogtags_usec")
            return $"Dogtag {faction} — Standard";
        return localizedName;
    }
}
