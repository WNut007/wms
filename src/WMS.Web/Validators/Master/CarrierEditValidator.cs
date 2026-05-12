using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit validator. No Code uniqueness check (Code read-only on Edit).
// Same Status + Type enum-membership rules as Create.
public sealed class CarrierEditValidator : AbstractValidator<CarrierEditViewModel>
{
    public CarrierEditValidator()
    {
        RuleFor(x => x.Status)
            .Must(v => CarrierEditViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Inactive, Configured, Tested, or Production.");

        RuleFor(x => x.Type)
            .Must(v => string.IsNullOrEmpty(v) || CarrierEditViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be Express, Standard, Postal, Bike, or left blank.");
    }
}
