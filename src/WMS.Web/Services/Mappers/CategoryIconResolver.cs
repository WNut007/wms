namespace WMS.Web.Services.Mappers;

// Maps a Product's CategoryCode → (Tabler icon class, hex color) for
// the Detail page hero + list-row badges. Lifted from the static seed
// dictionary in MockProductDataService.GenerateSeed so the visual
// language stays identical after the controller swap in T8.
//
// The seeded master.ProductCategories table only ships the 'DEMO'
// category today (migration 052). The richer mapping here covers the
// codes that will be added when categories are properly seeded —
// keeping it in one place avoids a churn migration when that lands.
//
// Unknown / NULL category codes fall back to a neutral box icon —
// safer than a misleading "fits the wrong product" icon.
public static class CategoryIconResolver
{
    private static readonly IReadOnlyDictionary<string, (string Icon, string Color)> Map =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["DEMO"]        = ("ti-package",       "#534AB7"),
            ["ELECTRONICS"] = ("ti-device-laptop", "#534AB7"),
            ["APPAREL"]     = ("ti-shirt",         "#0C447C"),
            ["FOOD"]        = ("ti-cookie",        "#854F0B"),
            ["BEAUTY"]      = ("ti-sparkles",      "#A32D2D"),
            ["HOME"]        = ("ti-armchair",      "#27500A"),
            ["TOYS"]        = ("ti-puzzle",        "#BA7517"),
        };

    public static (string Icon, string Color) Resolve(string? categoryCode) =>
        categoryCode is not null && Map.TryGetValue(categoryCode, out var v)
            ? v
            : ("ti-box", "#888780");
}
