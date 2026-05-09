using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validator. Layered on top of DataAnnotations: DA covers
// Required / StringLength (and drives client-side); FV adds enum
// membership + async Code uniqueness pre-check.
//
// The DB UQ constraint on master.Products(Code) is the authority — FV
// pre-check is best-effort UX (catch the duplicate before the round-
// trip). The controller still wraps the INSERT in a try/catch
// (SqlException 2627) so a race between two admins creating the same
// SKU still surfaces as a field-level error, not an unhandled
// exception.
public sealed class ProductCreateValidator : AbstractValidator<ProductCreateViewModel>
{
    private readonly IProductRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public ProductCreateValidator(
        IProductRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        // Enum membership — duplicated from DA's [RegularExpression]
        // alternative for clarity. DB CHECK is the ultimate authority.
        RuleFor(x => x.TrackingMethod)
            .Must(BeValidTrackingMethod)
            .WithMessage("Tracking method must be None, Lot, or LotAndSerial.");

        RuleFor(x => x.Status)
            .Must(BeValidStatus)
            .WithMessage("Status must be Active, Inactive, Discontinued, or Draft.");

        // Async uniqueness — runs only when sync rules pass (default
        // CascadeMode in FV 11.x).
        RuleFor(x => x.Code)
            .MustAsync(BeUniqueCodeAsync)
            .WithMessage(x => $"Code '{x.Code}' is already in use.");
    }

    private static bool BeValidTrackingMethod(string value) =>
        ProductCreateViewModel.AllTrackingMethods.Contains(value);

    private static bool BeValidStatus(string value) =>
        ProductCreateViewModel.AllStatuses.Contains(value);

    private async Task<bool> BeUniqueCodeAsync(
        string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeAsync(code.Trim(), ct);
        return existing is null;
    }
}
