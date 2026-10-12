namespace TornCommander;

internal static class TornRoutes
{
    public const string Home = "https://www.torn.com/";
    public const string Crimes = "https://www.torn.com/page.php?sid=crimes";
    // Current Crimes 2.0 deep links use page.php + the crime hash route.
    public const string Graffiti = "https://www.torn.com/page.php?sid=crimes#/graffiti";
    public const string Racing = "https://www.torn.com/page.php?sid=racing";
    public const string Education = "https://www.torn.com/page.php?sid=education#/step=main";
    public const string Faction = "https://www.torn.com/factions.php?step=your";
    public const string Company = "https://www.torn.com/companies.php?step=your";
    public const string Gym = "https://www.torn.com/gym.php";
    public const string Items = "https://www.torn.com/item.php";
    public const string Bazaar = "https://www.torn.com/page.php?sid=bazaar";
    public const string Casino = "https://www.torn.com/casino.php";
    public const string Awards = "https://www.torn.com/awards.php";
    public const string ItemMarket = "https://www.torn.com/page.php?sid=ItemMarket";
    public const string Points = "https://www.torn.com/page.php?sid=points";
    public const string Recruit = "https://www.torn.com/bringafriend.php";
    public const string Forums = "https://www.torn.com/forums.php";
    public const string FullAccessApiKey = "https://www.torn.com/preferences.php#tab=api?step=addNewKey&title=Torn%20Commander%20Full%20Access&type=4";

    public static string ItemMarketFor(int itemId)
        => $"https://www.torn.com/page.php?sid=ItemMarket#/market/view=search&itemID={itemId}";

    public static string Normalize(string? raw)
    {
        var url = string.IsNullOrWhiteSpace(raw) ? Home : raw.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Home;

        return FixLegacyTornRoute(uri).ToString();
    }

    public static Uri FixLegacyTornRoute(Uri uri)
    {
        if (!IsTornHost(uri.Host)) return uri;

        // Torn retired direct loader.php navigation in favor of page.php for SID pages.
        // This includes Crimes 2.0 hash routes (for example page.php?sid=crimes#/graffiti).
        // Only top-level browser navigation is rewritten; page XHR/fetch requests are untouched.
        if (uri.AbsolutePath.Equals("/loader.php", StringComparison.OrdinalIgnoreCase)
            && uri.Query.Contains("sid=", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri) { Path = "/page.php" };
            return builder.Uri;
        }

        return uri;
    }

    public static bool IsTornHost(string host)
        => host.Equals("torn.com", StringComparison.OrdinalIgnoreCase)
           || host.Equals("www.torn.com", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".torn.com", StringComparison.OrdinalIgnoreCase);
}
