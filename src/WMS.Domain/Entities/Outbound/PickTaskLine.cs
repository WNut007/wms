namespace WMS.Domain.Entities.Outbound;

// Phase 14C — outbound.PickTaskLines.
//
// One per OrderAllocation that was Active at task generation. Stock
// 6-tuple snapshotted at generation for stable display + reporting
// even if the underlying inventory.Stock row mutates.
//
// Quantity progression:
//   ExpectedQuantity = OrderAllocation.AllocatedQuantity at generation
//   PickedQuantity   = NULL until submit; then 0..Expected
//
// Per-line status:
//   Pending  — created, awaiting pick
//   Picked   — submitted with PickedQuantity populated (any value 0..Expected)
//   Skipped  — operator marked unpickable (full short)
//
// CK_PickTaskLines_StatusMatchesQty enforces the invariant.
// ShortPickReason required at service layer when PickedQty < Expected
// and LineStatus = 'Picked'.
public sealed class PickTaskLine
{
    public Guid Id { get; set; }
    public Guid PickTaskId { get; set; }
    public int LineNumber { get; set; }

    public Guid OrderAllocationId { get; set; }

    // Snapshot 6-tuple from the OrderAllocation's Stock row at gen.
    public Guid StockId { get; set; }
    public Guid ProductId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }

    public decimal ExpectedQuantity { get; set; }
    public decimal? PickedQuantity { get; set; }
    public string LineStatus { get; set; } = "Pending";
    public string? ShortPickReason { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
