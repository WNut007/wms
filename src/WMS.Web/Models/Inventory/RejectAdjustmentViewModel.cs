using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inventory;

// Phase 11A — Reject-form payload (POST /Adjustments/Reject/{id}).
// Approve takes no payload (id from route is enough); Reject needs a
// reason for audit trail.
public sealed record RejectAdjustmentViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Rejection reason is required.")]
    [StringLength(500, MinimumLength = 3,
        ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
