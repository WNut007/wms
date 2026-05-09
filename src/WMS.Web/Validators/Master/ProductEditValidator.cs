using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit-side validator. No Code uniqueness pre-check (Code is read-only
// on Edit per the brief). Enum membership stays as belt-and-suspenders
// against any forged form post.
public sealed class ProductEditValidator : AbstractValidator<ProductEditViewModel>
{
    public ProductEditValidator()
    {
        RuleFor(x => x.TrackingMethod)
            .Must(v => ProductEditViewModel.AllTrackingMethods.Contains(v))
            .WithMessage("Tracking method must be None, Lot, or LotAndSerial.");

        RuleFor(x => x.Status)
            .Must(v => ProductEditViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Active, Inactive, Discontinued, or Draft.");
    }
}
