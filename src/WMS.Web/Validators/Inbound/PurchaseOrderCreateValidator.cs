using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Validators.Inbound;

// Server-side validator for /PurchaseOrders/Create. Layered on top of
// DataAnnotations: DA covers Required / StringLength / Range; FV adds
// async PoNumber uniqueness + cross-line validation (≥1 line, distinct
// line numbers, per-line ProductId/UomId/Quantity required).
public sealed class PurchaseOrderCreateValidator
    : AbstractValidator<PurchaseOrderCreateViewModel>
{
    private readonly IPurchaseOrderRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public PurchaseOrderCreateValidator(
        IPurchaseOrderRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.PoNumber)
            .MustAsync(BeUniquePoNumberAsync)
            .WithMessage(x => $"PO number '{x.PoNumber}' is already in use.");

        // ≥1 line is the minimum viable PO.
        RuleFor(x => x.Lines)
            .NotEmpty()
            .WithMessage("At least one line is required.");

        // Per-line shape — DataAnnotations enforce most; FV adds
        // distinct-LineNumber check across the collection.
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
    }

    private async Task<bool> BeUniquePoNumberAsync(string poNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(poNumber)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByNumberAsync(poNumber.Trim(), ct);
        return existing is null;
    }

    private static bool HaveDistinctLineNumbers(IReadOnlyList<PurchaseOrderLineViewModel> lines)
    {
        var seen = new HashSet<int>();
        return lines.All(l => seen.Add(l.LineNumber));
    }
}
