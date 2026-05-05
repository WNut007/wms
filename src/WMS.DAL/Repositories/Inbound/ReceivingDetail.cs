using WMS.Domain.Entities.Inbound;

namespace WMS.DAL.Repositories.Inbound;

// Aggregate read-shape returned by IReceivingHeaderRepository's get
// methods. Same DAL-level placement as PurchaseOrderDetail.
public sealed record ReceivingDetail(
    ReceivingHeader Header,
    IReadOnlyList<ReceivingLine> Lines);
