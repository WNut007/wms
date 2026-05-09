using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inbound;

// Phase 10B (TD-023) — Cancel-receipt form payload. Reason is required
// for audit trail per design decision Q1 ("audit trail value"). Length
// capped at 500 to match the column width on inbound.ReceivingHeaders
// .CancelReason; long enough for an operator to write something useful
// ("supplier sent wrong SKU; returning shipment").
public sealed record CancelReceivingViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, MinimumLength = 3,
        ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
