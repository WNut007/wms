using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Web.Models.Inbound;
using WMS.BLL.Services.Inbound;

namespace WMS.Web.Controllers;

// Phase 9A admin write-side. Partial of PurchaseOrdersController.
public partial class PurchaseOrdersController
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new PurchaseOrderCreateViewModel
        {
            // Default warehouse from the operator's session claim, if any.
            WarehouseId = _currentUser.WarehouseId ?? Guid.Empty,
            // Start with one empty line so the grid isn't blank on first render.
            Lines = new List<PurchaseOrderLineViewModel>
            {
                new() { LineNumber = 1, ExpectedQuantity = 1m },
            },
        };
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchaseOrderCreateViewModel vm, CancellationToken ct)
    {
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

        var request = new CreatePurchaseOrderRequest(
            PoNumber: vm.PoNumber.Trim(),
            OwnerId: vm.OwnerId,
            WarehouseId: vm.WarehouseId,
            ExpectedDate: vm.ExpectedDate,
            Notes: string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            Lines: vm.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new CreatePurchaseOrderLineRequest(
                    LineNumber: l.LineNumber,
                    ProductId: l.ProductId,
                    UomId: l.UomId,
                    ExpectedQuantity: l.ExpectedQuantity))
                .ToList());

        try
        {
            var detail = await _service.CreateAsync(
                _tenant.RequireTenantId(), request, _currentUser.UserId, ct);
            return RedirectToAction(nameof(Detail), new { id = detail.Header.Id });
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            ModelState.AddModelError(nameof(vm.PoNumber),
                $"PO number '{vm.PoNumber}' is already in use.");
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Selected vendor, warehouse, product, or unit no longer exists. Please reselect.");
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var repo = _repos.For(tenantId);

        var detail = await repo.GetByIdAsync(id, ct);
        if (detail is null) return NotFound();

        // Lookup display strings for the readonly Vendor + Warehouse
        // fields. Cheaper than a full repo round-trip — pull from the
        // detail header's FK plus the (already-loaded) lookups when
        // the form populates them.
        var receivedCount = await repo.CountReceivedLinesAsync(id, ct);
        var locked = receivedCount > 0;

        var vm = new PurchaseOrderEditViewModel
        {
            Id = id,
            PoNumber = detail.Header.PoNumber,
            ExpectedDate = detail.Header.ExpectedDate,
            Notes = detail.Header.Notes,
            Status = detail.Header.Status,
            LinesLocked = locked,
            Lines = detail.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new PurchaseOrderLineViewModel
                {
                    LineNumber = l.LineNumber,
                    ProductId = l.ProductId,
                    UomId = l.UomId,
                    ExpectedQuantity = l.ExpectedQuantity,
                    ReceivedQuantity = l.ReceivedQuantity,
                })
                .ToList(),
        };

        await PopulateEditLookupsAsync(vm, detail.Header.OwnerId, detail.Header.WarehouseId, ct);
        return View(vm);
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PurchaseOrderEditViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var repo = _repos.For(tenantId);

        // Server-side authoritative lock check — UI's LinesLocked is
        // a hint, not trustworthy.
        var receivedCount = await repo.CountReceivedLinesAsync(id, ct);
        vm.LinesLocked = receivedCount > 0;
        vm.Id = id;

        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            var detail = await repo.GetByIdAsync(id, ct);
            await PopulateEditLookupsAsync(vm,
                detail?.Header.OwnerId ?? Guid.Empty,
                detail?.Header.WarehouseId ?? Guid.Empty, ct);
            return View(vm);
        }

        var request = new UpdatePurchaseOrderRequest(
            ExpectedDate: vm.ExpectedDate,
            Notes: string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            ReplaceLines: !vm.LinesLocked,
            Lines: (vm.LinesLocked || vm.Lines is null)
                ? Array.Empty<UpdatePurchaseOrderLineRequest>()
                : vm.Lines
                    .OrderBy(l => l.LineNumber)
                    .Select(l => new UpdatePurchaseOrderLineRequest(
                        LineNumber: l.LineNumber,
                        ProductId: l.ProductId,
                        UomId: l.UomId,
                        ExpectedQuantity: l.ExpectedQuantity))
                    .ToList());

        try
        {
            await _service.UpdateAsync(tenantId, id, request, _currentUser.UserId, ct);
            return RedirectToAction(nameof(Detail), new { id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Selected product or unit no longer exists. Please reselect.");
        }

        var detailRefresh = await repo.GetByIdAsync(id, ct);
        await PopulateEditLookupsAsync(vm,
            detailRefresh?.Header.OwnerId ?? Guid.Empty,
            detailRefresh?.Header.WarehouseId ?? Guid.Empty, ct);
        return View(vm);
    }

    [HttpPost("Archive/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var ok = await _service.ArchiveAsync(
            _tenant.RequireTenantId(), id, _currentUser.UserId, ct);
        if (!ok) return BadRequest("PO is already archived or in a non-cancellable state.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    // ====================================================================

    private async Task PopulateLookupsAsync(PurchaseOrderCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        vm.Vendors    = await _ownerRepos.For(tenantId).GetActiveSuppliersAsync(ct);
        var warehouses = await _warehouseRepos.For(tenantId).GetActiveAsync(ct);
        vm.Warehouses = warehouses.Select(w => new WMS.DAL.Common.LookupItem(w.Id, w.Code, w.Name)).ToList();
        vm.Products   = await _productRepos.For(tenantId).GetActiveAsync(ct);
        vm.Uoms       = await _uomRepos.For(tenantId).GetActiveAsync(ct);
    }

    private async Task PopulateEditLookupsAsync(
        PurchaseOrderEditViewModel vm, Guid ownerId, Guid warehouseId, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        vm.Products = await _productRepos.For(tenantId).GetActiveAsync(ct);
        vm.Uoms     = await _uomRepos.For(tenantId).GetActiveAsync(ct);

        // Resolve Vendor + Warehouse codes for read-only display.
        if (ownerId != Guid.Empty)
        {
            var owners = await _ownerRepos.For(tenantId).GetActiveSuppliersAsync(ct);
            var found = owners.FirstOrDefault(o => o.Id == ownerId);
            vm.VendorCode = found?.Code ?? "—";
            vm.VendorName = found?.Name ?? "";
        }
        if (warehouseId != Guid.Empty)
        {
            var wh = await _warehouseRepos.For(tenantId).GetByIdAsync(warehouseId, ct);
            vm.WarehouseCode = wh?.Code ?? "—";
        }
    }
}
