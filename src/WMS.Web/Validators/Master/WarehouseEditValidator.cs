using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

public sealed class WarehouseEditValidator : AbstractValidator<WarehouseEditViewModel>
{
    public WarehouseEditValidator()
    {
        RuleFor(x => x.Type)
            .Must(v => WarehouseEditViewModel.AllTypes.Contains(v))
            .WithMessage("Type must be Main, Satellite, or Branch.");
    }
}
