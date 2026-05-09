using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 7 admin write-side. Partial of ProductsController — see the
// main file for read endpoints (Index, GetData, Detail) and DI.
//
// Validation is layered:
//   1. ASP.NET Core ModelState binding (DataAnnotations) — automatic
//      on POST; drives jQuery unobtrusive client-side too.
//   2. FluentValidation IValidator<T>.ValidateAsync — manually invoked
//      after the DA pass; failures merged into ModelState. Adds enum
//      membership + async Code uniqueness pre-check + cross-field
//      rules.
//   3. DB constraints (UQ on Code, FK on CategoryId/BaseUomId, CHECK
//      on Status/TrackingMethod) — try/catch around the INSERT/UPDATE
//      surfaces them as field-level ModelState errors so the user
//      sees them inline instead of an unhandled exception.
public partial class ProductsController
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new ProductCreateViewModel();
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductCreateViewModel vm, CancellationToken ct)
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

        var entity = new Product
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            CategoryId = vm.CategoryId,
            BaseUomId = vm.BaseUomId,
            TrackingMethod = vm.TrackingMethod,
            VelocityClass = string.IsNullOrWhiteSpace(vm.VelocityClass) ? null : vm.VelocityClass.Trim(),
            UseCatchWeight = vm.UseCatchWeight,
            Status = vm.Status,
            Brand = string.IsNullOrWhiteSpace(vm.Brand) ? null : vm.Brand.Trim(),
        };

        try
        {
            await _repos.For(_tenant.RequireTenantId())
                        .InsertAsync(entity, _currentUser.UserId, ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // UQ violation — surfaces as field error so the user can
            // pick a different Code without losing the rest of the
            // form. The async pre-check usually catches this; this is
            // the race-condition safety net.
            ModelState.AddModelError(nameof(vm.Code), $"Code '{vm.Code}' is already in use.");
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            // FK violation — usually means CategoryId/BaseUomId pointed
            // at a row that was deleted between page load and POST.
            ModelState.AddModelError(string.Empty,
                "Selected category or unit of measure no longer exists. Please reselect.");
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Detail), new { sku = entity.Code });
    }

    [HttpGet("Edit/{code}")]
    public async Task<IActionResult> Edit(string code, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var entity = await _repos.For(tenantId).GetByCodeAsync(code, ct);
        if (entity is null) return NotFound();

        var vm = new ProductEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            CategoryId = entity.CategoryId,
            BaseUomId = entity.BaseUomId,
            TrackingMethod = entity.TrackingMethod,
            VelocityClass = entity.VelocityClass,
            UseCatchWeight = entity.UseCatchWeight,
            Status = entity.Status,
            Brand = entity.Brand,
        };
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, ProductEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
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

        // Code is the route parameter and the read-only display field;
        // the entity passes through carrying it for redirect, never
        // reaches the UPDATE statement (UpdateAsync omits Code from SET).
        var entity = new Product
        {
            Id = vm.Id,
            Code = code,
            Name = vm.Name.Trim(),
            CategoryId = vm.CategoryId,
            BaseUomId = vm.BaseUomId,
            TrackingMethod = vm.TrackingMethod,
            VelocityClass = string.IsNullOrWhiteSpace(vm.VelocityClass) ? null : vm.VelocityClass.Trim(),
            UseCatchWeight = vm.UseCatchWeight,
            Status = vm.Status,
            Brand = string.IsNullOrWhiteSpace(vm.Brand) ? null : vm.Brand.Trim(),
        };

        try
        {
            var ok = await _repos.For(_tenant.RequireTenantId())
                                 .UpdateAsync(entity, _currentUser.UserId, ct);
            if (!ok) return NotFound();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Selected category or unit of measure no longer exists. Please reselect.");
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Detail), new { sku = code });
    }

    private async Task PopulateLookupsAsync(ProductCreateViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        vm.Categories = await _categoryRepos.For(tenantId).GetActiveAsync(ct);
        vm.BaseUoms = await _uomRepos.For(tenantId).GetActiveAsync(ct);
    }

    private async Task PopulateLookupsAsync(ProductEditViewModel vm, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        vm.Categories = await _categoryRepos.For(tenantId).GetActiveAsync(ct);
        vm.BaseUoms = await _uomRepos.For(tenantId).GetActiveAsync(ct);
    }
}
