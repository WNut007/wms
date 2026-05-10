namespace WMS.Domain.Entities.Outbound;

// Phase 14D — outbound.PackTaskLines.
//
// One per SO line that had PickedQuantity > 0 at task generation
// (Skipped pick lines + zero-pick lines do NOT spawn pack lines —
// nothing to pack).
//
// Quantity progression:
//   PickedQuantity = SO line.PickedQuantity at gen (snapshot, immutable)
//   PackedQuantity = NULL until submit; then 0..PickedQuantity
//
// Per-line status:
//   Pending  — created, awaiting pack
//   Packed   — submitted with PackedQuantity populated (any value 0..Picked)
//   Skipped  — operator marked unpackable (e.g., damaged in transit
//              between pick + pack stations)
//
// CK_PackTaskLines_StatusMatchesQty enforces the invariant.
// ShortPackReason required at service layer when PackedQty < PickedQty
// and LineStatus = 'Packed', or when LineStatus = 'Skipped'.
public sealed class PackTaskLine
{
    public Guid Id { get; set; }
    public Guid PackTaskId { get; set; }
    public int LineNumber { get; set; }

    public Guid SalesOrderLineId { get; set; }

    // Snapshot fields (denormalized at task generation for stable
    // display + reporting). Pack doesn't track Lot/Pallet/Location —
    // stock has already left at this point; what's in the carton is
    // what matters.
    public Guid ProductId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }

    public decimal PickedQuantity { get; set; }
    public decimal? PackedQuantity { get; set; }
    public string LineStatus { get; set; } = "Pending";
    public string? ShortPackReason { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
