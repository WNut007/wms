using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 2.2 admin write-side. Adds cascade picker plumbing
// (warehouse + zone lookups via ViewBag).
public partial class LocationsController
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateWarehousePickerAsync(ct);
        // Zone picker starts empty — populated client-side via AJAX
        // when warehouse is chosen.
        ViewBag.Zones = Array.Empty<WMS.DAL.Common.LookupItem>();
        return View(new LocationCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LocationCreateViewModel vm, CancellationToken ct)
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
            await PopulateZonePickerAsync(vm.WarehouseId, ct);
            return View(vm);
        }

        var entity = new Location
        {
            WarehouseId = vm.WarehouseId,
            ZoneId = vm.ZoneId,
            Code = vm.Code.Trim(),
            Description = NullIfBlank(vm.Description),
            LengthCm = vm.LengthCm,
            WidthCm = vm.WidthCm,
            HeightCm = vm.HeightCm,
            CapacityVolumeCubicCm = vm.CapacityVolumeCubicCm,
            CapacityWeightKg = vm.CapacityWeightKg,
            CapacityPolicy = vm.CapacityPolicy,
            BinRank = vm.BinRank,
            AllowMultipleItems = vm.AllowMultipleItems,
            MinPickQty = vm.MinPickQty,
            MaxPickQty = vm.MaxPickQty,
            PositionX = vm.PositionX,
            PositionY = vm.PositionY,
            PositionZ = vm.PositionZ,
            Rotation = vm.Rotation,
            Aisle = NullIfBlank(vm.Aisle),
            Bay = vm.Bay,
            Level = vm.Level,
            Show3D = vm.Show3D,
            DisplayColor = NullIfBlank(vm.DisplayColor),
            IsPickface = vm.IsPickface,
            Status = vm.Status,
            IsActive = vm.IsActive,
        };

        Guid newId;
        try
        {
            newId = await _repos.For(_tenant.RequireTenantId())
                                .InsertAsync(entity, _currentUser.UserId, ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            ModelState.AddModelError(nameof(vm.Code),
                $"Code '{vm.Code}' already exists in this warehouse.");
            await PopulateWarehousePickerAsync(ct);
            await PopulateZonePickerAsync(vm.WarehouseId, ct);
            return View(vm);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            // FK violation — likely Zone not in selected warehouse, or
            // FK to Warehouses/Zones referencing missing parent.
            ModelState.AddModelError(nameof(vm.ZoneId),
                "Zone is invalid for the selected warehouse.");
            await PopulateWarehousePickerAsync(ct);
            await PopulateZonePickerAsync(vm.WarehouseId, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Edit), new { id = newId });
    }

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var entity = await _repos.For(tenantId).GetByIdAsync(id, ct);
        if (entity is null) return NotFound();

        var warehouse = await _warehouseRepos.For(tenantId).GetByIdAsync(entity.WarehouseId, ct);
        // Pre-populate zone picker with the current-warehouse zones so
        // the dropdown renders correctly even before the user touches it.
        await PopulateZonePickerAsync(entity.WarehouseId, ct);

        return View(new LocationEditViewModel
        {
            Id = entity.Id,
            WarehouseId = entity.WarehouseId,
            WarehouseCode = warehouse?.Code ?? "(unknown)",
            WarehouseName = warehouse?.Name ?? "(unknown)",
            ZoneId = entity.ZoneId,
            Code = entity.Code,
            Description = entity.Description,
            LengthCm = entity.LengthCm,
            WidthCm = entity.WidthCm,
            HeightCm = entity.HeightCm,
            CapacityVolumeCubicCm = entity.CapacityVolumeCubicCm,
            CapacityWeightKg = entity.CapacityWeightKg,
            CapacityPolicy = entity.CapacityPolicy,
            BinRank = entity.BinRank,
            AllowMultipleItems = entity.AllowMultipleItems,
            MinPickQty = entity.MinPickQty,
            MaxPickQty = entity.MaxPickQty,
            PositionX = entity.PositionX,
            PositionY = entity.PositionY,
            PositionZ = entity.PositionZ,
            Rotation = entity.Rotation,
            Aisle = entity.Aisle,
            Bay = entity.Bay,
            Level = entity.Level,
            Show3D = entity.Show3D,
            DisplayColor = entity.DisplayColor,
            IsPickface = entity.IsPickface,
            Status = entity.Status,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LocationEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateZonePickerAsync(vm.WarehouseId, ct);
            return View(vm);
        }

        var entity = new Location
        {
            Id = id,
            WarehouseId = vm.WarehouseId,
            ZoneId = vm.ZoneId,
            Code = vm.Code,
            Description = NullIfBlank(vm.Description),
            LengthCm = vm.LengthCm,
            WidthCm = vm.WidthCm,
            HeightCm = vm.HeightCm,
            CapacityVolumeCubicCm = vm.CapacityVolumeCubicCm,
            CapacityWeightKg = vm.CapacityWeightKg,
            CapacityPolicy = vm.CapacityPolicy,
            BinRank = vm.BinRank,
            AllowMultipleItems = vm.AllowMultipleItems,
            MinPickQty = vm.MinPickQty,
            MaxPickQty = vm.MaxPickQty,
            PositionX = vm.PositionX,
            PositionY = vm.PositionY,
            PositionZ = vm.PositionZ,
            Rotation = vm.Rotation,
            Aisle = NullIfBlank(vm.Aisle),
            Bay = vm.Bay,
            Level = vm.Level,
            Show3D = vm.Show3D,
            DisplayColor = NullIfBlank(vm.DisplayColor),
            IsPickface = vm.IsPickface,
            Status = vm.Status,
            IsActive = vm.IsActive,
        };

        try
        {
            var ok = await _repos.For(_tenant.RequireTenantId())
                                 .UpdateAsync(entity, _currentUser.UserId, ct);
            if (!ok) return NotFound();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            ModelState.AddModelError(nameof(vm.ZoneId),
                "Zone is invalid for this warehouse.");
            await PopulateZonePickerAsync(vm.WarehouseId, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task PopulateWarehousePickerAsync(CancellationToken ct)
    {
        var rows = await _warehouseRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);
        ViewBag.Warehouses = rows;
    }

    private async Task PopulateZonePickerAsync(Guid warehouseId, CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
        {
            ViewBag.Zones = Array.Empty<WMS.DAL.Common.LookupItem>();
            return;
        }
        var rows = await _repos.For(_tenant.RequireTenantId())
                               .GetZonesForWarehouseAsync(warehouseId, ct);
        ViewBag.Zones = rows;
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
