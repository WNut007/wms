namespace WMS.Domain.Entities.Counts;

// Phase 12 — per-stock-row count line, snapshotted at session-create
// time. CountedQuantity is null until the operator records a count;
// LineStatus tracks whether the line is Pending / Counted / Skipped.
//
// 6-tuple denormalised so a snapshot survives even if the underlying
// Stock row gets adjusted to zero between Counting and Applied.
//
// No Version column — operators may overwrite freely until the
// session is Counted/Applied; concurrent edits within a session are
// rare (single counter v1).
public sealed class CycleCountLine
{
    public Guid Id { get; set; }
    public Guid CycleCountId { get; set; }
    public int LineNumber { get; set; }

    public Guid StockId { get; set; }

    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }

    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }

    public string LineStatus { get; set; } = "Pending";
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Convenience read — variance only meaningful when Counted.
    public decimal? Variance =>
        CountedQuantity.HasValue ? CountedQuantity.Value - ExpectedQuantity : null;
}
