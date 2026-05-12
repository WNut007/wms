using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit validator — no Code/Warehouse uniqueness check (both read-only
// on Edit). Same Type + Temperature CHECK-set validation as Create.
public sealed class ZoneEditValidator : AbstractValidator<ZoneEditViewModel>
{
    public ZoneEditValidator()
    {
        RuleFor(x => x.Type)
            .Must(v => ZoneEditViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be one of: Receiving, Storage, Picking, Packing, Shipping, Staging, Quarantine, Returns.");

        RuleFor(x => x.Temperature)
            .Must(v => string.IsNullOrEmpty(v) || ZoneEditViewModel.AllTemperatures.Contains(v))
            .WithMessage("Temperature must be Ambient, Chilled, Frozen, or left blank.");
    }
}
