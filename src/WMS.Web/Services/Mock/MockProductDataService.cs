namespace WMS.Web.Services.Mock;

public class MockProductDataService
{
    private static readonly List<ProductListItem> _seed = GenerateSeed();

    public PagedResult<ProductListItem> GetProducts(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? statusFilter = null,
        string? categoryFilter = null,
        string? brandFilter = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var query = _seed.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Sku.ToLowerInvariant().Contains(s) ||
                p.Name.ToLowerInvariant().Contains(s) ||
                p.Brand.ToLowerInvariant().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            query = query.Where(p => p.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != "all")
            query = query.Where(p => p.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(brandFilter) && brandFilter != "all")
            query = query.Where(p => p.Brand.Equals(brandFilter, StringComparison.OrdinalIgnoreCase));

        query = (sortBy.ToLowerInvariant(), sortDesc) switch
        {
            ("sku",       false) => query.OrderBy(p => p.Sku),
            ("sku",       true)  => query.OrderByDescending(p => p.Sku),
            ("name",      false) => query.OrderBy(p => p.Name),
            ("name",      true)  => query.OrderByDescending(p => p.Name),
            ("brand",     false) => query.OrderBy(p => p.Brand),
            ("brand",     true)  => query.OrderByDescending(p => p.Brand),
            ("category",  false) => query.OrderBy(p => p.Category),
            ("category",  true)  => query.OrderByDescending(p => p.Category),
            ("price",     false) => query.OrderBy(p => p.Price),
            ("price",     true)  => query.OrderByDescending(p => p.Price),
            ("stock",     false) => query.OrderBy(p => p.StockOnHand),
            ("stock",     true)  => query.OrderByDescending(p => p.StockOnHand),
            ("updatedat", false) => query.OrderBy(p => p.UpdatedAt),
            ("updatedat", true)  => query.OrderByDescending(p => p.UpdatedAt),
            _                    => query.OrderBy(p => p.Name),
        };

        var total = query.Count();
        var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<ProductListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public ProductListItem? GetBySku(string sku) =>
        _seed.FirstOrDefault(p => p.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));

    private static List<ProductListItem> GenerateSeed()
    {
        // Category metadata: icon + brand color tint
        var categories = new (string Name, string IconClass, string Color)[]
        {
            ("Electronics", "ti-device-laptop", "#534AB7"),
            ("Apparel",     "ti-shirt",         "#0C447C"),
            ("Food",        "ti-cookie",        "#854F0B"),
            ("Beauty",      "ti-sparkles",      "#A32D2D"),
            ("Home",        "ti-armchair",      "#27500A"),
            ("Toys",        "ti-puzzle",        "#BA7517"),
        };

        var brands = new[]
        {
            "Apple", "Samsung", "Sony", "LG", "Nike", "Adidas",
            "Uniqlo", "Zara", "Nestle", "Unilever", "L'Oreal",
            "IKEA", "Lego", "Hasbro", "Generic"
        };

        var statuses = new[] { "active", "active", "active", "active", "active", "out_of_stock", "discontinued" };

        var rng = new Random(42);
        var list = new List<ProductListItem>
        {
            new("SKU-000001", "MacBook Pro 14-inch M3",      "Apple",  "Electronics", "ti-device-laptop", "#534AB7", 69_900m, 847, "active",       DateTime.UtcNow.AddHours(-2)),
            new("SKU-000002", "iPhone 15 Pro 256GB",         "Apple",  "Electronics", "ti-device-mobile", "#534AB7", 42_900m, 1_240, "active",     DateTime.UtcNow.AddHours(-5)),
            new("SKU-000003", "Galaxy S24 Ultra",            "Samsung","Electronics", "ti-device-mobile", "#534AB7", 39_900m, 612, "active",       DateTime.UtcNow.AddHours(-12)),
            new("SKU-000004", "AirPods Pro (2nd gen)",       "Apple",  "Electronics", "ti-headphones",    "#534AB7",  8_900m, 2_104, "active",     DateTime.UtcNow.AddDays(-1)),
            new("SKU-000005", "Air Force 1 White - 42",      "Nike",   "Apparel",     "ti-shoe",          "#0C447C",  3_900m, 320, "active",       DateTime.UtcNow.AddDays(-2)),
            new("SKU-000006", "U3 Heattech Crew - L",        "Uniqlo", "Apparel",     "ti-shirt",         "#0C447C",    490m, 0, "out_of_stock", DateTime.UtcNow.AddDays(-3)),
        };

        for (int i = 7; i <= 50; i++)
        {
            var cat = categories[rng.Next(categories.Length)];
            var brand = brands[rng.Next(brands.Length)];
            var status = statuses[rng.Next(statuses.Length)];
            var price = (decimal)(rng.NextDouble() * 50_000 + 100);
            price = Math.Round(price / 10) * 10;
            var stock = status == "out_of_stock" ? 0
                      : status == "discontinued" ? rng.Next(0, 20)
                      : rng.Next(20, 2_500);
            var name = $"{brand} {cat.Name} Item {i:D3}";
            var sku = $"SKU-{i:D6}";
            var updated = DateTime.UtcNow.AddHours(-rng.Next(1, 720));

            list.Add(new ProductListItem(
                sku, name, brand, cat.Name, cat.IconClass, cat.Color,
                price, stock, status, updated));
        }

        return list;
    }
}

public record ProductListItem(
    string Sku,
    string Name,
    string Brand,
    string Category,
    string IconClass,
    string IconColor,
    decimal Price,
    int StockOnHand,
    string Status,
    DateTime UpdatedAt);
