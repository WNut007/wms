using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1.2 admin write-side. Same pattern as BoxTypes /
// Phase 7 Warehouses Admin.
public partial class UomsController
{
    [HttpGet("Create")]
    public IActionResult Create() => View(new UomCreateViewModel());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UomCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Uom
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            Type = vm.Type,
            IsBase = vm.IsBase,
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

        return RedirectToAction(nameof(Edit), new { code = entity.Code });
    }

    [HttpGet("Edit/{code}")]
    public async Task<IActionResult> Edit(string code, CancellationToken ct)
    {
        var entity = await _repos.For(_tenant.RequireTenantId()).GetByCodeAsync(code, ct);
        if (entity is null) return NotFound();

        return View(new UomEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Type = entity.Type,
            IsBase = entity.IsBase,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, UomEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Uom
        {
            Id = vm.Id,
            Code = code,
            Name = vm.Name.Trim(),
            // Type intentionally re-applied from the locked VM field —
            // the repo's UPDATE doesn't touch Type anyway, but keeping
            // it on the entity helps any future audit log.
            Type = vm.Type,
            IsBase = vm.IsBase,
            IsActive = vm.IsActive,
        };

        var ok = await _repos.For(_tenant.RequireTenantId())
                             .UpdateAsync(entity, _currentUser.UserId, ct);
        if (!ok) return NotFound();

        return RedirectToAction(nameof(Edit), new { code });
    }
}
