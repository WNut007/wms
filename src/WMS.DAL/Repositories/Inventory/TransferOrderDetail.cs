using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Aggregate read-shape for TransferOrder header + lines in one
// QueryMultiple round-trip. Same pattern as PurchaseOrderDetail /
// CycleCountDetail.
public sealed record TransferOrderDetail(
    TransferOrder Header,
    IReadOnlyList<TransferOrderLine> Lines);
