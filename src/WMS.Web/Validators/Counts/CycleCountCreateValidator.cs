using FluentValidation;
using WMS.Web.Models.Counts;

namespace WMS.Web.Validators.Counts;

public sealed class CycleCountCreateValidator
    : AbstractValidator<CycleCountCreateViewModel>
{
    public CycleCountCreateValidator()
    {
        RuleFor(x => x.WarehouseId)
            .NotEqual(Guid.Empty)
            .WithMessage("Warehouse is required.");
    }
}

public sealed class CancelCycleCountValidator
    : AbstractValidator<CancelCycleCountViewModel>
{
    public CancelCycleCountValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Cancellation reason is required.")
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Cancellation reason cannot be blank.")
            .MinimumLength(3).WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}
