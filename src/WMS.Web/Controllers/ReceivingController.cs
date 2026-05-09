using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Phase 9C — desktop GR list / detail / print. Distinct from
// /Receive (mobile single-line form) and /GoodsReceipt (Phase 9B
// desktop create form). Three roles, three controllers — kept
// separate so the URL surface communicates intent clearly:
//   /Receive            — mobile PWA scan-and-receive
//   /GoodsReceipt/Create — desktop multi-line create
//   /Receiving           — list + detail + GRN print (this class)
[Authorize]
[Route("Receiving")]
public sealed class ReceivingController : Controller
{
    private const int ActivityFeedLimit = 20;

    private readonly IReceivingHeaderRepositoryFactory _repos;
    private readonly IStockMovementRepositoryFactory _movementRepos;
    private readonly ITenantContext _tenant;
    private readonly IDocumentStorageService _docs;

    public ReceivingController(
        IReceivingHeaderRepositoryFactory repos,
        IStockMovementRepositoryFactory movementRepos,
        ITenantContext tenant,
        IDocumentStorageService docs)
    {
        _repos = repos;
        _movementRepos = movementRepos;
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
        string? warehouse = null,
        string sortBy = "receivedAt",
        bool sortDesc = true,
        CancellationToken ct = default)
    {
        var filter = new ReceivingFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: ReceivingStatusMapper.FromWire(status),
            WarehouseCode: NormaliseFilter(warehouse),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);  // TD-028

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id              = r.Id,
                receivingNumber = r.ReceivingNumber,
                poNumber        = r.PoNumber ?? "Blind",
                hasPo           = r.PurchaseOrderId.HasValue,
                vendor          = r.VendorCode ?? "—",
                vendorName      = r.VendorName ?? "",
                warehouse       = r.WarehouseCode,
                receivedAt      = r.ReceivedAt,
                receivedRelative = RelativeTime.Format(r.ReceivedAt),
                status          = ReceivingStatusMapper.ToWire(r.Status),
                statusLabel     = r.Status,
                lineCount       = r.LineCount,
                totalReceived   = r.TotalReceivedQty,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all       = counts.All,
                draft     = counts.Draft,
                posted    = counts.Posted,
                cancelled = counts.Cancelled,
            },
        });
    }

    [HttpGet("Detail/{number}")]
    public async Task<IActionResult> Detail(string number, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _repos.For(tenantId).GetByNumberAsync(number, ct);
        if (detail is null) return NotFound();

        var h = detail.Header;
        var lines = detail.Lines;

        // Documents tab — Phase 5 storage by EntityType + EntityId.
        var docs = await _docs.ListByEntityAsync("Receiving", h.Id.ToString(), ct);

        // Activity feed: per-line stock movements for this receipt.
        // The Movement Log stores ReferenceType='ReceivingLine' +
        // ReferenceId=line.Id (Phase 6A). Loop over lines and
        // Single SQL query joins through ReceivingLines for the
        // resolved display fields (PerformedByName + From/To codes).
        var allMovements = await _movementRepos.For(tenantId)
            .GetByReceivingHeaderAsync(h.Id, ActivityFeedLimit, ct);

        var totalReceivedQty = lines.Sum(l => l.ReceivedQuantity);

        var (statusLabel, statusVariant) = h.Status switch
        {
            "Draft"     => ("Draft",     "warning"),
            "Posted"    => ("Posted",    "success"),
            "Cancelled" => ("Cancelled", "neutral"),
            _           => (h.Status,    "neutral"),
        };

        var vm = new DetailPageViewModel
        {
            EntityType = "Receiving",
            EntityId = h.ReceivingNumber,
            Title = h.ReceivingNumber,
            Subtitle = $"{lines.Count} line(s) · {totalReceivedQty:N2} received · {h.ReceivedAt:yyyy-MM-dd HH:mm} UTC",
            IconClass = "ti-package-import",
            IconBgColor = "#E1F5EE",
            IconFgColor = "#085041",
            AvatarInitials = "",
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Receiving",
            BreadcrumbParentUrl = "/Receiving",
            Stats = new()
            {
                new("Lines",         lines.Count.ToString("N0")),
                new("Received qty",  totalReceivedQty.ToString("N2"),
                                      h.Status == "Posted" ? "#1D9E75" : null),
                new("PO",            h.PurchaseOrderId.HasValue ? "Linked" : "Blind",
                                      h.PurchaseOrderId.HasValue ? "#0C447C" : null),
                new("Status",        h.Status),
            },
            ShowImagesTab = false,
            Documents = docs.Select(d => new DocumentItem(
                d.DocumentId, d.FileName, d.Category,
                CategoryColorBg(d.Category), CategoryColorFg(d.Category),
                d.IconClass, d.IconColorBg, d.IconColorFg,
                d.FileSizeFormatted, d.UploadedBy, d.UploadedAt,
                RelativeTime.Format(d.UploadedAt))).ToList(),
            Activities = allMovements
                .OrderByDescending(m => m.PerformedAt)
                .Select(MovementActivityMapper.Map)
                .ToList(),
            QuickActions = new()
            {
                new("Print GRN",     "ti-printer",
                    $"/Receiving/Print/{Uri.EscapeDataString(h.ReceivingNumber)}"),
                new("View PO",       "ti-truck-delivery",
                    h.PurchaseOrderId.HasValue
                        ? $"/PurchaseOrders/Detail/{h.PurchaseOrderId.Value}"
                        : "#",
                    Enabled: h.PurchaseOrderId.HasValue),
                // Edit-Draft-and-Promote deferred (TD-027 logged) —
                // would re-open the GoodsReceipt/Create form pre-populated
                // with this draft. Phase 10 candidate.
                new("Edit draft",    "ti-edit", "#", Enabled: false),
                new("Cancel receipt", "ti-x",   "#", Enabled: false),
            },
            OverviewFields = new()
            {
                new("Receiving #", $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.ReceivingNumber)}</span>"),
                new("PO",          h.PurchaseOrderId.HasValue
                    ? $"<a href=\"/PurchaseOrders/Detail/{h.PurchaseOrderId.Value}\">View PO</a>"
                    : "Blind receipt"),
                new("Warehouse",   System.Net.WebUtility.HtmlEncode(h.WarehouseId.ToString())),
                new("Received at", h.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
                new("Status",      System.Net.WebUtility.HtmlEncode(h.Status)),
                new("Notes",       System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")),
            },
            Properties = new()
            {
                new("Created", RelativeTime.Format(h.CreatedAt)),
                new("Updated", h.UpdatedAt is null ? "—" : RelativeTime.Format(h.UpdatedAt.Value)),
            }
        };

        ViewBag.Lines = lines;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpGet("Print/{number}")]
    public async Task<IActionResult> Print(string number, CancellationToken ct)
    {
        var detail = await _repos.For(_tenant.RequireTenantId()).GetByNumberAsync(number, ct);
        if (detail is null) return NotFound();
        return View(detail);
    }

    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null : value;

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
