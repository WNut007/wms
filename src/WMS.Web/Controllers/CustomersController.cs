using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Web.Services;
using WMS.Web.Services.Mock;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

[Authorize]
[Route("Customers")]
public class CustomersController : Controller
{
    private readonly MockCustomerDataService _data;
    private readonly IDocumentStorageService _docs;

    public CustomersController(MockCustomerDataService data, IDocumentStorageService docs)
    {
        _data = data;
        _docs = docs;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public IActionResult GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? type = null,
        string? country = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var result = _data.GetCustomers(page, pageSize, search, status, type, country, sortBy, sortDesc);
        return Json(new
        {
            items = result.Items.Select(c => new
            {
                code              = c.Code,
                name              = c.Name,
                type              = c.Type,
                country           = c.Country,
                email             = c.Email,
                initials          = c.Initials,
                avatarColor       = c.AvatarColor,
                totalOrders       = c.TotalOrders,
                lastOrderDate     = c.LastOrderDate,
                lastOrderRelative = RelativeTime.Format(c.LastOrderDate),
                status            = c.Status,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
        });
    }

    [HttpGet("Detail/{code}")]
    public async Task<IActionResult> Detail(string code, CancellationToken ct)
    {
        var customer = _data.GetByCode(code);
        if (customer == null) return NotFound();

        var docs = await _docs.ListByEntityAsync("Customer", code, ct);

        var (statusLabel, statusVariant) = customer.Status switch
        {
            "active"   => ("Active", "success"),
            "pending"  => ("Pending", "warning"),
            _          => ("Inactive", "neutral"),
        };

        var vm = new DetailPageViewModel
        {
            EntityType = "Customer",
            EntityId = customer.Code,
            Title = customer.Name,
            Subtitle = $"{customer.Code} · {customer.Type} · {customer.Country}",
            IconClass = customer.Type == "B2B" ? "ti-building" : "ti-user",
            IconBgColor = $"{customer.AvatarColor}1A",
            IconFgColor = customer.AvatarColor,
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Customers",
            BreadcrumbParentUrl = "/Customers",
            Stats = new()
            {
                new("Total orders", customer.TotalOrders.ToString("N0")),
                new("YTD revenue", $"฿{customer.TotalOrders * 4500:N0}", "#534AB7"),
                new("Last order",  customer.LastOrderDate.ToString("MMM dd")),
                new("Avg ticket",  "฿4,500"),
            },
            ShowImagesTab = false,
            Documents = docs.Select(d => new DocumentItem(
                d.DocumentId,
                d.FileName,
                d.Category,
                CategoryColorBg(d.Category),
                CategoryColorFg(d.Category),
                d.IconClass,
                d.IconColorBg,
                d.IconColorFg,
                d.FileSizeFormatted,
                d.UploadedBy,
                d.UploadedAt,
                RelativeTime.Format(d.UploadedAt)
            )).ToList(),
            Activities = new()
            {
                new($"<span style=\"font-weight:500\">{System.Net.WebUtility.HtmlEncode(customer.Name)}</span> placed order",
                    "SO-2026-0438 · 4 lines · ฿18,200", "ti-shopping-cart", "#534AB7",
                    customer.LastOrderDate, RelativeTime.Format(customer.LastOrderDate), "Recent"),
                new("<span style=\"font-weight:500\">Sales team</span> sent quote",
                    "Q-2026-0214 · 12 lines", "ti-mail", "#0C447C",
                    DateTime.UtcNow.AddDays(-7), "1 w ago", "1 week ago"),
                new("<span style=\"font-weight:500\">System Admin</span> uploaded contract",
                    "Customer_Agreement.pdf · 980 KB", "ti-file-plus", "#534AB7",
                    DateTime.UtcNow.AddDays(-60), "2 mo ago", "2 months ago"),
                new("<span style=\"font-weight:500\">Sales team</span> created customer",
                    $"{customer.Code} added to CRM", "ti-plus", "#888780",
                    DateTime.UtcNow.AddDays(-180), "6 mo ago", "6 months ago"),
            },
            QuickActions = new()
            {
                new("Create order",   "ti-shopping-cart", "#"),
                new("Send email",     "ti-mail",          $"mailto:{customer.Email}"),
                new("View invoices",  "ti-receipt",       "#"),
            },
            OverviewFields = new()
            {
                new("Code",    $"<span style=\"font-family: var(--wms-font-mono);\">{customer.Code}</span>"),
                new("Type",    System.Net.WebUtility.HtmlEncode(customer.Type)),
                new("Country", System.Net.WebUtility.HtmlEncode(customer.Country)),
                new("Email",   $"<a href=\"mailto:{System.Net.WebUtility.HtmlEncode(customer.Email)}\" style=\"color: var(--wms-primary); text-decoration: none;\">{System.Net.WebUtility.HtmlEncode(customer.Email)}</a>"),
                new("Phone",   "+66 2 123 4567"),
                new("Tax ID",  "0107537001234"),
            },
            Properties = new()
            {
                new("Created",      "6 mo ago"),
                new("Last contact", "2 w ago"),
                new("Owner",        "Sales team"),
            }
        };

        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    private static string CategoryColorBg(string cat) => cat switch
    {
        "Specification" => "#E6F1FB",
        "Manual"        => "#EEEDFE",
        "Pricing"       => "#FAEEDA",
        "Certificate"   => "#E1F5EE",
        "Contract"      => "#FCEBEB",
        _               => "#F1EFE8",
    };

    private static string CategoryColorFg(string cat) => cat switch
    {
        "Specification" => "#0C447C",
        "Manual"        => "#3C3489",
        "Pricing"       => "#854F0B",
        "Certificate"   => "#085041",
        "Contract"      => "#A32D2D",
        _               => "#444441",
    };
}
