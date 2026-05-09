namespace WMS.Domain.Entities.Inbound;

// Maps to inbound.ReceivingHeaders. PurchaseOrderId is nullable for
// blind receipts (goods that arrive without a matching PO — returns,
// transfers, supplier walk-ins).
//
// Phase 10B (TD-023) — Cancelled* trio captures WHO cancelled the
// receipt, WHEN, and WHY. Populated atomically alongside Status flip
// inside CancelReceivingAsync; remain NULL on Draft / Posted rows.
public sealed class ReceivingHeader : BaseEntity
{
    public string ReceivingNumber { get; set; } = "";
    public Guid? PurchaseOrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string Status { get; set; } = "Posted";
    public string? Notes { get; set; }

    public Guid? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
}
