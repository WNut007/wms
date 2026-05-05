using WMS.DAL.Repositories.Inbound;

namespace WMS.BLL.Services.Inbound;

// Operational receiving primitive. PostReceivingAsync persists the
// receiving header + lines, drives stock + lot + pallet upserts via
// IReceivingService (β) for each line, and bumps PurchaseOrderLine
// ReceivedQuantity for any line that names a PO line.
//
// Status orchestration (PO header / line / receiving Draft→Posted) is
// out of scope for this chunk; the receiving header is created at
// 'Posted' and the PO header / line keep their existing Status until
// a future chunk owns the transition rules.
public interface IReceivingHeaderService
{
    Task<ReceivingDetail> PostReceivingAsync(
        Guid tenantId,
        PostReceivingRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    Task<ReceivingDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<ReceivingDetail?> GetByNumberAsync(
        Guid tenantId, string receivingNumber, CancellationToken ct = default);
}
