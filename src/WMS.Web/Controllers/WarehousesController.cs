using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

[Authorize]
[Route("Warehouses")]
public class WarehousesController : Controller
{
    private readonly IWarehouseRepositoryFactory _repos;
    private readonly ITenantContext _tenant;
    private readonly IDocumentStorageService _docs;

    public WarehousesController(
        IWarehouseRepositoryFactory repos,
        ITenantContext tenant,
        IDocumentStorageService docs)
    {
        _repos = repos;
        _tenant = tenant;
        _docs = docs;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? region = null,
        string? type = null,
        string sortBy = "name",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        // Region filter has no schema column — silently absorbed
        // (TD-009-adjacent). Frontend dropdown can keep emitting it;
        // the page just ignores. Search hits Address so users can still
        // narrow by city via the search box.
        _ = region;

        var filter = new WarehouseFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            // Wire 'active'/'inactive' → bool. Mock 'maintenance' chip
            // → null (= no filter), per TD-009.
            IsActive: WarehouseStatusMapper.FromWire(status),
            Type: NormaliseFilter(type),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var result = await _repos.For(_tenant.RequireTenantId())
                                 .GetPagedAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(w => new
            {
                code              = w.Code,
                name              = w.Name,
                // Mock had a separate cosmetic Subtitle — schema has no
                // such column. Sent as null; the JS list view guards
                // with `${w.subtitle ? ... : ''}` so it just hides.
                subtitle          = (string?)null,
                // Mock had a Region column (e.g. "Bangkok, TH") — schema
                // only has Address. Surface Address as the "region"
                // wire field so the existing column header renders the
                // value users expect. Phase 6B seed addresses already
                // follow the "City, Country" shape.
                region            = w.Address ?? "—",
                type              = w.Type,
                status            = WarehouseStatusMapper.ToWire(w.IsActive),
                locationCount     = w.LocationCount,
                updatedAt         = w.UpdatedAt,
                updatedAtRelative = w.UpdatedAt is null
                    ? "—"
                    : RelativeTime.Format(w.UpdatedAt.Value),
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
        var row = await _repos.For(_tenant.RequireTenantId())
                              .GetListRowByCodeAsync(code, ct);
        if (row is null) return NotFound();

        var docs = await _docs.ListByEntityAsync("Warehouse", code, ct);

        var (statusLabel, statusVariant) = row.IsActive
            ? ("Active", "success")
            : ("Inactive", "neutral");

        var vm = new DetailPageViewModel
        {
            EntityType = "Warehouse",
            EntityId = row.Code,
            Title = row.Name,
            // Pull the city portion out of Address for the subtitle
            // (e.g. "Bangkok, TH" → "Bangkok"). Matches the way the
            // grid card derives its region label.
            Subtitle = $"{row.Code} · {DeriveCity(row.Address)} · {row.Type}",
            IconClass = "ti-building-warehouse",
            IconBgColor = "#E6F1FB",
            IconFgColor = "#0C447C",
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Warehouses",
            BreadcrumbParentUrl = "/Warehouses",
            Stats = new()
            {
                new("Locations",   row.LocationCount.ToString("N0")),
                // Capacity / Active SKUs / Avg dwell need inventory
                // analytics that aren't materialised yet. Stubbed
                // until Phase 7+ analytics. Same shape as
                // Customer.TotalOrders → TD-011-adjacent.
                new("Capacity",    "—"),
                new("Active SKUs", "—"),
                new("Avg dwell",   "—"),
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
            // TD-010-style — Activity stays hardcoded until a real
            // warehouse activity stream exists (cycle counts +
            // receiving headers + putaway events).
            Activities = new()
            {
                new("<span style=\"font-weight:500\">Maya Rodriguez</span> received shipment",
                    "RC-2026-0142 · 50 units · 12 SKUs", "ti-package-import", "#639922",
                    DateTime.UtcNow.AddHours(-3), "3 h ago", "Today"),
                new("<span style=\"font-weight:500\">Cycle count</span> completed",
                    "Zone A-1 · 142 locations · 0 variances", "ti-checklist", "#534AB7",
                    DateTime.UtcNow.AddDays(-1), "1 d ago", "Yesterday"),
                new("<span style=\"font-weight:500\">System Admin</span> uploaded floor plan",
                    "Floor_Plan.pdf · 1.8 MB", "ti-file-plus", "#534AB7",
                    DateTime.UtcNow.AddDays(-45), "1 mo ago", "1 month ago"),
                new("<span style=\"font-weight:500\">System Admin</span> created warehouse",
                    $"{row.Code} added to network", "ti-plus", "#888780",
                    row.CreatedAt, RelativeTime.Format(row.CreatedAt), "Earlier"),
            },
            QuickActions = new()
            {
                new("Receive shipment", "ti-package-import", "#"),
                new("Cycle count",      "ti-checklist",      "#"),
                new("View stock",       "ti-archive",        "#"),
            },
            OverviewFields = new()
            {
                new("Code",      $"<span style=\"font-family: var(--wms-font-mono);\">{System.Net.WebUtility.HtmlEncode(row.Code)}</span>"),
                new("Type",      System.Net.WebUtility.HtmlEncode(row.Type)),
                new("Address",   System.Net.WebUtility.HtmlEncode(row.Address ?? "—")),
                new("Locations", row.LocationCount.ToString("N0")),
                new("Manager",   System.Net.WebUtility.HtmlEncode(row.ManagerName ?? "—")),
                new("Phone",     System.Net.WebUtility.HtmlEncode(row.PhoneNumber ?? "—")),
            },
            Properties = new()
            {
                new("Created", RelativeTime.Format(row.CreatedAt)),
                new("Updated", row.UpdatedAt is null ? "—" : RelativeTime.Format(row.UpdatedAt.Value)),
                new("Owner",   "Operations"),
            }
        };

        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    // 'all' / null / whitespace → null.
    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    // Best-effort city extraction from a "City, Country" address.
    // Falls back to the full address (or em dash) when the string
    // has no comma.
    private static string DeriveCity(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "—";
        var idx = address.IndexOf(',');
        return idx > 0 ? address[..idx].Trim() : address.Trim();
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
