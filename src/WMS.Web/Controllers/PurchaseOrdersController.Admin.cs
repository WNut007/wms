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

        // Block 4.5.2 d.2 — Closed/Cancelled POs are terminal; the
        // Edit form has nothing the operator could change that would
        // persist. Send them to the read-only Detail view instead.
        // Prevents misleading "edit" UX on finalized records.
        if (detail.Header.Status is "Closed" or "Cancelled")
            return RedirectToAction(nameof(Detail), new { id });

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
        vm.Id = id;

        // Authoritative DB state. Drives lock classification + the
        // Closed/Cancelled redirect (defence; GET already redirects).
        var detail = await repo.GetByIdAsync(id, ct);
        if (detail is null) return NotFound();
        if (detail.Header.Status is "Closed" or "Cancelled")
            return RedirectToAction(nameof(Detail), new { id });

        // Server overrides the VM's hint with DB truth.
        vm.LinesLocked = detail.Lines.Any(l => l.ReceivedQuantity > 0);

        // d.2.3.a.2 + d.2.3.b — repopulate vm.Lines from DB state for
        // LOCKED ROWS ONLY. Two reasons to repopulate:
        //   1. d.2.2's whole-grid disable left every UoM <select> empty
        //      in the POST → vm.Lines[*].UomId arrived Guid.Empty across
        //      the board → error-path re-render showed '—'.
        //   2. d.2.3.b's per-row disable still leaves locked-row UomId
        //      hidden in POST (via :disabled on the readonly text or
        //      via x-if branch with hidden inputs); operator tampering
        //      could still inject bad values into locked-row POST keys.
        //
        // Scope MUST be locked rows only — d.2.3.b's per-row UI allows
        // the operator to edit unlocked rows on a partial-lock PO.
        // Repopulating ALL vm.Lines (the earlier shape) would clobber
        // those operator edits with DB values, silently losing the
        // edit. dbLine.ReceivedQuantity > 0 narrows to truly-locked rows.
        if (vm.LinesLocked && vm.Lines is { Count: > 0 })
        {
            var dbByLineNumber = detail.Lines.ToDictionary(l => l.LineNumber);
            foreach (var line in vm.Lines)
            {
                if (dbByLineNumber.TryGetValue(line.LineNumber, out var dbLine)
                    && dbLine.ReceivedQuantity > 0)
                {
                    line.ProductId = dbLine.ProductId;
                    line.UomId = dbLine.UomId;
                    line.ExpectedQuantity = dbLine.ExpectedQuantity;
                    line.ReceivedQuantity = dbLine.ReceivedQuantity;
                }
            }
        }

        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateEditLookupsAsync(vm, detail.Header.OwnerId, detail.Header.WarehouseId, ct);
            return View(vm);
        }

        // d.2.3.b — per-line classification (TD-026 closure). The UI
        // now sends per-row state: locked rows round-trip via hidden
        // inputs (LineNumber + ProductId + UomId; readonly Qty input
        // POSTs its current value); unlocked rows submit their editable
        // selections. Classification by LineNumber — Path X (d.2.3.b
        // could switch to line.Id but LineNumber matches the existing
        // POST shape's stable key + the UI doesn't change LineNumber
        // on Edit mode, so no Id-vs-LineNumber drift risk).
        //
        // Two filters layered:
        //   1. dbLockedNumbers — locked DB lines never enter updates
        //      or deletes. Operator-tampered POSTs that try to mutate
        //      a locked LineNumber are silently dropped at this layer
        //      AND refused at the service layer (defence in depth).
        //   2. Service-side ReceivedQuantity > 0 check in
        //      UpdatePartialAsync — final authority. If an in-flight
        //      receipt landed between classification and service call,
        //      the service refuses with a friendly error.
        var dbLinesByNumber = detail.Lines.ToDictionary(l => l.LineNumber);
        var dbLockedNumbers = detail.Lines
            .Where(l => l.ReceivedQuantity > 0)
            .Select(l => l.LineNumber)
            .ToHashSet();
        var postedLineNumbers = (vm.Lines ?? new())
            .Select(l => l.LineNumber).ToHashSet();

        var lineUpdates = new List<PartialUpdateLineEdit>();
        var lineInserts = new List<PartialUpdateLineInsert>();
        var lineDeletes = new List<Guid>();

        foreach (var posted in vm.Lines ?? Enumerable.Empty<PurchaseOrderLineViewModel>())
        {
            if (dbLockedNumbers.Contains(posted.LineNumber)) continue;
            if (dbLinesByNumber.TryGetValue(posted.LineNumber, out var dbLine))
                lineUpdates.Add(new PartialUpdateLineEdit(
                    dbLine.Id, posted.ProductId, posted.UomId, posted.ExpectedQuantity));
            else
                lineInserts.Add(new PartialUpdateLineInsert(
                    posted.LineNumber, posted.ProductId, posted.UomId, posted.ExpectedQuantity));
        }

        foreach (var dbLine in detail.Lines)
        {
            if (dbLockedNumbers.Contains(dbLine.LineNumber)) continue;
            if (!postedLineNumbers.Contains(dbLine.LineNumber))
                lineDeletes.Add(dbLine.Id);
        }

        var request = new PartialUpdatePurchaseOrderRequest(
            ExpectedDate: vm.ExpectedDate,
            Notes: string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
            LineUpdates: lineUpdates,
            LineInserts: lineInserts,
            LineDeletes: lineDeletes);

        try
        {
            await _service.UpdatePartialAsync(tenantId, id, request, _currentUser.UserId, ct);
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

        await PopulateEditLookupsAsync(vm, detail.Header.OwnerId, detail.Header.WarehouseId, ct);
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
