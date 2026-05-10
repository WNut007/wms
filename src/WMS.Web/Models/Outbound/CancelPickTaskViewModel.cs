using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Outbound;

// Phase 14C — Cancel-pick-task form payload. Reason is required for
// audit trail; capped at 500 to match outbound.PickTasks.CancelReason
// column width. Same shape as Phase 10B CancelReceivingViewModel.
public sealed record CancelPickTaskViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, MinimumLength = 3,
        ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
