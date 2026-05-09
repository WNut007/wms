using FluentValidation;
using WMS.Web.Models.Inventory;

namespace WMS.Web.Validators.Inventory;

// Phase 11A — same shape as Phase 10B's CancelReceivingValidator.
// Whitespace-only doesn't slip past NotEmpty.
public sealed class RejectAdjustmentValidator
    : AbstractValidator<RejectAdjustmentViewModel>
{
    public RejectAdjustmentValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty)
            .WithMessage("Adjustment id is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Rejection reason is required.")
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Rejection reason cannot be blank.")
            .MinimumLength(3).WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}
