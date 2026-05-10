using FluentValidation;
using WMS.Web.Models.Outbound;

namespace WMS.Web.Validators.Outbound;

// Phase 14D — same shape as Phase 14C CancelPickTaskValidator + Phase
// 12 CancelCycleCountValidator. Reason is required, 3–500 chars,
// non-blank.
public sealed class CancelPackTaskValidator
    : AbstractValidator<CancelPackTaskViewModel>
{
    public CancelPackTaskValidator()
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
