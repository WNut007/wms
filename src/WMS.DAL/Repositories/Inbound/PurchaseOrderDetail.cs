using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Aggregate read-shape returned by IPurchaseOrderRepository's get
// methods — header + its lines in one round-trip via Dapper's
// QueryMultiple. Lives in the DAL because both the repo (returning
// it) and the service (consuming it) reference Domain entities; the
// DTO doesn't need to cross WMS.Common's reference-free boundary.
public sealed record PurchaseOrderDetail(
    PurchaseOrder Header,
    IReadOnlyList<PurchaseOrderLine> Lines);
