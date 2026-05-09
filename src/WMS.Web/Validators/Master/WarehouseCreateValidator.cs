using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

public sealed class WarehouseCreateValidator : AbstractValidator<WarehouseCreateViewModel>
{
    private readonly IWarehouseRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public WarehouseCreateValidator(
        IWarehouseRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.Type)
            .Must(v => WarehouseCreateViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be Main, Satellite, or Branch.");

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
