using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Inventory;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Inventory;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Phase 13 (ADR-012) — Inter-warehouse Transfer workflow.
//   GET  /Transfers                       — Alpine list with chip counts
//   GET  /Transfers/Data                  — JSON envelope (rows + counts)
//   GET  /Transfers/Create                — multi-line Create form
//   POST /Transfers/Create                — validate → service → redirect to Detail
//   GET  /Transfers/Detail/{id}           — _DetailLayout w/ inline Dispatch/Receive forms
//   POST /Transfers/Submit/{id}           — Draft → Submitted
//   POST /Transfers/Approve/{id}          — Submitted → Approved (sep. of duties)
//   POST /Transfers/Dispatch/{id}         — Approved → InTransit (TX-wrapped, lines)
//   POST /Transfers/Receive/{id}          — InTransit → Received (TX-wrapped, lines)
//   POST /Transfers/Cancel/{id}           — pre-InTransit → Cancelled
//   POST /Transfers/MarkLost/{id}         — InTransit → Lost
//   GET  /Transfers/Locations/{whId}      — JSON helper (cascading dropdown)
[Authorize]
[Route("Transfers")]
public sealed class TransfersController : Controller
{
    private readonly ITransferOrderRepositoryFactory _repos;
    private readonly ITransferOrderService _service;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly IOwnerRepositoryFactory _ownerRepos;
    private readonly IUomRepositoryFactory _uomRepos;
    private readonly ILocationRepositoryFactory _locationRepos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<TransferOrderCreateViewModel> _createValidator;
    private readonly IValidator<CancelTransferViewModel> _cancelValidator;
    private readonly IValidator<MarkLostTransferViewModel> _lostValidator;

    public TransfersController(
        ITransferOrderRepositoryFactory repos,
        ITransferOrderService service,
        IWarehouseRepositoryFactory warehouseRepos,
        IProductRepositoryFactory productRepos,
        IOwnerRepositoryFactory ownerRepos,
        IUomRepositoryFactory uomRepos,
        ILocationRepositoryFactory locationRepos,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<TransferOrderCreateViewModel> createValidator,
        IValidator<CancelTransferViewModel> cancelValidator,
        IValidator<MarkLostTransferViewModel> lostValidator)
    {
        _repos = repos;
        _service = service;
        _warehouseRepos = warehouseRepos;
        _productRepos = productRepos;
        _ownerRepos = ownerRepos;
        _uomRepos = uomRepos;
        _locationRepos = locationRepos;
        _tenant = tenant;
        _currentUser = currentUser;
        _createValidator = createValidator;
        _cancelValidator = cancelValidator;
        _lostValidator = lostValidator;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? fromWarehouse = null,
        string? toWarehouse = null,
        string sortBy = "requestedAt",
        bool sortDesc = true,
        CancellationToken ct = default)
    {
        var filter = new TransferOrderFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: TransferOrderStatusMapper.FromWire(status),
            FromWarehouseCode: NormaliseFilter(fromWarehouse),
            ToWarehouseCode: NormaliseFilter(toWarehouse),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(t => new
            {
                id                = t.Id,
                transferNumber    = t.TransferNumber,
                fromWarehouseCode = t.FromWarehouseCode,
                toWarehouseCode   = t.ToWarehouseCode,
                status            = TransferOrderStatusMapper.ToWire(t.Status),
                statusLabel       = t.Status,
                reason            = t.Reason,
                lineCount         = t.LineCount,
                totalRequested    = t.TotalRequested,
                totalDispatched   = t.TotalDispatched,
                totalReceived     = t.TotalReceived,
                requestedByName   = t.RequestedByName,
                requestedAt       = t.RequestedAt,
                requestedRelative = RelativeTime.Format(t.RequestedAt),
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all       = counts.All,
                draft     = counts.Draft,
                submitted = counts.Submitted,
                approved  = counts.Approved,
                intransit = counts.InTransit,
                received  = counts.Received,
                cancelled = counts.Cancelled,
                lost      = counts.Lost,
            },
        });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new TransferOrderCreateViewModel
        {
            FromWarehouseId = _currentUser.WarehouseId ?? Guid.Empty,
            Lines = new() { NewLine(1) },
        };
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        TransferOrderCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();

        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }

        try
        {
            var requesterId = _currentUser.UserId
                ?? throw new InvalidOperationException(
                    "Cannot create transfer without an authenticated user.");

            var request = new CreateTransferOrderRequest(
                FromWarehouseId: vm.FromWarehouseId,
                ToWarehouseId:   vm.ToWarehouseId,
                Reason:          vm.Reason,
                Notes:           string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
                Lines: vm.Lines.Select((l, i) => new CreateTransferOrderLineRequest(
                    LineNumber:     l.LineNumber > 0 ? l.LineNumber : i + 1,
                    ProductId:      l.ProductId,
                    OwnerId:        l.OwnerId,
                    UomId:          l.UomId,
                    LotId:          l.LotId,
                    PalletId:       l.PalletId,
                    FromLocationId: l.FromLocationId,
                    ToLocationId:   l.ToLocationId,
                    QtyRequested:   l.QtyRequested,
                    Notes:          string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                    .ToList());

            var saved = await _service.CreateAsync(tenantId, request, requesterId, ct);
            TempData["TransferMessage"] =
                $"Transfer {saved.Header.TransferNumber} created — {saved.Lines.Count} lines (Draft).";
            return RedirectToAction(nameof(Detail), new { id = saved.Header.Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }
    }

    [HttpGet("Detail/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _service.GetByIdAsync(tenantId, id, ct);
        if (detail is null) return NotFound();

        var lineRows = await _repos.For(tenantId).GetLineRowsByIdAsync(id, ct);
        var h = detail.Header;
        var currentUserId = _currentUser.UserId;

        // State-driven Quick Action gating.
        var isDraft     = h.Status == "Draft";
        var isSubmitted = h.Status == "Submitted";
        var isApproved  = h.Status == "Approved";
        var isInTransit = h.Status == "InTransit";
        var preInTransit = isDraft || isSubmitted || isApproved;

        var canSubmit   = isDraft;
        var canApprove  = isSubmitted
            && currentUserId is not null
            && currentUserId.Value != h.RequestedBy;
        var canDispatch = isApproved;
        var canReceive  = isInTransit;
        var canCancel   = preInTransit;
        var canMarkLost = isInTransit;

        var statusVariant = TransferOrderStatusMapper.ToBadgeVariant(h.Status);

        var totalRequested = lineRows.Sum(l => l.QtyRequested);
        var totalDispatched = lineRows.Sum(l => l.QtyDispatched ?? 0m);
        var totalReceived = lineRows.Sum(l => l.QtyReceived ?? 0m);
        var totalLoss = lineRows.Sum(l => l.QtyLossInTransit);

        var vm = new DetailPageViewModel
        {
            EntityType = "Transfer",
            EntityId = id.ToString(),
            Title = h.TransferNumber,
            Subtitle = $"{lineRows.Count} lines · {h.Reason} · {h.Status}",
            IconClass = "ti-transfer",
            IconBgColor = "#EEEDFE",
            IconFgColor = "#534AB7",
            AvatarInitials = "",
            StatusLabel = h.Status,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Transfers",
            BreadcrumbParentUrl = "/Transfers",
            Stats = new()
            {
                new("Lines",       lineRows.Count.ToString("N0")),
                new("Requested",   totalRequested.ToString("N2")),
                new("Dispatched",  totalDispatched.ToString("N2"),
                                    totalDispatched > 0m ? "#0C447C" : null),
                new("Received",    totalReceived.ToString("N2"),
                                    totalReceived > 0m ? "#1D9E75"
                                    : (totalLoss > 0m ? "#A32D2D" : null)),
            },
            ShowImagesTab = false,
            CustomTabs = new()
            {
                // Lines tab — inline Dispatch form when Approved; inline
                // Receive form when InTransit; read-only with variance
                // highlight otherwise.
                new("lines", "Lines", "ti-list-details",
                    "Detail/_TransferLinesPanel", lineRows.Count),
            },
            QuickActions = new()
            {
                new("Submit",        "ti-send",
                    canSubmit   ? "#submit-modal"   : "#", Enabled: canSubmit),
                new("Approve",       "ti-check",
                    canApprove  ? "#approve-modal"  : "#", Enabled: canApprove),
                new("Dispatch",      "ti-truck-loading",
                    canDispatch ? "#dispatch-form"  : "#", Enabled: canDispatch),
                new("Receive",       "ti-package-import",
                    canReceive  ? "#receive-form"   : "#", Enabled: canReceive),
                new("Cancel",        "ti-x",
                    canCancel   ? "#cancel-modal"   : "#", Enabled: canCancel),
                new("Mark as lost",  "ti-alert-triangle",
                    canMarkLost ? "#lost-modal"     : "#", Enabled: canMarkLost),
            },
            OverviewFields = BuildOverviewFields(h),
            Properties = BuildProperties(h),
        };

        ViewBag.HeaderId       = h.Id;
        ViewBag.HeaderStatus   = h.Status;
        ViewBag.IsDraft        = isDraft;
        ViewBag.IsSubmitted    = isSubmitted;
        ViewBag.IsApproved     = isApproved;
        ViewBag.IsInTransit    = isInTransit;
        ViewBag.CanSubmit      = canSubmit;
        ViewBag.CanApprove     = canApprove;
        ViewBag.CanDispatch    = canDispatch;
        ViewBag.CanReceive     = canReceive;
        ViewBag.CanCancel      = canCancel;
        ViewBag.CanMarkLost    = canMarkLost;
        // Self-approval banner — same key as Phase 11A so the layout's
        // existing check renders the message.
        ViewBag.SelfApproval   = isSubmitted
            && currentUserId is not null
            && currentUserId.Value == h.RequestedBy;
        ViewBag.LineRows       = lineRows;
        ViewBag.TransferMessage = TempData["TransferMessage"] as string;
        ViewBag.TransferError   = TempData["TransferError"]   as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpPost("Submit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.SubmitAsync(tenantId, id, requesterId, ct);
            TempData["TransferMessage"] = changed
                ? "Transfer submitted for approval."
                : "Transfer was already submitted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TransferError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Approve/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var approverId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.ApproveAsync(tenantId, id, approverId, ct);
            TempData["TransferMessage"] = changed
                ? "Transfer approved — ready for dispatch."
                : "Transfer was already approved.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TransferError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Dispatch/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispatch(
        Guid id, DispatchTransferViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var dispatcherId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        // Skip rows the operator left at zero (didn't pick those lines).
        var entries = vm.Lines
            .Where(l => l.QtyDispatched > 0m)
            .Select(l => new DispatchLineRequest(l.LineId, l.QtyDispatched))
            .ToList();

        if (entries.Count == 0)
        {
            TempData["TransferError"] =
                "Enter at least one dispatched quantity (must be greater than zero).";
            return RedirectToAction(nameof(Detail), new { id });
        }

        try
        {
            var changed = await _service.DispatchAsync(
                tenantId, id, entries, dispatcherId, ct);
            TempData["TransferMessage"] = changed
                ? $"Dispatched {entries.Count} line(s). Stock decremented at source."
                : "Transfer was already dispatched.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["TransferError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Receive/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(
        Guid id, ReceiveTransferViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var receiverId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        if (vm.Lines.Count == 0)
        {
            TempData["TransferError"] = "No line entries posted.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var entries = vm.Lines
            .Select(l => new ReceiveLineRequest(l.LineId, l.QtyReceived))
            .ToList();

        try
        {
            var changed = await _service.ReceiveAsync(
                tenantId, id, entries, receiverId, ct);
            TempData["TransferMessage"] = changed
                ? $"Received {entries.Count} line(s). Stock incremented at destination."
                : "Transfer was already received.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData["TransferError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        Guid id, CancelTransferViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _cancelValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["TransferError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
                ?? "Validation failed.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.CancelAsync(
                tenantId, id, vm.Reason.Trim(), requesterId, ct);
            TempData["TransferMessage"] = changed
                ? "Transfer cancelled."
                : "Transfer was already cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TransferError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("MarkLost/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkLost(
        Guid id, MarkLostTransferViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _lostValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["TransferError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
                ?? "Validation failed.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.MarkLostAsync(
                tenantId, id, vm.Reason.Trim(), requesterId, ct);
            TempData["TransferMessage"] = changed
                ? "Transfer marked as lost. QtyLossInTransit reflects the gap."
                : "Transfer was already marked as lost.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["TransferError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpGet("Locations/{warehouseId:guid}")]
    public async Task<IActionResult> Locations(Guid warehouseId, CancellationToken ct)
    {
        // AJAX cascade for both From-warehouse and To-warehouse pickers.
        var rows = await _locationRepos.For(_tenant.RequireTenantId())
                                       .GetActiveByWarehouseAsync(warehouseId, ct);
        return Json(rows.Select(l => new { id = l.Id, code = l.Code, name = l.Name }));
    }

    private async Task PopulateLookupsAsync(
        TransferOrderCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();

        vm.Warehouses = (await _warehouseRepos.For(tenantId).GetActiveAsync(ct))
            .Select(w => new WMS.DAL.Common.LookupItem(w.Id, w.Code, w.Name))
            .ToList();
        vm.Products = await _productRepos.For(tenantId).GetActiveAsync(ct);
        // Phase 11A precedent: owners dropdown reuses GetActiveSuppliersAsync.
        // Broader owner exposure (Self / Customer3PL) is a separate concern.
        vm.Owners = await _ownerRepos.For(tenantId).GetActiveSuppliersAsync(ct);
        vm.Uoms   = await _uomRepos  .For(tenantId).GetActiveAsync(ct);

        var locRepo = _locationRepos.For(tenantId);
        if (vm.FromWarehouseId != Guid.Empty)
        {
            var fromRows = await locRepo.GetActiveByWarehouseAsync(vm.FromWarehouseId, ct);
            vm.FromLocations = fromRows
                .Select(l => new WMS.DAL.Common.LookupItem(l.Id, l.Code, l.Name))
                .ToList();
        }
        if (vm.ToWarehouseId != Guid.Empty)
        {
            var toRows = await locRepo.GetActiveByWarehouseAsync(vm.ToWarehouseId, ct);
            vm.ToLocations = toRows
                .Select(l => new WMS.DAL.Common.LookupItem(l.Id, l.Code, l.Name))
                .ToList();
        }
    }

    private static TransferOrderLineViewModel NewLine(int n) =>
        new() { LineNumber = n, QtyRequested = 0m };

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Inventory.TransferOrder h)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Transfer #", $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.TransferNumber)}</span>"),
            new("Status",     System.Net.WebUtility.HtmlEncode(h.Status)),
            new("Reason",     System.Net.WebUtility.HtmlEncode(h.Reason)),
            new("Notes",      System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")),
        };

        if (h.Status == "Cancelled")
            fields.Add(new("Cancel reason",
                System.Net.WebUtility.HtmlEncode(h.CancelReason ?? "—")));
        if (h.Status == "Lost")
            fields.Add(new("Loss reason",
                System.Net.WebUtility.HtmlEncode(h.LossReason ?? "—")));

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Inventory.TransferOrder h)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Requested", RelativeTime.Format(h.RequestedAt)),
        };

        if (h.SubmittedAt.HasValue)
            props.Add(new("Submitted",  RelativeTime.Format(h.SubmittedAt.Value)));
        if (h.ApprovedAt.HasValue)
            props.Add(new("Approved",   RelativeTime.Format(h.ApprovedAt.Value)));
        if (h.DispatchedAt.HasValue)
            props.Add(new("Dispatched", RelativeTime.Format(h.DispatchedAt.Value)));
        if (h.ReceivedAt.HasValue)
            props.Add(new("Received",   RelativeTime.Format(h.ReceivedAt.Value)));
        if (h.CancelledAt.HasValue)
            props.Add(new("Cancelled",  RelativeTime.Format(h.CancelledAt.Value)));
        if (h.LostAt.HasValue)
            props.Add(new("Lost",       RelativeTime.Format(h.LostAt.Value)));

        return props;
    }

    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null : value;
}
