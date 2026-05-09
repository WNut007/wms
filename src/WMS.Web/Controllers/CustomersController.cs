using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Read-side here; admin Create / Edit lives in
// CustomersController.Admin.cs (partial).
[Authorize]
[Route("Customers")]
public partial class CustomersController : Controller
{
    private readonly ICustomerRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly IDocumentStorageService _docs;
    private readonly ICarrierRepositoryFactory _carrierRepos;
    private readonly IValidator<CustomerCreateViewModel> _createValidator;
    private readonly IValidator<CustomerEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public CustomersController(
        ICustomerRepositoryFactory repos,
        ITenantContext tenant,
        IDocumentStorageService docs,
        ICarrierRepositoryFactory carrierRepos,
        IValidator<CustomerCreateViewModel> createValidator,
        IValidator<CustomerEditViewModel> editValidator,
        ICurrentUser currentUser)
    {
        _repos = repos;
        _tenant = tenant;
        _docs = docs;
        _carrierRepos = carrierRepos;
        _createValidator = createValidator;
        _editValidator = editValidator;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? type = null,
        string? country = null,
        string sortBy = "name",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new CustomerFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            // Wire format → DB-canonical PascalCase. Mock 'pending' →
            // 'Draft' handled inside the mapper.
            Status: CustomerStatusMapper.FromWire(status),
            CustomerType: NormaliseFilter(type),
            Country: NormaliseCountry(country),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var result = await _repos.For(_tenant.RequireTenantId())
                                 .GetPagedAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(c => new
            {
                code              = c.Code,
                name              = c.Name,
                type              = c.CustomerType,
                country           = c.Country,
                email             = c.Email,
                initials          = CustomerAvatar.Initials(c.Name),
                avatarColor       = CustomerAvatar.Color(c.Name),
                // TD-011 — no orders schema yet. Frontend renders '—'
                // for nulls (fmtNumber + lastOrderRelative both guarded
                // against null).
                totalOrders       = (int?)null,
                lastOrderDate     = (DateTime?)null,
                lastOrderRelative = "—",
                status            = CustomerStatusMapper.ToWire(c.Status),
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
        var customer = await _repos.For(_tenant.RequireTenantId())
                                   .GetByCodeAsync(code, ct);
        if (customer is null) return NotFound();

        var docs = await _docs.ListByEntityAsync("Customer", code, ct);

        var (statusLabel, statusVariant) = customer.Status switch
        {
            "Active"    => ("Active", "success"),
            "Suspended" => ("Suspended", "warning"),
            "Draft"     => ("Pending", "warning"),
            _           => ("Inactive", "neutral"),
        };

        var avatarColor = CustomerAvatar.Color(customer.Name);

        var vm = new DetailPageViewModel
        {
            EntityType = "Customer",
            EntityId = customer.Code,
            Title = customer.Name,
            Subtitle = $"{customer.Code} · {customer.CustomerType} · {customer.Country ?? "—"}",
            IconClass = customer.CustomerType == "B2B" ? "ti-building" : "ti-user",
            IconBgColor = $"{avatarColor}1A",
            IconFgColor = avatarColor,
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Customers",
            BreadcrumbParentUrl = "/Customers",
            Stats = new()
            {
                // TD-011 — no orders schema, all four tiles stubbed.
                // Wire when Phase 7+ orders implementation lands.
                new("Total orders", "—"),
                new("YTD revenue",  "—"),
                new("Last order",   "—"),
                new("Avg ticket",   "—"),
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
            // TD-010-style — Activity stays hardcoded until an order
            // and CRM activity stream lands. Customer activity would
            // pull from inbound.PurchaseOrders + outbound.SalesOrders
            // + Customer notes (Phase 7+).
            Activities = new()
            {
                new($"<span style=\"font-weight:500\">{System.Net.WebUtility.HtmlEncode(customer.Name)}</span> placed order",
                    "SO-2026-0438 · 4 lines · ฿18,200", "ti-shopping-cart", "#534AB7",
                    DateTime.UtcNow.AddDays(-2), "2 d ago", "Recent"),
                new("<span style=\"font-weight:500\">Sales team</span> sent quote",
                    "Q-2026-0214 · 12 lines", "ti-mail", "#0C447C",
                    DateTime.UtcNow.AddDays(-7), "1 w ago", "1 week ago"),
                new("<span style=\"font-weight:500\">System Admin</span> uploaded contract",
                    "Customer_Agreement.pdf · 980 KB", "ti-file-plus", "#534AB7",
                    DateTime.UtcNow.AddDays(-60), "2 mo ago", "2 months ago"),
                new("<span style=\"font-weight:500\">Sales team</span> created customer",
                    $"{customer.Code} added to CRM",
                    "ti-plus", "#888780",
                    customer.CreatedAt, RelativeTime.Format(customer.CreatedAt), "Earlier"),
            },
            QuickActions = new()
            {
                // TD-017 partial — only Send email has a working
                // target today (mailto:); it auto-disables when the
                // customer has no email on file. Create order +
                // View invoices wait on Phase 7+ orders/invoices
                // schemas.
                new("Create order",   "ti-shopping-cart", "#", Enabled: false),
                new("Send email",     "ti-mail",
                    customer.Email is null ? "#" : $"mailto:{customer.Email}",
                    Enabled: customer.Email is not null),
                new("View invoices",  "ti-receipt",       "#", Enabled: false),
            },
            OverviewFields = new()
            {
                new("Code",    $"<span style=\"font-family: var(--wms-font-mono);\">{System.Net.WebUtility.HtmlEncode(customer.Code)}</span>"),
                new("Type",    System.Net.WebUtility.HtmlEncode(customer.CustomerType)),
                new("Country", System.Net.WebUtility.HtmlEncode(customer.Country ?? "—")),
                new("Email",   FormatEmailField(customer.Email)),
                new("Phone",   System.Net.WebUtility.HtmlEncode(customer.Phone ?? "—")),
                // TaxId only carried for B2B; column is NULL for B2C.
                new("Tax ID",  System.Net.WebUtility.HtmlEncode(customer.TaxId ?? "—")),
            },
            Properties = new()
            {
                new("Created", RelativeTime.Format(customer.CreatedAt)),
                new("Updated", customer.UpdatedAt is null ? "—" : RelativeTime.Format(customer.UpdatedAt.Value)),
                new("Owner",   "Sales team"),
            }
        };

        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    // 'all' / null / whitespace → null.
    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    // The customer view ships Country with sentinel 'any' (vs. 'all'
    // elsewhere) — mirror that quirk so 'any' also drops the filter.
    private static string? NormaliseCountry(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("all", StringComparison.OrdinalIgnoreCase)
        || value.Equals("any", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string FormatEmailField(string? email) => email is null
        ? "—"
        : $"<a href=\"mailto:{System.Net.WebUtility.HtmlEncode(email)}\" style=\"color: var(--wms-primary); text-decoration: none;\">{System.Net.WebUtility.HtmlEncode(email)}</a>";

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
