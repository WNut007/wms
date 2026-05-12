using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1.4 admin write-side. Adds parent-picker plumbing
// (ViewBag.Parents) on top of the Phase 7 admin template.
//
// Two DB-level invariants surface here as SqlException:
//   * Self-parent CHECK (Migration_029) → SqlException 547. Caught and
//     mapped to a friendly ParentId error.
//   * Cycle trigger (Migration_030) → RAISERROR with message containing
//     "parent chain forms a cycle.". Caught by message-match (no
//     stable error number for RAISERROR).
public partial class CategoriesController
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateParentPickerAsync(excludeId: null, ct);
        return View(new CategoryCreateViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateParentPickerAsync(excludeId: null, ct);
            return View(vm);
        }

        var entity = new ProductCategory
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            ParentId = vm.ParentId,
            Description = NullIfBlank(vm.Description),
            DisplayOrder = vm.DisplayOrder,
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
            await PopulateParentPickerAsync(excludeId: null, ct);
            return View(vm);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            // CHECK constraint or FK violation. Self-parent CHECK +
            // ParentId FK both surface here. Surface as ParentId error.
            ModelState.AddModelError(nameof(vm.ParentId),
                "Parent reference is invalid (cannot reference itself or a non-existent category).");
            await PopulateParentPickerAsync(excludeId: null, ct);
            return View(vm);
        }
        catch (SqlException ex) when (ex.Message.Contains("parent chain forms a cycle", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(vm.ParentId),
                "Cannot create this category — the chosen parent would form a cycle in the tree.");
            await PopulateParentPickerAsync(excludeId: null, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Edit), new { code = entity.Code });
    }

    [HttpGet("Edit/{code}")]
    public async Task<IActionResult> Edit(string code, CancellationToken ct)
    {
        var entity = await _repos.For(_tenant.RequireTenantId()).GetByCodeAsync(code, ct);
        if (entity is null) return NotFound();

        await PopulateParentPickerAsync(excludeId: entity.Id, ct);

        return View(new CategoryEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Path = entity.Path,
            Name = entity.Name,
            ParentId = entity.ParentId,
            Description = entity.Description,
            DisplayOrder = entity.DisplayOrder,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, CategoryEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid)
        {
            await PopulateParentPickerAsync(excludeId: vm.Id, ct);
            return View(vm);
        }

        var entity = new ProductCategory
        {
            Id = vm.Id,
            Code = code,
            Name = vm.Name.Trim(),
            ParentId = vm.ParentId,
            Description = NullIfBlank(vm.Description),
            DisplayOrder = vm.DisplayOrder,
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
            ModelState.AddModelError(nameof(vm.ParentId),
                "Parent reference is invalid (cannot reference itself or a non-existent category).");
            await PopulateParentPickerAsync(excludeId: vm.Id, ct);
            return View(vm);
        }
        catch (SqlException ex) when (ex.Message.Contains("parent chain forms a cycle", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(vm.ParentId),
                "Cannot save — the chosen parent would form a cycle in the tree.");
            await PopulateParentPickerAsync(excludeId: vm.Id, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Edit), new { code });
    }

    private async Task PopulateParentPickerAsync(Guid? excludeId, CancellationToken ct)
    {
        var picker = await _repos.For(_tenant.RequireTenantId())
                                 .GetPickerItemsAsync(excludeId, ct);
        ViewBag.Parents = picker;
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
