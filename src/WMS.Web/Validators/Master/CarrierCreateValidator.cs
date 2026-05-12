using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validation for /Carriers/Create.
public sealed class CarrierCreateValidator : AbstractValidator<CarrierCreateViewModel>
{
    private readonly ICarrierRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public CarrierCreateValidator(
        ICarrierRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.Status)
            .Must(v => CarrierCreateViewModel.AllStatuses.Contains(v))
            .WithMessage("Status must be Inactive, Configured, Tested, or Production.");

        RuleFor(x => x.Type)
            .Must(v => string.IsNullOrEmpty(v) || CarrierCreateViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be Express, Standard, Postal, Bike, or left blank.");

        RuleFor(x => x.Code)
            .MustAsync(BeUniqueCodeAsync)
            .WithMessage(x => $"Code '{x.Code}' is already in use.");
    }

    private async Task<bool> BeUniqueCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeAsync(code.Trim(), ct);
        return existing is null;
    }
}
