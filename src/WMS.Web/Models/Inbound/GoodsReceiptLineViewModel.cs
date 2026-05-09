using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inbound;

// Phase 9B GR line. Editable inline grid row; reuses Phase 9A line-
// grid pattern (x-for + indexed Lines[N].FieldName binding). When
// pre-filled from a PO, ProductId / UomId / ExpectedQuantity /
// PurchaseOrderLineId are server-set and the grid renders them
// readonly. For blind receipts (no PO selected), all fields are
// user-entered and ExpectedQuantity stays null.
public sealed class GoodsReceiptLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Line number must be positive.")]
    public int LineNumber { get; set; }

    // Optional FK back to PO line — populated when the line was
    // pre-filled from a PO. Drives PO line ReceivedQuantity bump on
    // Post (PostReceivingAsync existing behaviour).
    public Guid? PurchaseOrderLineId { get; set; }

    [Required(ErrorMessage = "Product is required.")]
    public Guid ProductId { get; set; }

    [Required(ErrorMessage = "Unit of measure is required.")]
    public Guid UomId { get; set; }

    [Required(ErrorMessage = "Owner is required.")]
    public Guid OwnerId { get; set; }

    [Required(ErrorMessage = "Location is required.")]
    public Guid LocationId { get; set; }

    // Display-only when pre-filled from a PO. Null for blind receipts.
    public decimal? ExpectedQuantity { get; set; }

    [Range(typeof(decimal), "0.0001", "9999999999.9999",
        ErrorMessage = "Received quantity must be positive.")]
    public decimal ReceivedQuantity { get; set; }

    [StringLength(50)]
    public string? LotNumber { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [StringLength(50)]
    public string? PalletNumber { get; set; }
}
