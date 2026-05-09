using FluentValidation;
using WMS.Web.Models.Inventory;

namespace WMS.Web.Validators.Inventory;

// Phase 11A — server-side cross-field rules. DataAnnotations cover the
// per-field shape; this layer adds enum membership + zero-delta + the
// "Notes required when reason='Other'" rule so an Other reason has a
// minimum explanation.
public sealed class AdjustmentCreateValidator
    : AbstractValidator<AdjustmentCreateViewModel>
{
    public AdjustmentCreateValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .Must(r => AdjustmentCreateViewModel.AllReasons.Contains(r))
            .WithMessage(vm =>
                $"Reason must be one of: {string.Join(", ", AdjustmentCreateViewModel.AllReasons)}.");

        RuleFor(x => x.QuantityDelta)
            .NotEqual(0m)
            .WithMessage("Quantity delta must be non-zero.");

        // 'Other' reason demands a written explanation.
        When(x => x.Reason == "Other", () =>
        {
            RuleFor(x => x.Notes)
                .NotEmpty()
                .WithMessage("Notes are required when reason is 'Other'.")
                .MinimumLength(3)
                .WithMessage("Notes must be at least 3 characters.");
        });

        RuleFor(x => x.WarehouseId).NotEqual(Guid.Empty)
            .WithMessage("Warehouse is required.");
        RuleFor(x => x.LocationId).NotEqual(Guid.Empty)
            .WithMessage("Location is required.");
        RuleFor(x => x.ProductId).NotEqual(Guid.Empty)
            .WithMessage("Product is required.");
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty)
            .WithMessage("Owner is required.");
        RuleFor(x => x.UomId).NotEqual(Guid.Empty)
            .WithMessage("Unit of measure is required.");
    }
}
