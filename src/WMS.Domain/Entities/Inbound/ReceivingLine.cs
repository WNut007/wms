namespace WMS.Domain.Entities.Inbound;

// Maps to inbound.ReceivingLines. LotNumber / PalletNumber are
// captured verbatim (matches what's on the delivery paperwork);
// LotId / PalletId are populated by the orchestration after the
// β GetOrCreate calls resolve them, providing a hard FK link to
// inventory rows for audit.
public sealed class ReceivingLine : BaseEntity
{
    public Guid ReceivingHeaderId { get; set; }
    public int LineNumber { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid UomId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid LocationId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string? LotNumber { get; set; }
    public string? PalletNumber { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }
}
