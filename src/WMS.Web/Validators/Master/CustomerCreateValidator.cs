using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validator for /Customers/Create. Two notable rules:
//
//   1. B2B requires CompanyName + TaxId (cross-field). The DB CHECK
//      doesn't enforce this — Phase 7 brief explicitly defers it to
//      BLL/FV per the migration 018 comment "BLL validates at write
//      time (avoids complex multi-column CHECK that fights bulk
//      inserts)".
//
//   2. Async Code uniqueness pre-check — same pattern as
//      ProductCreateValidator; DB UQ is the ultimate authority.
public sealed class CustomerCreateValidator : AbstractValidator<CustomerCreateViewModel>
{
    private readonly ICustomerRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public CustomerCreateValidator(
        ICustomerRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.CustomerType)
            .Must(v => CustomerCreateViewModel.AllCustomerTypes.Contains(v))
            .WithMessage("Customer type must be B2B or B2C.");

        RuleFor(x => x.Status)
            .Must(v => CustomerCreateViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Active, Inactive, Suspended, or Draft.");

        // Tier is nullable in the schema (CK allows NULL); only enforce
        // membership when set.
        RuleFor(x => x.CustomerTier)
            .Must(v => v is null || CustomerCreateViewModel.AllTiers.Contains(v))
            .WithMessage("Tier must be Tier1, Tier2, Tier3, or Tier4.");

        // B2B cross-field rule. CompanyName + TaxId are nullable at the
        // DB level (B2C customers don't carry them); for B2B, the BLL
        // is the enforcement point.
        RuleFor(x => x.CompanyName)
            .NotEmpty()
            .When(x => x.CustomerType == "B2B")
            .WithMessage("Company name is required for B2B customers.");

        RuleFor(x => x.TaxId)
            .NotEmpty()
            .When(x => x.CustomerType == "B2B")
            .WithMessage("Tax ID is required for B2B customers.");

        RuleFor(x => x.Code)
            .MustAsync(BeUniqueCodeAsync)
            .WithMessage(x => $"Code '{x.Code}' is already in use.");
    }

    private async Task<bool> BeUniqueCodeAsync(
        string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeAsync(code.Trim(), ct);
        return existing is null;
    }
}
