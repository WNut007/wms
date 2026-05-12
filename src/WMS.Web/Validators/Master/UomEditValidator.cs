using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit validator — same "one base per Type" rule as Create, plus
// exceptId=current-row so flipping IsBase on/off for the SAME row
// doesn't trip on itself.
//
// No Code uniqueness check (Code read-only on Edit).
// No Type CHECK validation (Type read-only on Edit).
public sealed class UomEditValidator : AbstractValidator<UomEditViewModel>
{
    private readonly IUomRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public UomEditValidator(
        IUomRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x)
            .MustAsync(BeNoExistingBaseAsync)
            .When(x => x.IsBase)
            .WithName("IsBase")
            .WithMessage(x => $"Type '{x.Type}' already has a base unit (different row). " +
                              "Uncheck the existing base first.");
    }

    private async Task<bool> BeNoExistingBaseAsync(
        UomEditViewModel vm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vm.Type)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetBaseByTypeAsync(vm.Type, exceptId: vm.Id, ct);
        return existing is null;
    }
}
