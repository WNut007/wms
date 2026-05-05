using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inbound;

// Inbound-side primitive. Today: "deposit N units at a location" —
// no lot, no pallet, no PO line. Future chunks layer those on top
// without changing this entry point.
public interface IReceivingService
{
    Task<Stock> ReceiveStockAsync(
        Guid tenantId,
        ReceiveStockRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);
}
