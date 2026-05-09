using FluentValidation;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.Web.Models.Inbound;

namespace WMS.Web.Validators.Inbound;

// Phase 9B server-side validator for the desktop Goods Receipt form.
// Layered with DataAnnotations on the view-model (DA covers required
// + format on header fields + per-line ranges); FV adds:
//   - async ReceivingNumber uniqueness against existing receipts
//     (PoNumber-pattern from Phase 9A reused)
//   - ≥1 line + distinct line numbers
//   - per-line ProductId / UomId / OwnerId / LocationId / Qty checks
//
// Note: Draft saves go through the SAME validator. The IsDraft flag
// in the request shape changes the orchestration path in the service,
// not the validation rules — a draft must still be syntactically valid
// (you can't save a draft with zero lines or a duplicate ReceivingNumber).
public sealed class GoodsReceiptCreateValidator
    : AbstractValidator<GoodsReceiptCreateViewModel>
{
    private readonly IReceivingHeaderRepositoryFactory _factory;
    private readonly ITenantContext _tenant;

    public GoodsReceiptCreateValidator(
        IReceivingHeaderRepositoryFactory factory,
        ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;

        RuleFor(x => x.ReceivingNumber)
            .MustAsync(BeUniqueReceivingNumberAsync)
            .WithMessage(x => $"Receiving number '{x.ReceivingNumber}' is already in use.");

        RuleFor(x => x.Lines)
            .NotEmpty()
            .WithMessage("At least one line is required.");

        RuleFor(x => x.Lines).Must(HaveDistinctLineNumbers)
            .When(x => x.Lines is { Count: > 0 })
            .WithMessage("Line numbers must be distinct.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId)
                .NotEqual(Guid.Empty).WithMessage("Product is required.");
            line.RuleFor(l => l.UomId)
                .NotEqual(Guid.Empty).WithMessage("Unit of measure is required.");
            line.RuleFor(l => l.OwnerId)
                .NotEqual(Guid.Empty).WithMessage("Owner is required.");
            line.RuleFor(l => l.LocationId)
                .NotEqual(Guid.Empty).WithMessage("Location is required.");
            line.RuleFor(l => l.ReceivedQuantity)
                .GreaterThan(0).WithMessage("Received quantity must be positive.");
        });
    }

    private async Task<bool> BeUniqueReceivingNumberAsync(string receivingNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(receivingNumber)) return true;
        var repo = _factory.For(_tenant.RequireTenantId());
        var existing = await repo.GetByNumberAsync(receivingNumber.Trim(), ct);
        return existing is null;
    }

    private static bool HaveDistinctLineNumbers(IReadOnlyList<GoodsReceiptLineViewModel> lines)
    {
        var seen = new HashSet<int>();
        return lines.All(l => seen.Add(l.LineNumber));
    }
}
