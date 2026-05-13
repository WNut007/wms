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

        // d.2.3.a.2 — repopulate vm.Lines from DB state when whole-grid
        // lock is on. The d.2.2 UI disables every UoM <select> in this
        // case, so vm.Lines arrives with Guid.Empty UomId across the
        // board (disabled selects don't POST). Two consequences without
        // this repopulate:
        //   1. Razor's error-path re-render shows '—' for every UoM cell.
        //   2. Even unrelated future validation surfaces would see bad
        //      input shape.
        // Source-of-truth handoff: the controller's short-circuit (below)
        // already drops line classification when LinesLocked; this just
        // brings the bound VM into agreement with DB so re-render is
        // sane. d.2.3.b's per-row UI will only disable per-locked-row,
        // so this fixup no-ops there.
        if (vm.LinesLocked && vm.Lines is { Count: > 0 })
        {
            var dbByLineNumber = detail.Lines.ToDictionary(l => l.LineNumber);
            foreach (var line in vm.Lines)
            {
                if (dbByLineNumber.TryGetValue(line.LineNumber, out var dbLine))
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

        // d.2.3.a.1 — short-circuit when whole-grid lock is on. The
        // d.2.2 UI applies LinesLocked = (any DB line has receipts) to
        // the WHOLE grid, disabling every UoM <select> regardless of
        // per-line state. Disabled <select>s don't POST their value, so
        // vm.Lines[*].UomId arrives Guid.Empty across the board. Per-
        // line classification against this body would mis-classify the
        // unlocked-but-UI-disabled rows as legitimate "edit to empty
        // UomId" attempts — service-layer would refuse with "UomId is
        // required". Effective semantics for d.2.2 UI's locked state:
        // header-only update. d.2.3.b's per-row UI sends per-line state
        // and replaces this branch with real classification.
        var lineUpdates = new List<PartialUpdateLineEdit>();
        var lineInserts = new List<PartialUpdateLineInsert>();
        var lineDeletes = new List<Guid>();

        if (!vm.LinesLocked)
        {
            // Unlocked path: no DB line has receipts. UI keeps all
            // cells editable, POST carries authoritative values.
            // Classification by LineNumber — Path X (transitional;
            // d.2.3.b switches to line.Id once UI adds the hidden POST
            // field).
            var dbLinesByNumber = detail.Lines.ToDictionary(l => l.LineNumber);
            var postedLineNumbers = (vm.Lines ?? new())
                .Select(l => l.LineNumber).ToHashSet();

            foreach (var posted in vm.Lines ?? Enumerable.Empty<PurchaseOrderLineViewModel>())
            {
                if (dbLinesByNumber.TryGetValue(posted.LineNumber, out var dbLine))
                    lineUpdates.Add(new PartialUpdateLineEdit(
                        dbLine.Id, posted.ProductId, posted.UomId, posted.ExpectedQuantity));
                else
                    lineInserts.Add(new PartialUpdateLineInsert(
                        posted.LineNumber, posted.ProductId, posted.UomId, posted.ExpectedQuantity));
            }

            foreach (var dbLine in detail.Lines)
            {
                if (!postedLineNumbers.Contains(dbLine.LineNumber))
                    lineDeletes.Add(dbLine.Id);
            }
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
