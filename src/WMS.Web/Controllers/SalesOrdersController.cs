using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Outbound;
using WMS.Web.Models.Outbound;
using WMS.Web.Services;
using WMS.Web.Services.Mappers;
using WMS.Web.ViewModels.Detail;

namespace WMS.Web.Controllers;

// Phase 14A — Sales Order admin CRUD (MVP foundation).
// Phase 14B — Allocation primitive (Allocate POST + alloc panel).
//   GET  /SalesOrders                  — Alpine list with chip counts
//   GET  /SalesOrders/Data             — JSON envelope (5 status counts)
//   GET  /SalesOrders/Create           — multi-line Create form
//   POST /SalesOrders/Create           — validate → service → redirect
//   GET  /SalesOrders/Edit/{id}        — edit form (header always; lines if Draft)
//   POST /SalesOrders/Edit/{id}        — validate → service → redirect
//   GET  /SalesOrders/Detail/{id}      — _DetailLayout w/ Lines + Allocations panels
//   POST /SalesOrders/Submit/{id}      — Draft → Open
//   POST /SalesOrders/Allocate/{id}    — Open|Allocating → Allocating|Allocated
//   POST /SalesOrders/Cancel/{id}      — pre-Cancelled → Cancelled (TX-wraps reversal)
[Authorize]
[Route("SalesOrders")]
public sealed class SalesOrdersController : Controller
{
    private readonly ISalesOrderRepositoryFactory _repos;
    private readonly ISalesOrderService _service;
    private readonly IAllocationService _allocationService;
    private readonly IPickTaskService _pickTaskService;
    private readonly IPackTaskService _packTaskService;
    private readonly IShipmentService _shipmentService;
    private readonly IOrderAllocationRepositoryFactory _allocRepos;
    private readonly ICustomerRepositoryFactory _customerRepos;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly IOwnerRepositoryFactory _ownerRepos;
    private readonly IUomRepositoryFactory _uomRepos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<SalesOrderCreateViewModel> _createValidator;
    private readonly IValidator<SalesOrderEditViewModel> _editValidator;

    public SalesOrdersController(
        ISalesOrderRepositoryFactory repos,
        ISalesOrderService service,
        IAllocationService allocationService,
        IPickTaskService pickTaskService,
        IPackTaskService packTaskService,
        IShipmentService shipmentService,
        IOrderAllocationRepositoryFactory allocRepos,
        ICustomerRepositoryFactory customerRepos,
        IWarehouseRepositoryFactory warehouseRepos,
        IProductRepositoryFactory productRepos,
        IOwnerRepositoryFactory ownerRepos,
        IUomRepositoryFactory uomRepos,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<SalesOrderCreateViewModel> createValidator,
        IValidator<SalesOrderEditViewModel> editValidator)
    {
        _repos = repos;
        _service = service;
        _allocationService = allocationService;
        _pickTaskService = pickTaskService;
        _packTaskService = packTaskService;
        _shipmentService = shipmentService;
        _allocRepos = allocRepos;
        _customerRepos = customerRepos;
        _warehouseRepos = warehouseRepos;
        _productRepos = productRepos;
        _ownerRepos = ownerRepos;
        _uomRepos = uomRepos;
        _tenant = tenant;
        _currentUser = currentUser;
        _createValidator = createValidator;
        _editValidator = editValidator;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Data")]
    public async Task<IActionResult> GetData(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? customer = null,
        string? warehouse = null,
        string sortBy = "orderDate",
        bool sortDesc = true,
        CancellationToken ct = default)
    {
        var filter = new SalesOrderFilter(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: SalesOrderStatusMapper.FromWire(status),
            CustomerCode: NormaliseFilter(customer),
            WarehouseCode: NormaliseFilter(warehouse),
            SortBy: sortBy,
            SortDesc: sortDesc);

        var repo = _repos.For(_tenant.RequireTenantId());
        var result = await repo.GetPagedAsync(filter, ct);
        var counts = await repo.GetStatusCountsAsync(filter, ct);

        return Json(new
        {
            items = result.Items.Select(r => new
            {
                id                = r.Id,
                soNumber          = r.SoNumber,
                customerCode      = r.CustomerCode,
                customerName      = r.CustomerName,
                warehouseCode     = r.WarehouseCode,
                orderDate         = r.OrderDate,
                requestedShipDate = r.RequestedShipDate,
                status            = SalesOrderStatusMapper.ToWire(r.Status),
                statusLabel       = r.Status,
                lineCount         = r.LineCount,
                totalQuantity     = r.TotalQuantity,
                createdByName     = r.CreatedByName,
                createdAt         = r.CreatedAt,
                createdRelative   = RelativeTime.Format(r.CreatedAt),
            }),
            total      = result.Total,
            page       = result.Page,
            pageSize   = result.PageSize,
            totalPages = result.TotalPages,
            counts     = new
            {
                all             = counts.All,
                draft           = counts.Draft,
                open            = counts.Open,
                allocating      = counts.Allocating,
                allocated       = counts.Allocated,
                picking         = counts.Picking,
                picked          = counts.Picked,
                partiallypicked = counts.PartiallyPicked,
                packed          = counts.Packed,
                shipped         = counts.Shipped,
                cancelled       = counts.Cancelled,
            },
        });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new SalesOrderCreateViewModel
        {
            WarehouseId = _currentUser.WarehouseId ?? Guid.Empty,
            OrderDate = DateTime.UtcNow.Date,
            Lines = new() { NewLine(1) },
        };
        await PopulateCreateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        SalesOrderCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateCreateLookupsAsync(vm, ct);
            return View(vm);
        }

        try
        {
            var requesterId = _currentUser.UserId
                ?? throw new InvalidOperationException(
                    "Cannot create sales order without an authenticated user.");

            var request = new CreateSalesOrderRequest(
                CustomerId:        vm.CustomerId,
                WarehouseId:       vm.WarehouseId,
                OrderDate:         DateOnly.FromDateTime(vm.OrderDate),
                RequestedShipDate: vm.RequestedShipDate.HasValue
                                    ? DateOnly.FromDateTime(vm.RequestedShipDate.Value)
                                    : null,
                Notes:             string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
                Lines: vm.Lines.Select((l, i) => new CreateSalesOrderLineRequest(
                    LineNumber:      l.LineNumber > 0 ? l.LineNumber : i + 1,
                    ProductId:       l.ProductId,
                    OwnerId:         l.OwnerId,
                    UomId:           l.UomId,
                    OrderedQuantity: l.OrderedQuantity,
                    UnitPrice:       l.UnitPrice,
                    Notes:           string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                    .ToList());

            var saved = await _service.CreateAsync(
                _tenant.RequireTenantId(), request, requesterId, ct);

            TempData["SalesOrderMessage"] =
                $"Sales order {saved.Header.SoNumber} created — {saved.Lines.Count} lines (Draft).";
            return RedirectToAction(nameof(Detail), new { id = saved.Header.Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateCreateLookupsAsync(vm, ct);
            return View(vm);
        }
    }

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _service.GetByIdAsync(tenantId, id, ct);
        if (detail is null) return NotFound();

        if (detail.Header.Status == "Cancelled")
        {
            TempData["SalesOrderError"] = "Cannot edit a cancelled sales order.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        var customer = await _customerRepos.For(tenantId).GetByIdAsync(detail.Header.CustomerId, ct);
        // Warehouse code/name is fetched from the GetActiveAsync list; cheaper than a dedicated GetById.
        var warehouses = await _warehouseRepos.For(tenantId).GetActiveAsync(ct);
        var warehouse = warehouses.FirstOrDefault(w => w.Id == detail.Header.WarehouseId);

        var linesLocked = detail.Header.Status != "Draft";
        var vm = new SalesOrderEditViewModel
        {
            Id = id,
            SoNumber = detail.Header.SoNumber,
            Status = detail.Header.Status,
            CustomerId = detail.Header.CustomerId,
            CustomerCode = customer?.Code ?? "",
            CustomerName = customer?.Name ?? "",
            WarehouseId = detail.Header.WarehouseId,
            WarehouseCode = warehouse?.Code ?? "",
            WarehouseName = warehouse?.Name ?? "",
            OrderDate = detail.Header.OrderDate.ToDateTime(TimeOnly.MinValue),
            RequestedShipDate = detail.Header.RequestedShipDate?.ToDateTime(TimeOnly.MinValue),
            Notes = detail.Header.Notes,
            ReplaceLines = false,
            LinesLocked = linesLocked,
            Lines = detail.Lines.Select(l => new SalesOrderLineViewModel
            {
                LineNumber = l.LineNumber,
                ProductId = l.ProductId,
                OwnerId = l.OwnerId,
                UomId = l.UomId,
                OrderedQuantity = l.OrderedQuantity,
                UnitPrice = l.UnitPrice,
                Notes = l.Notes,
            }).ToList(),
        };

        await PopulateEditLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id, SalesOrderEditViewModel vm, CancellationToken ct)
    {
        // Route id is authoritative — guard against tampering.
        vm.Id = id;

        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateEditLookupsAsync(vm, ct);
            return View(vm);
        }

        try
        {
            var requesterId = _currentUser.UserId
                ?? throw new InvalidOperationException("Authenticated user required.");

            var request = new UpdateSalesOrderRequest(
                OrderDate:         DateOnly.FromDateTime(vm.OrderDate),
                RequestedShipDate: vm.RequestedShipDate.HasValue
                                    ? DateOnly.FromDateTime(vm.RequestedShipDate.Value)
                                    : null,
                Notes:             string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
                ReplaceLines:      vm.ReplaceLines,
                Lines: vm.ReplaceLines
                    ? vm.Lines.Select((l, i) => new UpdateSalesOrderLineRequest(
                        LineNumber:      l.LineNumber > 0 ? l.LineNumber : i + 1,
                        ProductId:       l.ProductId,
                        OwnerId:         l.OwnerId,
                        UomId:           l.UomId,
                        OrderedQuantity: l.OrderedQuantity,
                        UnitPrice:       l.UnitPrice,
                        Notes:           string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim()))
                        .ToList()
                    : Array.Empty<UpdateSalesOrderLineRequest>());

            var saved = await _service.UpdateAsync(
                _tenant.RequireTenantId(), id, request, requesterId, ct);

            TempData["SalesOrderMessage"] =
                $"Sales order {saved.Header.SoNumber} updated.";
            return RedirectToAction(nameof(Detail), new { id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("", ex.Message);
            await PopulateEditLookupsAsync(vm, ct);
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
        var allocations = await _allocRepos.For(tenantId).GetActiveBySalesOrderIdAsync(id, ct);
        var h = detail.Header;

        var customer = await _customerRepos.For(tenantId).GetByIdAsync(h.CustomerId, ct);
        var warehouses = await _warehouseRepos.For(tenantId).GetActiveAsync(ct);
        var warehouse = warehouses.FirstOrDefault(w => w.Id == h.WarehouseId);

        var isDraft           = h.Status == "Draft";
        var isOpen            = h.Status == "Open";
        var isAllocating      = h.Status == "Allocating";
        var isAllocated       = h.Status == "Allocated";
        var isPicking         = h.Status == "Picking";
        var isPicked          = h.Status == "Picked";
        var isPartiallyPicked = h.Status == "PartiallyPicked";
        var isPacked          = h.Status == "Packed";
        var isShipped         = h.Status == "Shipped";
        var isCancelled       = h.Status == "Cancelled";

        var canEdit     = !isCancelled;
        var canSubmit   = isDraft;
        var canAllocate = isOpen || isAllocating;   // Allocated is no-op (idempotent); show greyed
        // Phase 14C — Generate pick active only when fully Allocated.
        // Picking returns the existing task idempotently — also clickable
        // so a re-trigger lands on the same Detail page.
        var canGenerate = isAllocated || isPicking;
        // Phase 14D — Generate pack active when SO has been picked
        // (Picked or PartiallyPicked). Idempotent on existing Pending
        // pack task — controller redirects to the same Detail.
        var canGeneratePack = isPicked || isPartiallyPicked;
        // Phase 14E — Generate shipment active when SO is Packed.
        // Idempotent on existing Pending shipment.
        var canGenerateShipment = isPacked;
        // Cancel narrows to pre-pick states. Once a pick task exists,
        // operator must cancel the pick task first (revert SO Picking →
        // Allocated) before cancelling the SO. Picked/PartiallyPicked/
        // Packed/Shipped are post-Submit terminals — need a future
        // return-to-stock workflow to reverse.
        var canCancel   = isDraft || isOpen || isAllocating || isAllocated;

        var statusVariant = SalesOrderStatusMapper.ToBadgeVariant(h.Status);
        var totalQuantity  = lineRows.Sum(l => l.OrderedQuantity);
        var totalAllocated = lineRows.Sum(l => l.AllocatedQuantity);
        var totalShortfall = totalQuantity - totalAllocated;

        // "Allocated" stat tile: x.xx / y.yy with green tint when fully
        // allocated, amber when partial. Tracks the visible chip variant
        // logic so the page reads coherently.
        var allocColor = totalShortfall <= 0m && totalQuantity > 0m
            ? "#0F6E56"
            : (totalAllocated > 0m ? "#854F0B" : null);

        var vm = new DetailPageViewModel
        {
            EntityType = "SalesOrder",
            EntityId = id.ToString(),
            Title = h.SoNumber,
            Subtitle = $"{lineRows.Count} lines · {customer?.Name ?? "—"} · {h.Status}",
            IconClass = "ti-shopping-cart",
            IconBgColor = "#EEEDFE",
            IconFgColor = "#534AB7",
            AvatarInitials = "",
            StatusLabel = h.Status,
            StatusVariant = statusVariant,
            BreadcrumbParent = "Sales Orders",
            BreadcrumbParentUrl = "/SalesOrders",
            Stats = new()
            {
                new("Lines",     lineRows.Count.ToString("N0")),
                new("Quantity",  totalQuantity.ToString("N2")),
                new("Allocated",
                    totalQuantity > 0m
                        ? $"{totalAllocated:N2} / {totalQuantity:N2}"
                        : "—",
                    allocColor),
                new("Status",    h.Status),
            },
            ShowImagesTab = false,
            CustomTabs = new()
            {
                new("lines", "Lines", "ti-list-details",
                    "Detail/_SalesOrderLinesPanel", lineRows.Count),
                // Allocations tab visible whenever the SO has any
                // active allocation (or is in an allocation-aware
                // state); count drives the badge.
                new("allocations", "Allocations", "ti-tags",
                    "Detail/_SalesOrderAllocationsPanel", allocations.Count),
            },
            QuickActions = new()
            {
                new("Edit",              "ti-edit",
                    canEdit             ? $"/SalesOrders/Edit/{id}" : "#", Enabled: canEdit),
                new("Submit",            "ti-send",
                    canSubmit           ? "#submit-modal"           : "#", Enabled: canSubmit),
                new("Allocate",          "ti-tags",
                    canAllocate         ? "#allocate-modal"         : "#", Enabled: canAllocate),
                new("Generate pick",     "ti-list-check",
                    canGenerate         ? "#generate-modal"         : "#", Enabled: canGenerate),
                new("Generate pack",     "ti-package",
                    canGeneratePack     ? "#generate-pack-modal"    : "#", Enabled: canGeneratePack),
                new("Generate shipment", "ti-truck-delivery",
                    canGenerateShipment ? "#generate-ship-modal"    : "#", Enabled: canGenerateShipment),
                new("Cancel",            "ti-x",
                    canCancel           ? "#cancel-modal"           : "#", Enabled: canCancel),
            },
            OverviewFields = BuildOverviewFields(h, customer, warehouse),
            Properties = BuildProperties(h),
        };

        ViewBag.HeaderId           = h.Id;
        ViewBag.HeaderStatus       = h.Status;
        ViewBag.IsDraft            = isDraft;
        ViewBag.IsOpen             = isOpen;
        ViewBag.IsAllocating       = isAllocating;
        ViewBag.IsAllocated        = isAllocated;
        ViewBag.IsPicking          = isPicking;
        ViewBag.IsPicked           = isPicked;
        ViewBag.IsPartiallyPicked  = isPartiallyPicked;
        ViewBag.IsPacked           = isPacked;
        ViewBag.IsShipped          = isShipped;
        ViewBag.CanSubmit          = canSubmit;
        ViewBag.CanAllocate        = canAllocate;
        ViewBag.CanGenerate        = canGenerate;
        ViewBag.CanGeneratePack    = canGeneratePack;
        ViewBag.CanGenerateShipment = canGenerateShipment;
        ViewBag.CanCancel          = canCancel;
        ViewBag.LineRows           = lineRows;
        ViewBag.AllocationRows     = allocations;
        ViewBag.SalesOrderMessage  = TempData["SalesOrderMessage"] as string;
        ViewBag.SalesOrderError    = TempData["SalesOrderError"]   as string;
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
            TempData["SalesOrderMessage"] = changed
                ? "Sales order submitted — Draft → Open."
                : "Sales order was already submitted.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SalesOrderError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("Allocate/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Allocate(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            // Strategy name = null → resolver picks default (FIFO for
            // MVP). Future UI can pass a per-tenant configured name.
            var result = await _allocationService.AllocateAsync(
                tenantId, id, strategyName: null, requesterId, ct);

            TempData["SalesOrderMessage"] = result.IsFullyAllocated
                ? $"Fully allocated — {result.LineCount} line(s) reserved against stock."
                : $"Partially allocated — {result.FullyAllocatedLineCount} of {result.LineCount} line(s) fully filled. Re-run when more stock arrives.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SalesOrderError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    // Phase 14C — generates a pick task from the SO's Active
    // OrderAllocations and flips SO Allocated → Picking. Idempotent on
    // already-Picking (returns the existing task; the redirect lands on
    // its Detail page either way).
    [HttpPost("Generate/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var result = await _pickTaskService.GenerateAsync(
                tenantId, id, requesterId, ct);

            TempData["PickTaskMessage"] =
                $"Pick task {result.PickNumber} generated — {result.LineCount} line(s), total expected {result.TotalExpectedQuantity:N2}.";
            return RedirectToAction("Detail", "PickTasks", new { id = result.PickTaskId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["SalesOrderError"] = ex.Message;
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    // Phase 14D — generates a pack task from the SO's positively-picked
    // lines. SO header NOT flipped on Generate (no Packing intermediate
    // state for MVP); pack-in-flight detected via existing-task guard.
    // Idempotent on existing Pending pack task — controller redirects
    // to its Detail.
    [HttpPost("GeneratePack/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePack(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var result = await _packTaskService.GenerateAsync(
                tenantId, id, requesterId, ct);

            TempData["PackTaskMessage"] =
                $"Pack task {result.PackNumber} generated — {result.LineCount} line(s), total picked {result.TotalPickedQuantity:N2}.";
            return RedirectToAction("Detail", "PackTasks", new { id = result.PackTaskId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["SalesOrderError"] = ex.Message;
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    // Phase 14E — generates a shipment from a Packed SO. SO header NOT
    // flipped on Generate (mirrors 14D Pack); ship-in-flight detected
    // via existing-task guard. Idempotent on existing Pending shipment.
    [HttpPost("GenerateShipment/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateShipment(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var result = await _shipmentService.GenerateAsync(
                tenantId, id, requesterId, ct);

            TempData["ShipmentMessage"] =
                $"Shipment {result.ShipmentNumber} generated — fill in carrier + tracking and submit to dispatch.";
            return RedirectToAction("Detail", "Shipments", new { id = result.ShipmentId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["SalesOrderError"] = ex.Message;
            return RedirectToAction(nameof(Detail), new { id });
        }
    }

    [HttpPost("Cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var requesterId = _currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user required.");

        try
        {
            var changed = await _service.CancelAsync(tenantId, id, requesterId, ct);
            TempData["SalesOrderMessage"] = changed
                ? "Sales order cancelled."
                : "Sales order was already cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SalesOrderError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    // ====================================================================
    // Lookup population helpers
    // ====================================================================

    private async Task PopulateCreateLookupsAsync(
        SalesOrderCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();

        vm.Customers = await _customerRepos.For(tenantId).GetActiveAsync(ct);
        vm.Warehouses = (await _warehouseRepos.For(tenantId).GetActiveAsync(ct))
            .Select(w => new WMS.DAL.Common.LookupItem(w.Id, w.Code, w.Name))
            .ToList();
        vm.Products = await _productRepos.For(tenantId).GetActiveAsync(ct);
        vm.Owners = await _ownerRepos.For(tenantId).GetActiveSuppliersAsync(ct);
        vm.Uoms = await _uomRepos.For(tenantId).GetActiveAsync(ct);
    }

    private async Task PopulateEditLookupsAsync(
        SalesOrderEditViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        vm.Products = await _productRepos.For(tenantId).GetActiveAsync(ct);
        vm.Owners = await _ownerRepos.For(tenantId).GetActiveSuppliersAsync(ct);
        vm.Uoms = await _uomRepos.For(tenantId).GetActiveAsync(ct);
    }

    private static SalesOrderLineViewModel NewLine(int n) =>
        new() { LineNumber = n, OrderedQuantity = 0m };

    private static List<KeyValuePair<string, string>> BuildOverviewFields(
        Domain.Entities.Outbound.SalesOrder h,
        Domain.Entities.Master.Customer? customer,
        WMS.Common.Auth.WarehouseInfo? warehouse)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("SO #",       $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(h.SoNumber)}</span>"),
            new("Status",     System.Net.WebUtility.HtmlEncode(h.Status)),
            new("Customer",   customer is null
                ? "—"
                : $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(customer.Code)}</span> · " +
                  System.Net.WebUtility.HtmlEncode(customer.Name)),
            new("Warehouse",  warehouse is null
                ? "—"
                : $"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(warehouse.Code)}</span> · " +
                  System.Net.WebUtility.HtmlEncode(warehouse.Name)),
            new("Order date", h.OrderDate.ToString("yyyy-MM-dd")),
            new("Requested ship",
                h.RequestedShipDate.HasValue
                    ? h.RequestedShipDate.Value.ToString("yyyy-MM-dd")
                    : "—"),
            new("Notes",      System.Net.WebUtility.HtmlEncode(h.Notes ?? "—")),
        };

        return fields;
    }

    private static List<KeyValuePair<string, string>> BuildProperties(
        Domain.Entities.Outbound.SalesOrder h)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Created", RelativeTime.Format(h.CreatedAt)),
        };

        if (h.UpdatedAt.HasValue)
            props.Add(new("Updated", RelativeTime.Format(h.UpdatedAt.Value)));

        return props;
    }

    private static string? NormaliseFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null : value;
}
