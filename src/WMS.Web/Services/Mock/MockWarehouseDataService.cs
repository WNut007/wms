using WMS.DAL.Common;

namespace WMS.Web.Services.Mock;

public class MockWarehouseDataService
{
    private static readonly List<WarehouseListItem> _seed = GenerateSeed();

    public PagedResult<WarehouseListItem> GetWarehouses(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? statusFilter = null,
        string? regionFilter = null,
        string? typeFilter = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var query = _seed.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(w =>
                w.Code.ToLowerInvariant().Contains(s) ||
                w.Name.ToLowerInvariant().Contains(s) ||
                (w.Subtitle != null && w.Subtitle.ToLowerInvariant().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            query = query.Where(w => w.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(regionFilter) && regionFilter != "any")
            query = query.Where(w => w.Region.Equals(regionFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(typeFilter) && typeFilter != "all")
            query = query.Where(w => w.Type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase));

        query = (sortBy.ToLowerInvariant(), sortDesc) switch
        {
            ("code",      false) => query.OrderBy(w => w.Code),
            ("code",      true)  => query.OrderByDescending(w => w.Code),
            ("name",      false) => query.OrderBy(w => w.Name),
            ("name",      true)  => query.OrderByDescending(w => w.Name),
            ("region",    false) => query.OrderBy(w => w.Region),
            ("region",    true)  => query.OrderByDescending(w => w.Region),
            ("locations", false) => query.OrderBy(w => w.LocationCount),
            ("locations", true)  => query.OrderByDescending(w => w.LocationCount),
            ("updatedat", false) => query.OrderBy(w => w.UpdatedAt),
            ("updatedat", true)  => query.OrderByDescending(w => w.UpdatedAt),
            _                    => query.OrderBy(w => w.Name),
        };

        var total = query.Count();
        var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<WarehouseListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public WarehouseListItem? GetByCode(string code) =>
        _seed.FirstOrDefault(w => w.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    private static List<WarehouseListItem> GenerateSeed()
    {
        var statuses  = new[] { "active", "active", "active", "active", "maintenance", "inactive" };
        var types     = new[] { "3PL", "3PL", "Internal", "Internal", "3PL" };
        var regions   = new[] { "Bangkok, TH", "Chiang Mai, TH", "Phuket, TH", "Khon Kaen, TH", "Samut Prakan, TH", "Pattaya, TH" };
        var subtitles = new[] { "Headquarters", "Regional", "Coastal hub", "Distribution", "Forward DC", "Cold storage", "Bulk storage" };

        // Fixed seed so the mock list stays stable across requests.
        var rng = new Random(42);
        var list = new List<WarehouseListItem>
        {
            new("WH-MAIN",  "Bangkok Main",       "Headquarters", "Bangkok, TH",      "3PL",      "active",      1247, DateTime.UtcNow.AddHours(-2)),
            new("WH-CM01",  "Chiang Mai Hub",     "Regional",     "Chiang Mai, TH",   "3PL",      "active",       486, DateTime.UtcNow.AddHours(-5)),
            new("WH-PKT01", "Phuket South",       "Coastal hub",  "Phuket, TH",       "3PL",      "active",       312, DateTime.UtcNow.AddDays(-1)),
            new("WH-KK01",  "Khon Kaen East",     "Distribution", "Khon Kaen, TH",    "Internal", "maintenance",  198, DateTime.UtcNow.AddDays(-3)),
            new("WH-HQ02",  "Bangkok Sukhumvit",  "Forward DC",   "Bangkok, TH",      "Internal", "active",       854, DateTime.UtcNow.AddHours(-6)),
            new("WH-SP01",  "Samut Prakan Cold",  "Cold storage", "Samut Prakan, TH", "3PL",      "inactive",       0, DateTime.UtcNow.AddDays(-14)),
        };

        for (int i = 1; i <= 44; i++)
        {
            var region = regions[rng.Next(regions.Length)];
            var city = region.Split(',')[0];
            var cityPrefix = city.Replace(" ", "").Substring(0, Math.Min(3, city.Replace(" ", "").Length)).ToUpperInvariant();
            var code = $"WH-{cityPrefix}{i:D2}";
            var status = statuses[rng.Next(statuses.Length)];
            var type = types[rng.Next(types.Length)];
            var subtitle = subtitles[rng.Next(subtitles.Length)];
            var name = $"{city} {subtitle.Split(' ')[0]} {i:D2}";
            var locations = status == "inactive" ? 0 : rng.Next(50, 1500);
            var updated = DateTime.UtcNow.AddHours(-rng.Next(1, 720));

            list.Add(new WarehouseListItem(code, name, subtitle, region, type, status, locations, updated));
        }

        return list;
    }
}

public record WarehouseListItem(
    string Code,
    string Name,
    string? Subtitle,
    string Region,
    string Type,
    string Status,
    int LocationCount,
    DateTime UpdatedAt);
