using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit validator — no Code/Warehouse uniqueness check (both read-only
// on Edit). Same CapacityPolicy + Status enum-membership rules as
// Create, plus the Min ≤ Max pick threshold rule.
public sealed class LocationEditValidator : AbstractValidator<LocationEditViewModel>
{
    public LocationEditValidator()
    {
        RuleFor(x => x.CapacityPolicy)
            .Must(v => LocationEditViewModel.AllCapacityPolicies.Contains(v))
            .WithMessage("Capacity policy must be NoLimit, ByVolume, ByWeight, ByEither, or ByBoth.");

        RuleFor(x => x.Status)
            .Must(v => LocationEditViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Active, Blocked, or Maintenance.");

        RuleFor(x => x.ZoneId)
            .NotEqual(Guid.Empty).WithMessage("Zone is required.");

        RuleFor(x => x)
            .Must(vm => !(vm.MinPickQty.HasValue && vm.MaxPickQty.HasValue && vm.MinPickQty > vm.MaxPickQty))
            .WithName("MinPickQty")
            .WithMessage("Min pick qty cannot exceed Max pick qty.");
    }
}
