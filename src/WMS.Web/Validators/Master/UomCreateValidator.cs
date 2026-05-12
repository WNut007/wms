using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validation for /Uoms/Create. Three rules:
//   1. Type ∈ CHECK set (Count/Weight/Volume/Length).
//   2. Code uniqueness (async).
//   3. "One base per Type" — async lookup against master.UnitsOfMeasure
//      to ensure no existing IsBase=1 row for this Type already exists.
public sealed class UomCreateValidator : AbstractValidator<UomCreateViewModel>
{
    private readonly IUomRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public UomCreateValidator(
        IUomRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.Type)
            .Must(v => UomCreateViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be Count, Weight, Volume, or Length.");

        RuleFor(x => x.Code)
            .MustAsync(BeUniqueCodeAsync)
            .WithMessage(x => $"Code '{x.Code}' is already in use.");

        RuleFor(x => x)
            .MustAsync(BeNoExistingBaseAsync)
            .When(x => x.IsBase)
            .WithName("IsBase")
            .WithMessage(x => $"Type '{x.Type}' already has a base unit. " +
                              "Uncheck the existing base first, or leave this as non-base.");
    }

    private async Task<bool> BeUniqueCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeAsync(code.Trim(), ct);
        return existing is null;
    }

    private async Task<bool> BeNoExistingBaseAsync(
        UomCreateViewModel vm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vm.Type)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        // exceptId=null on Create (no self to ignore).
        var existing = await repo.GetBaseByTypeAsync(vm.Type, exceptId: null, ct);
        return existing is null;
    }
}
