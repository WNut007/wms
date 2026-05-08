using WMS.DAL.Common;

namespace WMS.Web.Services.Mock;

public class MockCustomerDataService
{
    private static readonly List<CustomerListItem> _seed = GenerateSeed();

    public PagedResult<CustomerListItem> GetCustomers(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? statusFilter = null,
        string? typeFilter = null,
        string? countryFilter = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var query = _seed.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.Code.ToLowerInvariant().Contains(s) ||
                c.Name.ToLowerInvariant().Contains(s) ||
                c.Email.ToLowerInvariant().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            query = query.Where(c => c.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(typeFilter) && typeFilter != "all")
            query = query.Where(c => c.Type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(countryFilter) && countryFilter != "any")
            query = query.Where(c => c.Country.Equals(countryFilter, StringComparison.OrdinalIgnoreCase));

        query = (sortBy.ToLowerInvariant(), sortDesc) switch
        {
            ("code",          false) => query.OrderBy(c => c.Code),
            ("code",          true)  => query.OrderByDescending(c => c.Code),
            ("name",          false) => query.OrderBy(c => c.Name),
            ("name",          true)  => query.OrderByDescending(c => c.Name),
            ("type",          false) => query.OrderBy(c => c.Type),
            ("type",          true)  => query.OrderByDescending(c => c.Type),
            ("country",       false) => query.OrderBy(c => c.Country),
            ("country",       true)  => query.OrderByDescending(c => c.Country),
            ("totalorders",   false) => query.OrderBy(c => c.TotalOrders),
            ("totalorders",   true)  => query.OrderByDescending(c => c.TotalOrders),
            ("lastorderdate", false) => query.OrderBy(c => c.LastOrderDate),
            ("lastorderdate", true)  => query.OrderByDescending(c => c.LastOrderDate),
            _                        => query.OrderBy(c => c.Name),
        };

        var total = query.Count();
        var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<CustomerListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public CustomerListItem? GetByCode(string code) =>
        _seed.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    private static List<CustomerListItem> GenerateSeed()
    {
        var avatarColors = new[] { "#534AB7", "#0C447C", "#27500A", "#854F0B", "#A32D2D", "#3C3489", "#085041", "#BA7517" };
        var countries = new[] { "Thailand", "Singapore", "Vietnam", "Malaysia", "Indonesia", "Philippines" };
        var statuses = new[] { "active", "active", "active", "active", "pending", "inactive" };
        var types = new[] { "B2B", "B2C", "B2C", "B2C", "B2B" };

        var firstNames = new[] { "Somchai", "Ananya", "Pim", "Nattakorn", "Wichai", "Suda", "Nat", "Apinya", "Thanat", "Mali" };
        var lastNames  = new[] { "Phongphan", "Srisuk", "Chai", "Boonrueng", "Kaewkamol", "Ngamtrakulchol", "Jaidee", "Saengthong" };
        var companies  = new[] { "Bangkok Trading", "TechCorp", "Acme", "Pacific Imports", "Sky Foods", "Northstar", "Bluebird Supply", "Indigo Group" };

        var rng = new Random(7);
        var list = new List<CustomerListItem>
        {
            new("CUS-0001", "Bangkok Trading Co., Ltd.", "B2B", "Thailand",  "ops@bangkoktrading.co.th",  "BT", "#534AB7", 312, DateTime.UtcNow.AddDays(-2),  "active"),
            new("CUS-0002", "TechCorp Solutions",        "B2B", "Singapore", "purchasing@techcorp.sg",    "TC", "#0C447C", 184, DateTime.UtcNow.AddDays(-5),  "active"),
            new("CUS-0003", "Ananya S.",                 "B2C", "Thailand",  "ananya.s@gmail.com",        "AS", "#27500A",   8, DateTime.UtcNow.AddDays(-12), "active"),
            new("CUS-0004", "Pacific Imports",           "B2B", "Vietnam",   "ceo@pacific-imports.vn",    "PI", "#854F0B",  72, DateTime.UtcNow.AddDays(-8),  "pending"),
        };

        for (int i = 5; i <= 40; i++)
        {
            var type = types[rng.Next(types.Length)];
            var status = statuses[rng.Next(statuses.Length)];
            var country = countries[rng.Next(countries.Length)];
            var color = avatarColors[rng.Next(avatarColors.Length)];

            string name, email, initials;
            if (type == "B2B")
            {
                var company = companies[rng.Next(companies.Length)];
                name = $"{company} #{i:D2}";
                email = $"contact@{company.Replace(" ", "").ToLowerInvariant()}{i:D2}.com";
                initials = string.Concat(company.Split(' ').Take(2).Select(s => s[0])).ToUpperInvariant();
            }
            else
            {
                var fn = firstNames[rng.Next(firstNames.Length)];
                var ln = lastNames[rng.Next(lastNames.Length)];
                name = $"{fn} {ln}";
                email = $"{fn.ToLowerInvariant()}.{ln.ToLowerInvariant()}@example.com";
                initials = $"{fn[0]}{ln[0]}".ToUpperInvariant();
            }

            var totalOrders = type == "B2B" ? rng.Next(20, 400) : rng.Next(1, 30);
            var lastOrder = DateTime.UtcNow.AddDays(-rng.Next(1, 90));
            var code = $"CUS-{i:D4}";

            list.Add(new CustomerListItem(code, name, type, country, email, initials, color, totalOrders, lastOrder, status));
        }

        return list;
    }
}

public record CustomerListItem(
    string Code,
    string Name,
    string Type,
    string Country,
    string Email,
    string Initials,
    string AvatarColor,
    int TotalOrders,
    DateTime LastOrderDate,
    string Status);
