using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validation for /BoxTypes/Create. Async Code uniqueness
// check — DataAnnotations handles per-field shape; FluentValidation
// handles cross-field + DB-level rules.
public sealed class BoxTypeCreateValidator : AbstractValidator<BoxTypeCreateViewModel>
{
    private readonly IBoxTypeRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public BoxTypeCreateValidator(
        IBoxTypeRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.Code)
            .MustAsync(BeUniqueCodeAsync)
            .WithMessage(x => $"Code '{x.Code}' is already in use.");

        // Belt-and-suspenders cross-check: if any internal dim is set,
        // they should ALL be set or none. Pure shape rule — admin can
        // skip volume which is computed separately.
        RuleFor(x => x)
            .Must(HaveConsistentInternalDims)
            .WithName("InternalLengthCm")
            .WithMessage("Internal dimensions: set all three (L, W, H) or none.");
    }

    private async Task<bool> BeUniqueCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeAsync(code.Trim(), ct);
        return existing is null;
    }

    private static bool HaveConsistentInternalDims(BoxTypeCreateViewModel vm)
    {
        var set = new[] { vm.InternalLengthCm, vm.InternalWidthCm, vm.InternalHeightCm }
            .Count(x => x.HasValue);
        return set == 0 || set == 3;
    }
}
