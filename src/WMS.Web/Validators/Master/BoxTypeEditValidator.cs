using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit validator — no uniqueness check (Code is read-only on Edit).
// Same internal-dimension consistency rule as Create.
public sealed class BoxTypeEditValidator : AbstractValidator<BoxTypeEditViewModel>
{
    public BoxTypeEditValidator()
    {
        RuleFor(x => x)
            .Must(HaveConsistentInternalDims)
            .WithName("InternalLengthCm")
            .WithMessage("Internal dimensions: set all three (L, W, H) or none.");
    }

    private static bool HaveConsistentInternalDims(BoxTypeEditViewModel vm)
    {
        var set = new[] { vm.InternalLengthCm, vm.InternalWidthCm, vm.InternalHeightCm }
            .Count(x => x.HasValue);
        return set == 0 || set == 3;
    }
}
