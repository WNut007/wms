namespace WMS.Web.Services.Auth;

// Static city → Thai-region mapping for the Phase 8.5 warehouse
// picker. Address values come in as "{City}, {Country}" or just
// "{City}" — we parse out the leading City segment and look it up.
//
// This is intentionally NOT a database column (TD-018 candidate if
// the business cares about formal regions later). City names are
// the natural, human-readable region anchor for Thai logistics;
// when more cities land, just extend the dictionary.
//
// Unknown cities fall to "Other" — Singapore, Vietnam, etc. cluster
// under that bucket. Bias is to keep the picker usable without
// requiring a schema migration.
public static class WarehouseRegionResolver
{
    public const string OtherRegion = "Other";

    private static readonly Dictionary<string, string> CityToRegion =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Bangkok metropolitan ---
            ["Bangkok"]            = "Bangkok",
            ["Samut Prakan"]       = "Bangkok",
            ["Nonthaburi"]         = "Bangkok",
            ["Pathum Thani"]       = "Bangkok",
            ["Samut Sakhon"]       = "Bangkok",

            // --- North ---
            ["Chiang Mai"]         = "North",
            ["Chiang Rai"]         = "North",
            ["Lampang"]            = "North",
            ["Mae Sot"]            = "North",
            ["Phitsanulok"]        = "North",

            // --- Northeast (Isan) ---
            ["Khon Kaen"]          = "Northeast",
            ["Nakhon Ratchasima"]  = "Northeast",
            ["Korat"]              = "Northeast",
            ["Udon Thani"]         = "Northeast",
            ["Ubon Ratchathani"]   = "Northeast",

            // --- Central ---
            ["Ayutthaya"]          = "Central",
            ["Kanchanaburi"]       = "Central",
            ["Saraburi"]           = "Central",
            ["Lopburi"]            = "Central",

            // --- East (Eastern Seaboard) ---
            ["Rayong"]             = "East",
            ["Chonburi"]           = "East",
            ["Pattaya"]            = "East",
            ["Trat"]               = "East",
            ["Chanthaburi"]        = "East",

            // --- South ---
            ["Phuket"]             = "South",
            ["Hat Yai"]            = "South",
            ["Surat Thani"]        = "South",
            ["Krabi"]              = "South",
            ["Nakhon Si Thammarat"]= "South",
            ["Songkhla"]           = "South",
        };

    // Region order for grouped display. "Other" sinks to end.
    public static readonly IReadOnlyList<string> RegionDisplayOrder =
        new[] { "Bangkok", "North", "Northeast", "Central", "East", "South", OtherRegion };

    // Parse the city (everything before the first comma) and look up
    // the region. Empty / unrecognised → "Other".
    public static string Resolve(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return OtherRegion;
        var commaIdx = address.IndexOf(',');
        var city = (commaIdx > 0 ? address[..commaIdx] : address).Trim();
        return CityToRegion.TryGetValue(city, out var region) ? region : OtherRegion;
    }

    // Display-only city extracted from Address (e.g. "Bangkok, TH" →
    // "Bangkok"). Used in the picker subtitle.
    public static string City(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "—";
        var commaIdx = address.IndexOf(',');
        return (commaIdx > 0 ? address[..commaIdx] : address).Trim();
    }
}
