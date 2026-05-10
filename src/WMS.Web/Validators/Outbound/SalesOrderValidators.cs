using FluentValidation;
using WMS.Web.Models.Outbound;

namespace WMS.Web.Validators.Outbound;

// Phase 14A — Sales Order Create-form validation. Header + per-line
// rules. Mirrors the TransferOrderCreateValidator shape.
public sealed class SalesOrderCreateValidator
    : AbstractValidator<SalesOrderCreateViewModel>
{
    public SalesOrderCreateValidator()
    {
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty)
            .WithMessage("Customer is required.");
        RuleFor(x => x.WarehouseId).NotEqual(Guid.Empty)
            .WithMessage("Warehouse is required.");

        RuleFor(x => x.OrderDate).NotEqual(default(DateTime))
            .WithMessage("Order date is required.");

        // RequestedShipDate is optional, but if supplied it shouldn't
        // be wildly in the past. Accept anything from OrderDate forward;
        // backdated ship dates are rare but legal (operator records a
        // shipment that already happened).
        RuleFor(x => x.RequestedShipDate)
            .GreaterThanOrEqualTo(x => x.OrderDate.AddDays(-30))
            .When(x => x.RequestedShipDate.HasValue)
            .WithMessage("Requested ship date too far before order date.");

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
            line.RuleFor(l => l.OrderedQuantity).GreaterThan(0m)
                .WithMessage("Ordered quantity must be positive.");
            line.RuleFor(l => l.UnitPrice)
                .GreaterThanOrEqualTo(0m)
                .When(l => l.UnitPrice.HasValue)
                .WithMessage("Unit price cannot be negative.");
        });
    }
}

// Phase 14A — Edit form validation. Same per-line shape as Create
// but skips line validation when ReplaceLines=false (controller may
// post empty Lines collection in that path).
public sealed class SalesOrderEditValidator
    : AbstractValidator<SalesOrderEditViewModel>
{
    public SalesOrderEditValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);

        RuleFor(x => x.OrderDate).NotEqual(default(DateTime))
            .WithMessage("Order date is required.");

        RuleFor(x => x.RequestedShipDate)
            .GreaterThanOrEqualTo(x => x.OrderDate.AddDays(-30))
            .When(x => x.RequestedShipDate.HasValue)
            .WithMessage("Requested ship date too far before order date.");

        RuleFor(x => x.Lines)
            .NotEmpty()
            .When(x => x.ReplaceLines)
            .WithMessage("At least one line is required when replacing lines.");

        // Per-line rules only when ReplaceLines=true (header-only edit
        // ignores Lines altogether).
        When(x => x.ReplaceLines, () =>
        {
            RuleForEach(x => x.Lines).ChildRules(line =>
            {
                line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty)
                    .WithMessage("Product is required.");
                line.RuleFor(l => l.OwnerId).NotEqual(Guid.Empty)
                    .WithMessage("Owner is required.");
                line.RuleFor(l => l.UomId).NotEqual(Guid.Empty)
                    .WithMessage("Unit of measure is required.");
                line.RuleFor(l => l.OrderedQuantity).GreaterThan(0m)
                    .WithMessage("Ordered quantity must be positive.");
                line.RuleFor(l => l.UnitPrice)
                    .GreaterThanOrEqualTo(0m)
                    .When(l => l.UnitPrice.HasValue)
                    .WithMessage("Unit price cannot be negative.");
            });
        });
    }
}
