using WMS.Common.Inventory;

namespace WMS.Domain.Entities.Inventory;

// Maps to inventory.StockMovements. Append-only — there is no UPDATE
// path. Doesn't inherit BaseEntity for that reason: PerformedAt is
// the only timestamp; PerformedBy is the only actor; Version /
// UpdatedAt / UpdatedBy aren't applicable to immutable rows.
//
// Sign convention: QuantityDelta > 0 means the Stock row gained
// units (Receive, Adjust+, Putaway destination); < 0 means it lost
// units (Pick, Adjust-, Putaway source). Per ADR-014.
public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid StockId { get; set; }
    public StockMovementType MovementType { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public decimal QuantityDelta { get; set; }
    public Guid UomId { get; set; }
    public Guid OwnerId { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public Guid? PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }
}

