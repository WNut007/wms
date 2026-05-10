using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14A — header + lines aggregate returned by GetByIdAsync /
// GetByNumberAsync. Same QueryMultiple round-trip pattern as
// PurchaseOrderDetail / TransferOrderDetail.
public sealed record SalesOrderDetail(
    SalesOrder Header,
    IReadOnlyList<SalesOrderLine> Lines);
