using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Controllers;

// Phase 7 admin write-side. Partial of CustomersController. Mirrors
// the Products pattern; key differences:
//   - 4-step Alpine stepper in the view (Identity / Contact /
//     Commercial / Status). Backed by a flat VM — stepper is a UI
//     affordance only.
//   - One lookup repo (Carriers, optional FK on PreferredCarrierId).
//   - CustomerCreateValidator carries the B2B cross-field rule —
//     CompanyName + TaxId required when CustomerType == "B2B".
public partial class CustomersController
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new CustomerCreateViewModel();
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerCreateViewModel vm, CancellationToken ct)
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

        var entity = new Customer
        {
            Code = vm.Code.Trim(),
            Name = vm.Name.Trim(),
            CustomerType = vm.CustomerType,
            CompanyName = NullIfBlank(vm.CompanyName),
            TaxId = NullIfBlank(vm.TaxId),
            Email = NullIfBlank(vm.Email),
            Phone = NullIfBlank(vm.Phone),
            Country = NullIfBlank(vm.Country),
            CustomerTier = NullIfBlank(vm.CustomerTier),
            AnnualRevenue = vm.AnnualRevenue,
            OrdersPerMonth = vm.OrdersPerMonth,
            AvgOrderValue = vm.AvgOrderValue,
            IsKeyAccount = vm.IsKeyAccount,
            IsStrategic = vm.IsStrategic,
            AllocationPriority = vm.AllocationPriority,
            SafetyStockDays = vm.SafetyStockDays,
            PromisedFillRate = vm.PromisedFillRate,
            PreferredCarrierId = vm.PreferredCarrierId,
            DefaultPaymentTerms = NullIfBlank(vm.DefaultPaymentTerms),
            Status = vm.Status,
        };

        try
        {
            await _repos.For(_tenant.RequireTenantId())
                        .InsertAsync(entity, _currentUser.UserId, ct);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            ModelState.AddModelError(nameof(vm.Code), $"Code '{vm.Code}' is already in use.");
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            ModelState.AddModelError(string.Empty,
                "Selected carrier no longer exists. Please reselect.");
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Detail), new { code = entity.Code });
    }

    [HttpGet("Edit/{code}")]
    public async Task<IActionResult> Edit(string code, CancellationToken ct)
    {
        var tenantId = _tenant.RequireTenantId();
        var entity = await _repos.For(tenantId).GetByCodeAsync(code, ct);
        if (entity is null) return NotFound();

        var vm = new CustomerEditViewModel
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            CustomerType = entity.CustomerType,
            CompanyName = entity.CompanyName,
            TaxId = entity.TaxId,
            Email = entity.Email,
            Phone = entity.Phone,
            Country = entity.Country,
            CustomerTier = entity.CustomerTier,
            AnnualRevenue = entity.AnnualRevenue,
            OrdersPerMonth = entity.OrdersPerMonth,
            AvgOrderValue = entity.AvgOrderValue,
            IsKeyAccount = entity.IsKeyAccount,
            IsStrategic = entity.IsStrategic,
            AllocationPriority = entity.AllocationPriority,
            SafetyStockDays = entity.SafetyStockDays,
            PromisedFillRate = entity.PromisedFillRate,
            PreferredCarrierId = entity.PreferredCarrierId,
            DefaultPaymentTerms = entity.DefaultPaymentTerms,
            Status = entity.Status,
        };
        await PopulateLookupsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost("Edit/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, CustomerEditViewModel vm, CancellationToken ct)
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

        // Code + CustomerType pass through for the redirect / display
        // round-trip but never reach the UPDATE statement —
        // CustomerRepository.UpdateAsync omits both from SET.
        var entity = new Customer
        {
            Id = vm.Id,
            Code = code,
            CustomerType = vm.CustomerType,
            Name = vm.Name.Trim(),
            CompanyName = NullIfBlank(vm.CompanyName),
            TaxId = NullIfBlank(vm.TaxId),
            Email = NullIfBlank(vm.Email),
            Phone = NullIfBlank(vm.Phone),
            Country = NullIfBlank(vm.Country),
            CustomerTier = NullIfBlank(vm.CustomerTier),
            AnnualRevenue = vm.AnnualRevenue,
            OrdersPerMonth = vm.OrdersPerMonth,
            AvgOrderValue = vm.AvgOrderValue,
            IsKeyAccount = vm.IsKeyAccount,
            IsStrategic = vm.IsStrategic,
            AllocationPriority = vm.AllocationPriority,
            SafetyStockDays = vm.SafetyStockDays,
            PromisedFillRate = vm.PromisedFillRate,
            PreferredCarrierId = vm.PreferredCarrierId,
            DefaultPaymentTerms = NullIfBlank(vm.DefaultPaymentTerms),
            Status = vm.Status,
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
                "Selected carrier no longer exists. Please reselect.");
            await PopulateLookupsAsync(vm, ct);
            return View(vm);
        }

        return RedirectToAction(nameof(Detail), new { code });
    }

    private async Task PopulateLookupsAsync(CustomerCreateViewModel vm, CancellationToken ct) =>
        vm.Carriers = await _carrierRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);

    private async Task PopulateLookupsAsync(CustomerEditViewModel vm, CancellationToken ct) =>
        vm.Carriers = await _carrierRepos.For(_tenant.RequireTenantId()).GetActiveAsync(ct);

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
