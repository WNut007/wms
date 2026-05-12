using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1 admin write-side. Same Phase 7 pattern as
// WarehousesController.Admin.cs — DataAnnotations runs at model-binding;
// FluentValidation merges into ModelState; SqlException 2627/2601
// (unique-violation) becomes a field error on Code as a fallback for
// the rare race where the async uniqueness check let a dup through.
public partial class BoxTypesController
{
    [HttpGet("Create")]
    public IActionResult Create() => View(new BoxTypeCreateViewModel());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BoxTypeCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new BoxType
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            InternalLengthCm = vm.InternalLengthCm,
            InternalWidthCm = vm.InternalWidthCm,
            InternalHeightCm = vm.InternalHeightCm,
            InternalVolumeCubicCm = vm.InternalVolumeCubicCm,
            ExternalLengthCm = vm.ExternalLengthCm,
            ExternalWidthCm = vm.ExternalWidthCm,
            ExternalHeightCm = vm.ExternalHeightCm,
            EmptyWeightKg = vm.EmptyWeightKg,
            MaxLoadKg = vm.MaxLoadKg,
            UnitCost = vm.UnitCost,
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

        return View(new BoxTypeEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            InternalLengthCm = entity.InternalLengthCm,
            InternalWidthCm = entity.InternalWidthCm,
            InternalHeightCm = entity.InternalHeightCm,
            InternalVolumeCubicCm = entity.InternalVolumeCubicCm,
            ExternalLengthCm = entity.ExternalLengthCm,
            ExternalWidthCm = entity.ExternalWidthCm,
            ExternalHeightCm = entity.ExternalHeightCm,
            EmptyWeightKg = entity.EmptyWeightKg,
            MaxLoadKg = entity.MaxLoadKg,
            UnitCost = entity.UnitCost,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, BoxTypeEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new BoxType
        {
            Id = vm.Id,
            Code = code,
            Name = vm.Name.Trim(),
            InternalLengthCm = vm.InternalLengthCm,
            InternalWidthCm = vm.InternalWidthCm,
            InternalHeightCm = vm.InternalHeightCm,
            InternalVolumeCubicCm = vm.InternalVolumeCubicCm,
            ExternalLengthCm = vm.ExternalLengthCm,
            ExternalWidthCm = vm.ExternalWidthCm,
            ExternalHeightCm = vm.ExternalHeightCm,
            EmptyWeightKg = vm.EmptyWeightKg,
            MaxLoadKg = vm.MaxLoadKg,
            UnitCost = vm.UnitCost,
            IsActive = vm.IsActive,
        };

        var ok = await _repos.For(_tenant.RequireTenantId())
                             .UpdateAsync(entity, _currentUser.UserId, ct);
        if (!ok) return NotFound();

        return RedirectToAction(nameof(Edit), new { code });
    }
}
