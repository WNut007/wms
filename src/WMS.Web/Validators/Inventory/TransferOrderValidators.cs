using FluentValidation;
using WMS.Web.Models.Inventory;

namespace WMS.Web.Validators.Inventory;

// Phase 13 — Transfer Create form validation. Header + per-line rules.
public sealed class TransferOrderCreateValidator
    : AbstractValidator<TransferOrderCreateViewModel>
{
    public TransferOrderCreateValidator()
    {
        RuleFor(x => x.FromWarehouseId).NotEqual(Guid.Empty)
            .WithMessage("Source warehouse is required.");
        RuleFor(x => x.ToWarehouseId).NotEqual(Guid.Empty)
            .WithMessage("Destination warehouse is required.");
        RuleFor(x => x.ToWarehouseId)
            .NotEqual(x => x.FromWarehouseId)
            .When(x => x.FromWarehouseId != Guid.Empty)
            .WithMessage("Destination warehouse must differ from source.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .Must(r => TransferOrderCreateViewModel.AllReasons.Contains(r))
            .WithMessage("Reason must be one of the allowed values.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("At least one line is required.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty)
                .WithMessage("Product is required.");
            line.RuleFor(l => l.OwnerId).NotEqual(Guid.Empty)
                .WithMessage("Owner is required.");
            line.RuleFor(l => l.UomId).NotEqual(Guid.Empty)
                .WithMessage("Unit of measure is required.");
            line.RuleFor(l => l.FromLocationId).NotEqual(Guid.Empty)
                .WithMessage("Source location is required.");
            line.RuleFor(l => l.ToLocationId).NotEqual(Guid.Empty)
                .WithMessage("Destination location is required.");
            line.RuleFor(l => l.QtyRequested).GreaterThan(0m)
                .WithMessage("Requested qty must be positive.");
        });
    }
}

public sealed class CancelTransferValidator
    : AbstractValidator<CancelTransferViewModel>
{
    public CancelTransferValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Cancellation reason is required.")
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Cancellation reason cannot be blank.")
            .MinimumLength(3)
            .MaximumLength(500);
    }
}

public sealed class MarkLostTransferValidator
    : AbstractValidator<MarkLostTransferViewModel>
{
    public MarkLostTransferValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Loss reason is required.")
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Loss reason cannot be blank.")
            .MinimumLength(3)
            .MaximumLength(500);
    }
}
