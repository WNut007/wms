using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 30A.3 Block 1.3 admin write-side. Same pattern as the earlier
// lookup CRUDs.
//
// Status flip workflow: per Phase 30A.3 Mod #2, the 4 seeded carriers
// (FLASH/KERRY/JT/THAIPOST) ship at Status='Inactive' and IsActive=1.
// Block 1.3 ships only the CRUD; the operator's first acceptance test
// is flipping each from Inactive → Production via this Edit form.
public partial class CarriersController
{
    [HttpGet("Create")]
    public IActionResult Create() => View(new CarrierCreateViewModel());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CarrierCreateViewModel vm, CancellationToken ct)
    {
        var fv = await _createValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Carrier
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            Type = NullIfBlank(vm.Type),
            Country = NullIfBlank(vm.Country),
            Status = vm.Status,
            SupportedServiceLevels = NullIfBlank(vm.SupportedServiceLevels),
            WebsiteUrl = NullIfBlank(vm.WebsiteUrl),
            LogoUrl = NullIfBlank(vm.LogoUrl),
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
            return View(vm);
        }

        return RedirectToAction(nameof(Edit), new { code = entity.Code });
    }

    [HttpGet("Edit/{code}")]
    public async Task<IActionResult> Edit(string code, CancellationToken ct)
    {
        var entity = await _repos.For(_tenant.RequireTenantId()).GetByCodeAsync(code, ct);
        if (entity is null) return NotFound();

        return View(new CarrierEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Type = entity.Type,
            Country = entity.Country,
            Status = entity.Status,
            SupportedServiceLevels = entity.SupportedServiceLevels,
            WebsiteUrl = entity.WebsiteUrl,
            LogoUrl = entity.LogoUrl,
            DisplayOrder = entity.DisplayOrder,
            IsActive = entity.IsActive,
        });
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, CarrierEditViewModel vm, CancellationToken ct)
    {
        var fv = await _editValidator.ValidateAsync(vm, ct);
        if (!fv.IsValid)
        {
            foreach (var err in fv.Errors)
                ModelState.AddModelError(err.PropertyName, err.ErrorMessage);
        }

        if (!ModelState.IsValid) return View(vm);

        var entity = new Carrier
        {
            Id = vm.Id,
            Code = code,
            Name = vm.Name.Trim(),
            Type = NullIfBlank(vm.Type),
            Country = NullIfBlank(vm.Country),
            Status = vm.Status,
            SupportedServiceLevels = NullIfBlank(vm.SupportedServiceLevels),
            WebsiteUrl = NullIfBlank(vm.WebsiteUrl),
            LogoUrl = NullIfBlank(vm.LogoUrl),
            DisplayOrder = vm.DisplayOrder,
            IsActive = vm.IsActive,
        };

        var ok = await _repos.For(_tenant.RequireTenantId())
                             .UpdateAsync(entity, _currentUser.UserId, ct);
        if (!ok) return NotFound();

        return RedirectToAction(nameof(Edit), new { code });
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
