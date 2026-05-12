using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validation for /Locations/Create. Composite-key uniqueness:
// Location.Code is unique within a warehouse, not tenant-wide.
public sealed class LocationCreateValidator : AbstractValidator<LocationCreateViewModel>
{
    private readonly ILocationRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public LocationCreateValidator(
        ILocationRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.CapacityPolicy)
            .Must(v => LocationCreateViewModel.AllCapacityPolicies.Contains(v))
            .WithMessage("Capacity policy must be NoLimit, ByVolume, ByWeight, ByEither, or ByBoth.");

        RuleFor(x => x.Status)
            .Must(v => LocationCreateViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Active, Blocked, or Maintenance.");

        RuleFor(x => x.WarehouseId)
            .NotEqual(Guid.Empty).WithMessage("Warehouse is required.");

        RuleFor(x => x.ZoneId)
            .NotEqual(Guid.Empty).WithMessage("Zone is required.");

        // Pick thresholds: Min ≤ Max when both set.
        RuleFor(x => x)
            .Must(vm => !(vm.MinPickQty.HasValue && vm.MaxPickQty.HasValue && vm.MinPickQty > vm.MaxPickQty))
            .WithName("MinPickQty")
            .WithMessage("Min pick qty cannot exceed Max pick qty.");

        RuleFor(x => x)
            .MustAsync(BeUniqueCodeInWarehouseAsync)
            .When(x => x.WarehouseId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Code))
            .WithName("Code")
            .WithMessage(x => $"Code '{x.Code}' already exists in this warehouse.");
    }

    private async Task<bool> BeUniqueCodeInWarehouseAsync(
        LocationCreateViewModel vm, CancellationToken ct)
    {
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeInWarehouseAsync(vm.WarehouseId, vm.Code.Trim(), ct);
        return existing is null;
    }
}
