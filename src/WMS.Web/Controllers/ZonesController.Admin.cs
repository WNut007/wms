using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 2.1 admin write-side. Same Phase 7 pattern as the
// Block 1 CRUDs — adds warehouse picker via ViewBag.Warehouses.
public partial class ZonesController
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateWarehousePickerAsync(ct);
        return View(new ZoneCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ZoneCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateWarehousePickerAsync(ct);
            return View(vm);
        }

        var entity = new Zone
        {
            WarehouseId = vm.WarehouseId,
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            Type = vm.Type,
            Temperature = NullIfBlank(vm.Temperature),
            AllowedProductTypes = NullIfBlank(vm.AllowedProductTypes),
            LotCommingleAllowed = vm.LotCommingleAllowed,
            IsActive = vm.IsActive,
        };

        Guid newId;
        try
        {
            // Capture the returned Id explicitly. The repo's InsertAsync
            // contract returns the assigned Guid — using the return value
            // rather than entity.Id makes the dependency clear and survives
            // test mocks that don't replicate the entity.Id mutation.
            newId = await _repos.For(_tenant.RequireTenantId())
                                .InsertAsync(entity, _currentUser.UserId, ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Composite UQ violation — (WarehouseId, Code) already exists.
            ModelState.AddModelError(nameof(vm.Code),
                $"Code '{vm.Code}' already exists in this warehouse.");
            await PopulateWarehousePickerAsync(ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Edit), new { id = newId });
    }

    // Edit by Id (not Code) — Code isn't tenant-wide unique on Zones,
    // so the route key must be the Guid.
    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var entity = await _repos.For(tenantId).GetByIdAsync(id, ct);
        if (entity is null) return NotFound();

        var warehouse = await _warehouseRepos.For(tenantId).GetByIdAsync(entity.WarehouseId, ct);

        return View(new ZoneEditViewModel
        {
            Id = entity.Id,
            WarehouseId = entity.WarehouseId,
            WarehouseCode = warehouse?.Code ?? "(unknown)",
            WarehouseName = warehouse?.Name ?? "(unknown)",
            Code = entity.Code,
            Name = entity.Name,
            Type = entity.Type,
            Temperature = entity.Temperature,
            AllowedProductTypes = entity.AllowedProductTypes,
            LotCommingleAllowed = entity.LotCommingleAllowed,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ZoneEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Zone
        {
            Id = id,
            WarehouseId = vm.WarehouseId,
            Code = vm.Code,
            Name = vm.Name.Trim(),
            Type = vm.Type,
            Temperature = NullIfBlank(vm.Temperature),
            AllowedProductTypes = NullIfBlank(vm.AllowedProductTypes),
            LotCommingleAllowed = vm.LotCommingleAllowed,
            IsActive = vm.IsActive,
        };

        var ok = await _repos.For(_tenant.RequireTenantId())
                             .UpdateAsync(entity, _currentUser.UserId, ct);
        if (!ok) return NotFound();

        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task PopulateWarehousePickerAsync(CancellationToken ct)
    {
        var rows = await _warehouseRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);
        ViewBag.Warehouses = rows;
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
