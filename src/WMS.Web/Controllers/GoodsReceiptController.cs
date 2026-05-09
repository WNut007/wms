using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.BLL.Services.Inbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Controllers;

// Phase 9B — Desktop Goods Receipt form. Single Create endpoint
// (no Index / Edit yet; those land in Phase 9C). Two submit modes
// driven by the IsDraft flag on the view-model: Save Draft creates
// a Draft header without stock movement; Post Receipt runs the full
// orchestration via PostReceivingAsync (existing behaviour).
[Authorize]
[Route("GoodsReceipt")]
public sealed class GoodsReceiptController : Controller
{
    private readonly IReceivingHeaderService _service;
    private readonly IPurchaseOrderRepositoryFactory _poRepos;
    private readonly IWarehouseRepositoryFactory _warehouseRepos;
    private readonly IOwnerRepositoryFactory _ownerRepos;
    private readonly IProductRepositoryFactory _productRepos;
    private readonly IUomRepositoryFactory _uomRepos;
    private readonly ILocationRepositoryFactory _locationRepos;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<GoodsReceiptCreateViewModel> _validator;
    private readonly ILogger<GoodsReceiptController> _logger;

    public GoodsReceiptController(
        IReceivingHeaderService service,
        IPurchaseOrderRepositoryFactory poRepos,
        IWarehouseRepositoryFactory warehouseRepos,
        IOwnerRepositoryFactory ownerRepos,
        IProductRepositoryFactory productRepos,
        IUomRepositoryFactory uomRepos,
        ILocationRepositoryFactory locationRepos,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IValidator<GoodsReceiptCreateViewModel> validator,
        ILogger<GoodsReceiptController> logger)
    {
        _service = service;
        _poRepos = poRepos;
        _warehouseRepos = warehouseRepos;
        _ownerRepos = ownerRepos;
        _productRepos = productRepos;
        _uomRepos = uomRepos;
        _locationRepos = locationRepos;
        _tenant = tenant;
        _currentUser = currentUser;
        _validator = validator;
        _logger = logger;
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(Guid? poId, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var sessionWh = _currentUser.WarehouseId ?? Guid.Empty;

        var vm = new GoodsReceiptCreateViewModel
        {
            ReceivingNumber = await GenerateReceivingNumberAsync(tenantId, ct),
            ReceivedAt = DateTime.UtcNow,
            WarehouseId = sessionWh,
            // One blank starter line (matches Phase 9A PO Create UX).
            Lines = new List<GoodsReceiptLineViewModel>
            {
                new() { LineNumber = 1, ReceivedQuantity = 1m, OwnerId = Guid.Empty },
            },
        };

        // Pre-fill from PO if provided via query string.
        if (poId is { } id)
        {
            await PrefillFromPoAsync(vm, id, tenantId, ct);
        }

        await PopulateLookupsAsync(vm, tenantId, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        GoodsReceiptCreateViewModel vm, string action, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();

        // The "action" parameter comes from the submit button's name=
        // attribute. "draft" → IsDraft=true; "post" → IsDraft=false.
        vm.IsDraft = string.Equals(action, "draft", StringComparison.OrdinalIgnoreCase);

        var fv = await _validator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateLookupsAsync(vm, tenantId, ct);
            return View(vm);
        }

        var request = new PostReceivingRequest(
            ReceivingNumber: vm.ReceivingNumber.Trim(),
            PurchaseOrderId: vm.PurchaseOrderId,
            WarehouseId: vm.WarehouseId,
            ReceivedAt: vm.ReceivedAt,
            Notes: string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            Lines: vm.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new PostReceivingLineRequest(
                    LineNumber: l.LineNumber,
                    PurchaseOrderLineId: l.PurchaseOrderLineId,
                    ProductId: l.ProductId,
                    UomId: l.UomId,
                    OwnerId: l.OwnerId,
                    LocationId: l.LocationId,
                    ReceivedQuantity: l.ReceivedQuantity,
                    Lot: string.IsNullOrWhiteSpace(l.LotNumber)
                        ? null
                        : new LotInfo(l.LotNumber.Trim(), DateOnly.FromDateTime(DateTime.UtcNow), l.ExpiryDate.HasValue ? DateOnly.FromDateTime(l.ExpiryDate.Value) : null),
                    Pallet: string.IsNullOrWhiteSpace(l.PalletNumber)
                        ? null
                        : new PalletInfo(l.PalletNumber.Trim())))
                .ToList(),
            IsDraft: vm.IsDraft);

        try
        {
            var detail = await _service.PostReceivingAsync(
                tenantId, request, _currentUser.UserId, ct);

            // Phase 9B has no GR Detail page yet; redirect to existing
            // /Receive/Posted/{id} for the confirmation screen. Phase 9C
            // replaces this with /Receiving/Detail/{number}.
            return RedirectToAction("Posted", "Receive", new { id = detail.Header.Id });
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            ModelState.AddModelError(nameof(vm.ReceivingNumber),
                $"Receiving number '{vm.ReceivingNumber}' is already in use.");
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Selected vendor, product, location, or unit no longer exists. Please reselect.");
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        await PopulateLookupsAsync(vm, tenantId, ct);
        return View(vm);
    }

    // JSON endpoint — returns the open + partially-received lines on
    // a PO, so the Create form can pre-fill the line grid via Alpine
    // fetch when the user picks a PO from the selector.
    [HttpGet("PoLines/{poId:guid}")]
    public async Task<IActionResult> PoLines(Guid poId, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var detail = await _poRepos.For(tenantId).GetByIdAsync(poId, ct);
        if (detail is null) return NotFound();

        // Open + partially-received lines only — closed/cancelled
        // lines aren't expected to receive more.
        var openLines = detail.Lines
            .Where(l => l.Status is "Open" or "PartiallyReceived")
            .OrderBy(l => l.LineNumber)
            .Select(l => new
            {
                purchaseOrderLineId = l.Id,
                lineNumber          = l.LineNumber,
                productId           = l.ProductId,
                uomId               = l.UomId,
                expectedQuantity    = l.ExpectedQuantity,
                receivedQuantity    = l.ReceivedQuantity,
                outstandingQuantity = l.ExpectedQuantity - l.ReceivedQuantity,
            })
            .ToList();

        return Json(new
        {
            poId = detail.Header.Id,
            poNumber = detail.Header.PoNumber,
            ownerId = detail.Header.OwnerId,
            warehouseId = detail.Header.WarehouseId,
            lines = openLines,
        });
    }

    // -----------------------------------------------------------------

    private async Task PrefillFromPoAsync(
        GoodsReceiptCreateViewModel vm, Guid poId, Guid tenantId, CancellationToken ct)
    {
        var detail = await _poRepos.For(tenantId).GetByIdAsync(poId, ct);
        if (detail is null) return;

        vm.PurchaseOrderId = poId;
        vm.PoNumber = detail.Header.PoNumber;
        vm.WarehouseId = detail.Header.WarehouseId;

        vm.Lines = detail.Lines
            .Where(l => l.Status is "Open" or "PartiallyReceived")
            .OrderBy(l => l.LineNumber)
            .Select(l => new GoodsReceiptLineViewModel
            {
                LineNumber = l.LineNumber,
                PurchaseOrderLineId = l.Id,
                ProductId = l.ProductId,
                UomId = l.UomId,
                OwnerId = detail.Header.OwnerId,
                ExpectedQuantity = l.ExpectedQuantity - l.ReceivedQuantity,
                ReceivedQuantity = l.ExpectedQuantity - l.ReceivedQuantity,
            })
            .ToList();
    }

    private async Task PopulateLookupsAsync(
        GoodsReceiptCreateViewModel vm, Guid tenantId, CancellationToken ct)
    {
        var warehouses = await _warehouseRepos.For(tenantId).GetActiveAsync(ct);
        vm.Warehouses = warehouses.Select(w => new LookupItem(w.Id, w.Code, w.Name)).ToList();
        vm.Vendors    = await _ownerRepos.For(tenantId).GetActiveSuppliersAsync(ct);
        vm.Products   = await _productRepos.For(tenantId).GetActiveAsync(ct);
        vm.Uoms       = await _uomRepos.For(tenantId).GetActiveAsync(ct);
        vm.Owners     = vm.Vendors;  // Same source — operator-facing label "Owner" maps to suppliers.

        // Locations scoped to the chosen warehouse. When WarehouseId is
        // unset, fall back to empty — UI re-fetches when warehouse picked.
        vm.Locations = vm.WarehouseId == Guid.Empty
            ? Array.Empty<LookupItem>()
            : await _locationRepos.For(tenantId).GetActiveByWarehouseAsync(vm.WarehouseId, ct);

        // Open POs for the picker — get a paged read filtered to
        // Status='Open' OR 'Receiving' (closed / cancelled don't accept
        // more receipts).
        var openPos = await _poRepos.For(tenantId).GetPagedAsync(
            new PurchaseOrderFilter(PageSize: 200, Status: "Open"), ct);
        var receivingPos = await _poRepos.For(tenantId).GetPagedAsync(
            new PurchaseOrderFilter(PageSize: 200, Status: "Receiving"), ct);
        vm.OpenPurchaseOrders = openPos.Items
            .Concat(receivingPos.Items)
            .Select(p => new LookupItem(p.Id, p.PoNumber, $"{p.OwnerCode} · {p.OwnerName}"))
            .ToList();
    }

    private async Task<string> GenerateReceivingNumberAsync(Guid tenantId, CancellationToken ct)
    {
        // Simple "GR-YYYYMMDD-NNNN" pattern — operator can override in
        // the form. Phase 9B+ may move this to a counter table for true
        // uniqueness; for now, the timestamp + a 4-char suffix from
        // Guid keeps collisions astronomically unlikely.
        var ts = DateTime.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
        return $"GR-{ts}-{suffix}";
    }
}
