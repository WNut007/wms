using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit-side validator. Code + CustomerType are read-only on Edit
// (UpdateAsync omits them) so:
//   - No Code uniqueness pre-check.
//   - The B2B cross-field rule (CompanyName/TaxId required for B2B)
//     STILL applies — operators can clear those fields on a B2B
//     customer's Edit form, which would leave the row in an invalid
//     business state. The DB doesn't catch that; we do.
public sealed class CustomerEditValidator : AbstractValidator<CustomerEditViewModel>
{
    public CustomerEditValidator()
    {
        RuleFor(x => x.Status)
            .Must(v => CustomerEditViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Active, Inactive, Suspended, or Draft.");

        RuleFor(x => x.CustomerTier)
            .Must(v => v is null || CustomerEditViewModel.AllTiers.Contains(v))
            .WithMessage("Tier must be Tier1, Tier2, Tier3, or Tier4.");

        RuleFor(x => x.CompanyName)
            .NotEmpty()
            .When(x => x.CustomerType == "B2B")
            .WithMessage("Company name is required for B2B customers.");

        RuleFor(x => x.TaxId)
            .NotEmpty()
            .When(x => x.CustomerType == "B2B")
            .WithMessage("Tax ID is required for B2B customers.");
    }
}
