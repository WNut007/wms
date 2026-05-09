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

// Phase 11A (ADR-013) — General Stock Adjustment workflow.
//   GET  /Adjustments              — Alpine list with chip counts
//   GET  /Adjustments/Data         — JSON envelope (rows + counts)
//   GET  /Adjustments/Create       — single-form page
//   POST /Adjustments/Create       — validate → service → redirect to Detail
//   GET  /Adjustments/Detail/{id}  — _DetailLayout with audit + Approve/Reject
//   POST /Adjustments/Approve/{id} — TX-wrapped apply (Pending → Applied)
//   POST /Adjustments/Reject/{id}  — terminal Reject with reason
//   GET  /Adjustments/Locations/{whId} — JSON helper for cascading dropdown
[Authorize]
[Route("Adjustments")]
public sealed class AdjustmentsController : Controller
{
    private readonly IAdjustmentRepositoryFactory _repos;
    private readonly IAdjustmentService _service;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly IOwnerRepositoryFactory _ownerRepos;
    private readonly IUomRepositoryFactory _uomRepos;
    private readonly ILocationRepositoryFactory _locationRepos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<AdjustmentCreateViewModel> _createValidator;
    private readonly IValidator<RejectAdjustmentViewModel> _rejectValidator;

    public AdjustmentsController(
        IAdjustmentRepositoryFactory repos,
        IAdjustmentService service,
        IWarehouseRepositoryFactory warehouseRepos,
        IProductRepositoryFactory productRepos,
        IOwnerRepositoryFactory ownerRepos,
        IUomRepositoryFactory uomRepos,
        ILocationRepositoryFactory locationRepos,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<AdjustmentCreateViewModel> createValidator,
        IValidator<RejectAdjustmentViewModel> rejectValidator)
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
        _rejectValidator = rejectValidator;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? reason = null,
        string? warehouse = null,
        string sortBy = "requestedAt",
        bool sortDesc = true,
        CancellationToken ct = default)
    {
        var filter = new AdjustmentFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: AdjustmentStatusMapper.FromWire(status),
            Reason: NormaliseFilter(reason),
            WarehouseCode: NormaliseFilter(warehouse),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(a => new
            {
                id              = a.Id,
                adjustmentNumber = a.AdjustmentNumber,
                productCode     = a.ProductCode,
                productName     = a.ProductName,
                warehouseCode   = a.WarehouseCode,
                locationCode    = a.LocationCode,
                ownerCode       = a.OwnerCode,
                uomCode         = a.UomCode,
                quantityDelta   = a.QuantityDelta,
                reason          = a.Reason,
                status          = AdjustmentStatusMapper.ToWire(a.Status),
                statusLabel     = a.Status,
                requestedByName = a.RequestedByName,
                requestedAt     = a.RequestedAt,
                requestedRelative = RelativeTime.Format(a.RequestedAt),
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all      = counts.All,
                pending  = counts.Pending,
                applied  = counts.Applied,
                rejected = counts.Rejected,
            },
        });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new AdjustmentCreateViewModel
        {
            WarehouseId = _currentUser.WarehouseId ?? Guid.Empty,
        };
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        AdjustmentCreateViewModel vm, CancellationToken ct)
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
                    "Cannot create adjustment without an authenticated user.");

            var request = new CreateAdjustmentRequest(
                LocationId: vm.LocationId,
                ProductId:  vm.ProductId,
                LotId:      vm.LotId,
                PalletId:   vm.PalletId,
                OwnerId:    vm.OwnerId,
                UomId:      vm.UomId,
                WarehouseId: vm.WarehouseId,
                QuantityDelta: vm.QuantityDelta,
                Reason: vm.Reason,
                Notes: string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
                AllowCreateNew: vm.AllowCreateNew);

            var saved = await _service.CreateAsync(tenantId, request, requesterId, ct);
            TempData["AdjustmentMessage"] = $"Adjustment {saved.AdjustmentNumber} created — pending approval.";
            return RedirectToAction(nameof(Detail), new { id = saved.Id });
        }
        catch (ArgumentException ex)
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
        var adj = await _service.GetByIdAsync(tenantId, id, ct);
        if (adj is null) return NotFound();

        var (statusLabel, statusVariant) = (
            adj.Status,
            AdjustmentStatusMapper.ToBadgeVariant(adj.Status));

        // Approval gating: button enabled only when (a) status is
        // Pending AND (b) currentUser != requester (separation of duties).
        var currentUserId = _currentUser.UserId;
        var canDecide = adj.Status == "Pending"
            && currentUserId is not null
            && currentUserId.Value != adj.RequestedBy;

        var vm = new DetailPageViewModel
        {
            EntityType = "Adjustment",
            EntityId = id.ToString(),
            Title = adj.AdjustmentNumber,
            Subtitle = $"{(adj.QuantityDelta >= 0 ? "+" : "")}{adj.QuantityDelta:N2} · {adj.Reason} · {adj.Status}",
            IconClass = adj.QuantityDelta >= 0 ? "ti-plus" : "ti-minus",
            IconBgColor = adj.QuantityDelta >= 0 ? "#E1F5EE" : "#FCEBEB",
            IconFgColor = adj.QuantityDelta >= 0 ? "#0F6E56" : "#A32D2D",
            AvatarInitials = "",
            StatusLabel = statusLabel,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Adjustments",
            BreadcrumbParentUrl = "/Adjustments",
            Stats = new()
            {
                new("Delta",     $"{(adj.QuantityDelta >= 0 ? "+" : "")}{adj.QuantityDelta:N2}",
                                  adj.QuantityDelta >= 0 ? "#1D9E75" : "#A32D2D"),
                new("Reason",    adj.Reason),
                new("Status",    adj.Status),
                new("Stock row", adj.StockId.HasValue ? "Linked" : "Pending resolve",
                                  adj.StockId.HasValue ? "#0C447C" : null),
            },
            ShowImagesTab = false,
            QuickActions = new()
            {
                new("Approve & apply", "ti-check",
                    canDecide ? "#approve-modal" : "#",
                    Enabled: canDecide),
                new("Reject",          "ti-x",
                    canDecide ? "#reject-modal" : "#",
                    Enabled: canDecide),
            },
            OverviewFields = BuildOverviewFields(adj),
            Properties = BuildProperties(adj),
        };

        ViewBag.HeaderId   = adj.Id;
        ViewBag.CanDecide  = canDecide;
        ViewBag.SelfApproval = adj.Status == "Pending"
            && currentUserId is not null
            && currentUserId.Value == adj.RequestedBy;
        ViewBag.AdjustmentMessage = TempData["AdjustmentMessage"] as string;
        ViewBag.AdjustmentError   = TempData["AdjustmentError"] as string;
        return View("~/Views/Shared/_DetailLayout.cshtml", vm);
    }

    [HttpPost("Approve/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var approverId = _currentUser.UserId
            ?? throw new InvalidOperationException("Approver user id missing.");

        try
        {
            var changed = await _service.ApproveAsync(tenantId, id, approverId, ct);
            TempData["AdjustmentMessage"] = changed
                ? "Adjustment approved & applied. Stock + Movement Log updated."
                : "Adjustment was already applied — no action taken.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["AdjustmentError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Reject/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        Guid id, RejectAdjustmentViewModel vm, CancellationToken ct)
    {
        vm = vm with { Id = id };

        var fv = await _rejectValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            TempData["AdjustmentError"] = fv.Errors.FirstOrDefault()?.ErrorMessage
                ?? "Validation failed.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var tenantId = _tenant.RequireTenantId();
        var rejecterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Rejecter user id missing.");

        try
        {
            var changed = await _service.RejectAsync(
                tenantId, id, vm.Reason.Trim(), rejecterId, ct);
            TempData["AdjustmentMessage"] = changed
                ? "Adjustment rejected."
                : "Adjustment was already rejected — no action taken.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["AdjustmentError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpGet("Locations/{warehouseId:guid}")]
    public async Task<IActionResult> Locations(Guid warehouseId, CancellationToken ct)
    {
        // AJAX helper for the cascading Warehouse → Location dropdown
        // on the Create form. Mirrors the GoodsReceipt PoLines endpoint
        // shape (Phase 9B).
        var rows = await _locationRepos.For(_tenant.RequireTenantId())
                                       .GetActiveByWarehouseAsync(warehouseId, ct);
        return Json(rows.Select(l => new { id = l.Id, code = l.Code, name = l.Name }));
    }

    private async Task PopulateLookupsAsync(
        AdjustmentCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();

        vm.Warehouses = (await _warehouseRepos.For(tenantId).GetActiveAsync(ct))
            .Select(w => new WMS.DAL.Common.LookupItem(w.Id, w.Code, w.Name))
            .ToList();
        vm.Products = await _productRepos.For(tenantId).GetActiveAsync(ct);
        vm.Owners   = await _ownerRepos  .For(tenantId).GetActiveSuppliersAsync(ct);
        vm.Uoms     = await _uomRepos    .For(tenantId).GetActiveAsync(ct);

        // Pre-populate Locations if the form already knows the
        // warehouse (re-display after validation failure, or session
        // pre-fill).
        if (vm.WarehouseId != Guid.Empty)
        {
            var rows = await _locationRepos.For(tenantId)
                                           .GetActiveByWarehouseAsync(vm.WarehouseId, ct);
            vm.Locations = rows.Select(l => new WMS.DAL.Common.LookupItem(l.Id, l.Code, l.Name)).ToList();
        }
    }

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Inventory.Adjustment a)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Adjustment #", $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(a.AdjustmentNumber)}</span>"),
            new("Status",       System.Net.WebUtility.HtmlEncode(a.Status)),
            new("Reason",       System.Net.WebUtility.HtmlEncode(a.Reason)),
            new("Quantity delta",
                $"<span style=\"font-weight:500;color:{(a.QuantityDelta >= 0 ? "#0F6E56" : "#A32D2D")}\">" +
                $"{(a.QuantityDelta >= 0 ? "+" : "")}{a.QuantityDelta:N2}</span>"),
            new("Notes",        System.Net.WebUtility.HtmlEncode(a.Notes ?? "—")),
        };

        if (a.Status == "Rejected")
        {
            fields.Add(new("Rejection reason",
                System.Net.WebUtility.HtmlEncode(a.RejectionReason ?? "—")));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Inventory.Adjustment a)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Requested", RelativeTime.Format(a.RequestedAt)),
        };

        if (a.AppliedAt.HasValue)
            props.Add(new("Applied", RelativeTime.Format(a.AppliedAt.Value)));
        if (a.RejectedAt.HasValue)
            props.Add(new("Rejected", RelativeTime.Format(a.RejectedAt.Value)));

        return props;
    }

    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null : value;
}
