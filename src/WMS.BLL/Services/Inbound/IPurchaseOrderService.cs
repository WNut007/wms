using WMS.DAL.Repositories.Inbound;

namespace WMS.BLL.Services.Inbound;

// Inbound order-side primitives. CreateAsync persists a fresh PO
// header + lines transactionally and returns the resulting Detail
// (header + lines re-fetched, so callers see DB-defaulted CreatedAt
// / Status / Version). Get methods return null when not found.
//
// Status mutations (Open → Receiving → Closed / Cancelled) and the
// linkage to receipts (PoLine.ReceivedQuantity bumped from the
// receive flow) come in later chunks.
public interface IPurchaseOrderService
{
    Task<PurchaseOrderDetail> CreateAsync(
        Guid tenantId,
        CreatePurchaseOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    Task<PurchaseOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<PurchaseOrderDetail?> GetByNumberAsync(
        Guid tenantId, string poNumber, CancellationToken ct = default);
}
