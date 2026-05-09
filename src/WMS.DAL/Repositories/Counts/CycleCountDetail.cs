using WMS.Domain.Entities.Counts;

namespace WMS.DAL.Repositories.Counts;

// Aggregate read shape for a single cycle count session — header +
// lines in one round-trip via Dapper QueryMultiple. Same pattern as
// PurchaseOrderDetail / ReceivingDetail.
public sealed record CycleCountDetail(
    CycleCount Header,
    IReadOnlyList<CycleCountLine> Lines);
