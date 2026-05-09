using FluentValidation;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Validators.Inbound;

// Phase 10B (TD-023) — server-side validator. DataAnnotations on the
// VM cover client-side jQuery unobtrusive; this layer is the source
// of truth. Reason trimming + length is enforced both ways so an
// operator pasting whitespace doesn't slip past as "non-empty".
public sealed class CancelReceivingValidator
    : AbstractValidator<CancelReceivingViewModel>
{
    public CancelReceivingValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty)
            .WithMessage("Receipt id is required.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Cancellation reason is required.")
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Cancellation reason cannot be blank.")
            .MinimumLength(3)
            .WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(500)
            .WithMessage("Reason cannot exceed 500 characters.");
    }
}
