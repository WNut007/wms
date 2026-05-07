using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Web.Services;
using WMS.Web.Services.Mock;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

[Authorize]
[Route("Warehouses")]
public class WarehousesController : Controller
{
    private readonly MockWarehouseDataService _data;
    private readonly IDocumentStorageService _docs;

    public WarehousesController(MockWarehouseDataService data, IDocumentStorageService docs)
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
        string? region = null,
        string? type = null,
        string sortBy = "name",
        bool sortDesc = false)
    {
        var result = _data.GetWarehouses(page, pageSize, search, status, region, type, sortBy, sortDesc);
        return Json(new
        {
            items = result.Items.Select(w => new
            {
                code              = w.Code,
                name              = w.Name,
                subtitle          = w.Subtitle,
                region            = w.Region,
                type              = w.Type,
                status            = w.Status,
                locationCount     = w.LocationCount,
                updatedAt         = w.UpdatedAt,
                updatedAtRelative = RelativeTime.Format(w.UpdatedAt),
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
        var wh = _data.GetByCode(code);
        if (wh == null) return NotFound();

        var docs = await _docs.ListByEntityAsync("Warehouse", code, ct);

        var (statusLabel, statusVariant) = wh.Status switch
        {
            "active"      => ("Active", "success"),
            "maintenance" => ("Maintenance", "warning"),
            _             => ("Inactive", "neutral"),
        };

        var vm = new DetailPageViewModel
        {
            EntityType = "Warehouse",
            EntityId = wh.Code,
            Title = wh.Name,
            Subtitle = $"{wh.Code} · {wh.Region} · {wh.Type}",
            IconClass = "ti-building-warehouse",
            IconBgColor = "#E6F1FB",
            IconFgColor = "#0C447C",
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Warehouses",
            BreadcrumbParentUrl = "/Warehouses",
            Stats = new()
            {
                new("Locations",   wh.LocationCount.ToString("N0")),
                new("Capacity",    "78%"),
                new("Active SKUs", "1,247"),
                new("Avg dwell",   "3.2 d"),
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
                    $"{wh.Code} added to network", "ti-plus", "#888780",
                    DateTime.UtcNow.AddDays(-365), "1 y ago", "1 year ago"),
            },
            QuickActions = new()
            {
                new("Receive shipment", "ti-package-import", "#"),
                new("Cycle count",      "ti-checklist",      "#"),
                new("View stock",       "ti-archive",        "#"),
            },
            OverviewFields = new()
            {
                new("Code",      $"<span style=\"font-family: var(--wms-font-mono);\">{wh.Code}</span>"),
                new("Type",      System.Net.WebUtility.HtmlEncode(wh.Type)),
                new("Region",    System.Net.WebUtility.HtmlEncode(wh.Region)),
                new("Locations", wh.LocationCount.ToString("N0")),
                new("Manager",   "Maya Rodriguez"),
                new("Phone",     "+66 2 555 0123"),
            },
            Properties = new()
            {
                new("Created", "1 y ago"),
                new("Updated", RelativeTime.Format(wh.UpdatedAt)),
                new("Owner",   "Operations"),
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
