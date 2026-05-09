using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 7 admin write-side. Partial of WarehousesController. Simplest
// of the three:
//   - 2-step Alpine stepper in the view (Identity / Operations).
//   - No lookup repos consumed (no FKs out of master.Warehouses).
//   - No Status string — IsActive bool is the canonical archive
//     toggle (TD-009 acknowledged; "maintenance" intermediate state
//     not in schema yet).
public partial class WarehousesController
{
    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new WarehouseCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WarehouseCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Warehouse
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            Type = vm.Type,
            Address = NullIfBlank(vm.Address),
            PhoneNumber = NullIfBlank(vm.PhoneNumber),
            ManagerName = NullIfBlank(vm.ManagerName),
            TimeZoneId = NullIfBlank(vm.TimeZoneId),
            IsActive = vm.IsActive,
        };

        try
        {
            await _repos.For(_tenant.RequireTenantId())
                        .InsertAsync(entity, _currentUser.UserId, ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            ModelState.AddModelError(nameof(vm.Code), $"Code '{vm.Code}' is already in use.");
            return View(vm);
        }

        return RedirectToAction(nameof(Detail), new { code = entity.Code });
    }

    [HttpGet("Edit/{code}")]
    public async Task<IActionResult> Edit(string code, CancellationToken ct)
    {
        var entity = await _repos.For(_tenant.RequireTenantId()).GetByCodeAsync(code, ct);
        if (entity is null) return NotFound();

        return View(new WarehouseEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Type = entity.Type,
            Address = entity.Address,
            PhoneNumber = entity.PhoneNumber,
            ManagerName = entity.ManagerName,
            TimeZoneId = entity.TimeZoneId,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, WarehouseEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Warehouse
        {
            Id = vm.Id,
            Code = code,
            Name = vm.Name.Trim(),
            Type = vm.Type,
            Address = NullIfBlank(vm.Address),
            PhoneNumber = NullIfBlank(vm.PhoneNumber),
            ManagerName = NullIfBlank(vm.ManagerName),
            TimeZoneId = NullIfBlank(vm.TimeZoneId),
            IsActive = vm.IsActive,
        };

        var ok = await _repos.For(_tenant.RequireTenantId())
                             .UpdateAsync(entity, _currentUser.UserId, ct);
        if (!ok) return NotFound();

        return RedirectToAction(nameof(Detail), new { code });
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
