using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Inbound;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;
using WMS.BLL.Services.Inbound;

namespace WMS.Web.Controllers;

// Read-side (Index, GetData, Detail) lives here. Admin Create / Edit /
// Archive lives in PurchaseOrdersController.Admin.cs partial.
[Authorize]
[Route("PurchaseOrders")]
public partial class PurchaseOrdersController : Controller
{
    private const int ActivityFeedLimit = 20;

    private readonly IPurchaseOrderRepositoryFactory _repos;
    private readonly IReceivingHeaderRepositoryFactory _receivingRepos;
    private readonly ITenantContext _tenant;
    private readonly IDocumentStorageService _docs;
    private readonly IOwnerRepositoryFactory _ownerRepos;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly IUomRepositoryFactory _uomRepos;
    private readonly IPurchaseOrderService _service;
    private readonly IValidator<PurchaseOrderCreateViewModel> _createValidator;
    private readonly IValidator<PurchaseOrderEditViewModel> _editValidator;
    private readonly ICurrentUser _currentUser;

    public PurchaseOrdersController(
        IPurchaseOrderRepositoryFactory repos,
        IReceivingHeaderRepositoryFactory receivingRepos,
        ITenantContext tenant,
        IDocumentStorageService docs,
        IOwnerRepositoryFactory ownerRepos,
        IWarehouseRepositoryFactory warehouseRepos,
        IProductRepositoryFactory productRepos,
        IUomRepositoryFactory uomRepos,
        IPurchaseOrderService service,
        IValidator<PurchaseOrderCreateViewModel> createValidator,
        IValidator<PurchaseOrderEditViewModel> editValidator,
        ICurrentUser currentUser)
    {
        _repos = repos;
        _receivingRepos = receivingRepos;
        _tenant = tenant;
        _docs = docs;
        _ownerRepos = ownerRepos;
        _warehouseRepos = warehouseRepos;
        _productRepos = productRepos;
        _uomRepos = uomRepos;
        _service = service;
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
        string? owner = null,
        string? warehouse = null,
        string sortBy = "expectedDate",
        bool sortDesc = false,
        CancellationToken ct = default)
    {
        var filter = new PurchaseOrderFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: PurchaseOrderStatusMapper.FromWire(status),
            OwnerCode: NormaliseFilter(owner),
            WarehouseCode: NormaliseFilter(warehouse),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var result = await _repos.For(_tenant.RequireTenantId())
                                 .GetPagedAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(p => new
            {
                id                = p.Id,
                poNumber          = p.PoNumber,
                vendor            = p.OwnerCode,
                vendorName        = p.OwnerName,
                warehouse         = p.WarehouseCode,
                expectedDate      = p.ExpectedDate?.ToString("yyyy-MM-dd"),
                expectedRelative  = p.ExpectedDate is null
                    ? "—"
                    : RelativeTime.Format(p.ExpectedDate.Value),
                status            = PurchaseOrderStatusMapper.ToWire(p.Status),
                statusLabel       = p.Status,
                lineCount         = p.LineCount,
                totalExpected     = p.TotalExpectedQty,
                totalReceived     = p.TotalReceivedQty,
                fillPct           = p.TotalExpectedQty > 0
                    ? Math.Round(p.TotalReceivedQty / p.TotalExpectedQty * 100m, 1)
                    : 0m,
                createdAt         = p.CreatedAt,
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
        });
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var repo = _repos.For(tenantId);
        var detail = await repo.GetByIdAsync(id, ct);
        if (detail is null) return NotFound();

        var po = detail.Header;
        var lines = detail.Lines;

        // Activity feed — receipts that posted against this PO.
        var receipts = await _receivingRepos.For(tenantId)
                                            .GetActivityByPoAsync(id, ActivityFeedLimit, ct);

        var docs = await _docs.ListByEntityAsync("PurchaseOrder", id.ToString(), ct);

        var totalExpected = lines.Sum(l => l.ExpectedQuantity);
        var totalReceived = lines.Sum(l => l.ReceivedQuantity);
        var fillPct = totalExpected > 0
            ? Math.Round(totalReceived / totalExpected * 100m, 1)
            : 0m;

        var (statusLabel, statusVariant) = po.Status switch
        {
            "Open"      => ("Open",      "info"),
            "Receiving" => ("Receiving", "warning"),
            "Closed"    => ("Closed",    "success"),
            "Cancelled" => ("Cancelled", "neutral"),
            _           => (po.Status,   "neutral"),
        };

        var vm = new DetailPageViewModel
        {
            EntityType = "PurchaseOrder",
            EntityId = id.ToString(),
            Title = po.PoNumber,
            Subtitle = $"{lines.Count} line(s) · expected {totalExpected:N2} · received {totalReceived:N2}",
            IconClass = "ti-truck-delivery",
            IconBgColor = "#E6F1FB",
            IconFgColor = "#0C447C",
            AvatarInitials = "",
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Purchase Orders",
            BreadcrumbParentUrl = "/PurchaseOrders",
            Stats = new()
            {
                new("Lines",          lines.Count.ToString("N0")),
                new("Expected qty",   totalExpected.ToString("N2")),
                new("Received qty",   totalReceived.ToString("N2"),
                                       totalReceived >= totalExpected && totalExpected > 0 ? "#1D9E75" : null),
                new("Fill %",         $"{fillPct:0.#}%",
                                       fillPct >= 100 ? "#1D9E75" : null),
            },
            ShowImagesTab = false,
            Documents = docs.Select(d => new DocumentItem(
                d.DocumentId, d.FileName, d.Category,
                CategoryColorBg(d.Category), CategoryColorFg(d.Category),
                d.IconClass, d.IconColorBg, d.IconColorFg,
                d.FileSizeFormatted, d.UploadedBy, d.UploadedAt,
                RelativeTime.Format(d.UploadedAt))).ToList(),
            // Activity rendering uses the existing ReceivingActivityMapper
            // (Phase 6E). Same shape as warehouse activity.
            Activities = receipts.Select(MovementActivityMapperReceiptShim).ToList(),
            QuickActions = new()
            {
                new("Edit PO",        "ti-edit",
                    $"/PurchaseOrders/Edit/{id}"),
                new("Receive against", "ti-package-import", "#", Enabled: false),
                new("Cancel PO",      "ti-x",              "#", Enabled: po.Status is "Open" or "Receiving"),
            },
            OverviewFields = new()
            {
                new("PO number",   $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(po.PoNumber)}</span>"),
                new("Vendor",      System.Net.WebUtility.HtmlEncode(po.OwnerId.ToString())),
                new("Warehouse",   System.Net.WebUtility.HtmlEncode(po.WarehouseId.ToString())),
                new("Expected",    po.ExpectedDate?.ToString("yyyy-MM-dd") ?? "—"),
                new("Status",      System.Net.WebUtility.HtmlEncode(po.Status)),
                new("Notes",       System.Net.WebUtility.HtmlEncode(po.Notes ?? "—")),
            },
            Properties = new()
            {
                new("Created", RelativeTime.Format(po.CreatedAt)),
                new("Updated", po.UpdatedAt is null ? "—" : RelativeTime.Format(po.UpdatedAt.Value)),
            }
        };

        ViewBag.Lines = lines;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    // ReceivingActivityRow → ActivityItem shim. Same shape as Phase 6E
    // ReceivingActivityMapper but expressed inline since we don't have
    // direct access to the Receiving mapper here without a using ref.
    // Mirrors the existing Mapper output exactly.
    private static ActivityItem MovementActivityMapperReceiptShim(ReceivingActivityRow r)
    {
        var (verb, color) = r.Status switch
        {
            "Posted"    => ("posted",    "#085041"),
            "Draft"     => ("drafted",   "#888780"),
            "Cancelled" => ("cancelled", "#A32D2D"),
            _           => ("recorded",  "#444441"),
        };
        var perfomerHtml = $"<span style=\"font-weight:500\">{System.Net.WebUtility.HtmlEncode(r.PerformedByName)}</span>";
        return new ActivityItem(
            Title: $"{perfomerHtml} {verb} receipt {System.Net.WebUtility.HtmlEncode(r.ReceivingNumber)}",
            Description: $"{r.LineCount} line(s)",
            IconClass: "ti-truck-delivery",
            IconColor: color,
            Timestamp: r.ReceivedAt,
            TimestampRelative: RelativeTime.Format(r.ReceivedAt),
            DateGroup: BucketDateGroup(r.ReceivedAt));
    }

    private static string BucketDateGroup(DateTime ts)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        if (ts >= todayStart) return "Today";
        if (ts >= todayStart.AddDays(-1)) return "Yesterday";
        if (ts >= todayStart.AddDays(-7)) return "This week";
        return "Older";
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
