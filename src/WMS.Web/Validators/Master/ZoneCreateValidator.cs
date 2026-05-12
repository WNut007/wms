using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Server-side validation for /Zones/Create. Composite-key uniqueness:
// Zone.Code is unique only within a warehouse, not tenant-wide. The
// uniqueness check passes (WarehouseId, Code) to the repo.
public sealed class ZoneCreateValidator : AbstractValidator<ZoneCreateViewModel>
{
    private readonly IZoneRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public ZoneCreateValidator(
        IZoneRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.Type)
            .Must(v => ZoneCreateViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be one of: Receiving, Storage, Picking, Packing, Shipping, Staging, Quarantine, Returns.");

        RuleFor(x => x.Temperature)
            .Must(v => string.IsNullOrEmpty(v) || ZoneCreateViewModel.AllTemperatures.Contains(v))
            .WithMessage("Temperature must be Ambient, Chilled, Frozen, or left blank.");

        RuleFor(x => x.WarehouseId)
            .NotEqual(Guid.Empty).WithMessage("Warehouse is required.");

        RuleFor(x => x)
            .MustAsync(BeUniqueCodeInWarehouseAsync)
            .When(x => x.WarehouseId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Code))
            .WithName("Code")
            .WithMessage(x => $"Code '{x.Code}' already exists in this warehouse.");
    }

    private async Task<bool> BeUniqueCodeInWarehouseAsync(
        ZoneCreateViewModel vm, CancellationToken ct)
    {
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByCodeInWarehouseAsync(vm.WarehouseId, vm.Code.Trim(), ct);
        return existing is null;
    }
}
