using FluentValidation;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Validators.Inbound;

// Edit-form validator. PoNumber uniqueness check skipped (PoNumber is
// readonly on Edit). Line replacement rules apply only when
// LinesLocked=false; locked POs ignore the lines collection on the
// service side (header-only update path) so we don't need to enforce
// here — but defensive: still require ≥1 line + distinct line numbers
// when the user has touched them.
public sealed class PurchaseOrderEditValidator
    : AbstractValidator<PurchaseOrderEditViewModel>
{
    public PurchaseOrderEditValidator()
    {
        // Lines validation only when not locked (Locked = read-only on
        // server side, won't be submitted as ReplaceLines anyway).
        When(x => !x.LinesLocked, () =>
        {
            RuleFor(x => x.Lines)
                .NotEmpty()
                .WithMessage("At least one line is required.");

            RuleFor(x => x.Lines).Must(HaveDistinctLineNumbers)
                .When(x => x.Lines is { Count: > 0 })
                .WithMessage("Line numbers must be distinct.");

            RuleForEach(x => x.Lines).ChildRules(line =>
            {
                line.RuleFor(l => l.ProductId)
                    .NotEqual(Guid.Empty)
                    .WithMessage("Product is required.");
                line.RuleFor(l => l.UomId)
                    .NotEqual(Guid.Empty)
                    .WithMessage("Unit of measure is required.");
                line.RuleFor(l => l.ExpectedQuantity)
                    .GreaterThan(0)
                    .WithMessage("Expected quantity must be positive.");
            });
        });
    }

    private static bool HaveDistinctLineNumbers(IReadOnlyList<PurchaseOrderLineViewModel> lines)
    {
        var seen = new HashSet<int>();
        return lines.All(l => seen.Add(l.LineNumber));
    }
}
