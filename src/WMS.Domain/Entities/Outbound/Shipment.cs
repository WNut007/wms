namespace WMS.Domain.Entities.Outbound;

// Phase 14E — outbound.Shipments header.
//
// State flow:
//   Pending → Shipped | Cancelled
//
// One shipment per SO for MVP (UX_Shipments_SalesOrder UNIQUE).
// Cartons get stamped with ShipmentId on Submit.
//
// Status as string per project convention; CK_Shipments_Status
// constrains the allowed values + CK_Shipments_AuditMatchesStatus
// enforces the per-state audit trio.
public sealed class Shipment : BaseEntity
{
    public string ShipmentNumber { get; set; } = "";
    public Guid SalesOrderId { get; set; }
    public string Status { get; set; } = "Pending";

    public string? CarrierName { get; set; }      // free-text MVP; FK lookup deferred (TD)
    public string? TrackingNumber { get; set; }   // optional — deferred-default carrier pattern

    public string? Notes { get; set; }

    // Per-state audit trio. GeneratedBy/At always set on insert.
    public DateTime GeneratedAt { get; set; }
    public Guid? GeneratedBy { get; set; }

    public DateTime? ShippedAt { get; set; }
    public Guid? ShippedBy { get; set; }

    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancelReason { get; set; }
}
