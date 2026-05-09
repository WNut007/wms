using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.Web.Models.Inbound;
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
    private readonly IReceivingHeaderService _service;
    private readonly IValidator<CancelReceivingViewModel> _cancelValidator;
    private readonly ICurrentUser _currentUser;

    public ReceivingController(
        IReceivingHeaderRepositoryFactory repos,
        IStockMovementRepositoryFactory movementRepos,
        ITenantContext tenant,
        IDocumentStorageService docs,
        IReceivingHeaderService service,
        IValidator<CancelReceivingViewModel> cancelValidator,
        ICurrentUser currentUser)
    {
        _repos = repos;
        _movementRepos = movementRepos;
        _tenant = tenant;
        _docs = docs;
        _service = service;
        _cancelValidator = cancelValidator;
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
                // Phase 10B (TD-023) — Cancel button is interactive
                // only on Posted receipts. The href targets a JS hook
                // (`#cancel-modal`) the Detail view binds to open the
                // confirm modal; the form itself POSTs to /Receiving
                // /Cancel/{id}.
                new("Cancel receipt", "ti-x",
                    h.Status == "Posted" ? "#cancel-modal" : "#",
                    Enabled: h.Status == "Posted"),
            },
            OverviewFields = BuildOverviewFields(h),
            Properties = BuildProperties(h),
        };

        ViewBag.Lines = lines;
        // Phase 10B (TD-023) — pass header-level cancellation audit
        // to the layout for the dismissible TempData banner + the
        // hidden modal partial that the Quick Action triggers.
        ViewBag.HeaderId        = h.Id;
        ViewBag.IsPosted        = h.Status == "Posted";
        ViewBag.CancelMessage   = TempData["CancelMessage"] as string;
        ViewBag.CancelError     = TempData["CancelError"] as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    private static List<KeyValuePair<string, string>> BuildOverviewFields(Domain.Entities.Inbound.ReceivingHeader h)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Receiving #", $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.ReceivingNumber)}</span>"),
            new("PO",          h.PurchaseOrderId.HasValue
                ? $"<a href=\"/PurchaseOrders/Detail/{h.PurchaseOrderId.Value}\">View PO</a>"
                : "Blind receipt"),
            new("Warehouse",   System.Net.WebUtility.HtmlEncode(h.WarehouseId.ToString())),
            new("Received at", h.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"),
            new("Status",      System.Net.WebUtility.HtmlEncode(h.Status)),
            new("Notes",       System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")),
        };

        if (h.Status == "Cancelled")
        {
            fields.Add(new("Cancel reason",
                System.Net.WebUtility.HtmlEncode(h.CancelReason ?? "—")));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(Domain.Entities.Inbound.ReceivingHeader h)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Created", RelativeTime.Format(h.CreatedAt)),
            new("Updated", h.UpdatedAt is null ? "—" : RelativeTime.Format(h.UpdatedAt.Value)),
        };

        if (h.CancelledAt.HasValue)
        {
            props.Add(new("Cancelled", RelativeTime.Format(h.CancelledAt.Value)));
        }

        return props;
    }

    [HttpGet("Print/{number}")]
    public async Task<IActionResult> Print(string number, CancellationToken ct)
    {
        var detail = await _repos.For(_tenant.RequireTenantId()).GetByNumberAsync(number, ct);
        if (detail is null) return NotFound();
        return View(detail);
    }

    // Phase 10B (TD-023) — POST /Receiving/Cancel/{id}. Idempotent
    // (already-Cancelled returns success with a notice). Validation
    // failures redirect back to Detail with field errors in TempData.
    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelReceivingViewModel vm,
        CancellationToken ct)
    {
        // Bind id from route (form binds it too but route is the
        // authoritative source — guards against tampering).
        vm = vm with { Id = id };

        var validation = await _cancelValidator.ValidateAsync(vm, ct);
        if (!validation.IsValid)
        {
            // Surface the first validation error via TempData; the
            // Detail view renders it in a banner. Modal-driven flow
            // makes per-field error rendering noisy; a single message
            // is the cleaner UX.
            TempData["CancelError"] = validation.Errors.FirstOrDefault()?.ErrorMessage
                ?? "Validation failed.";
            return await RedirectToDetailByIdAsync(id, ct);
        }

        try
        {
            var changed = await _service.CancelReceivingAsync(
                _tenant.RequireTenantId(), id, vm.Reason.Trim(), _currentUser.UserId, ct);

            TempData["CancelMessage"] = changed
                ? "Receipt cancelled. Stock reversed; PO status reverted."
                : "Receipt was already cancelled — no action taken.";
        }
        catch (InvalidOperationException ex)
        {
            // Underflow on CK_Stock_OnHand_NonNegative or invalid
            // source state. Surface the message; nothing was written
            // (TransactionScope rolled back).
            TempData["CancelError"] = ex.Message;
        }

        return await RedirectToDetailByIdAsync(id, ct);
    }

    private async Task<IActionResult> RedirectToDetailByIdAsync(Guid id, CancellationToken ct)
    {
        // Detail's URL key is ReceivingNumber; resolve from the row
        // we have. If the row vanished (shouldn't, but defensive),
        // fall back to the list.
        var detail = await _repos.For(_tenant.RequireTenantId()).GetByIdAsync(id, ct);
        if (detail is null)
            return RedirectToAction(nameof(Index));
        return RedirectToAction(nameof(Detail),
            new { number = detail.Header.ReceivingNumber });
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
