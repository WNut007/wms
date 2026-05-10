using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Outbound;

// Phase 14D — Cancel-pack-task form payload. Same shape as Phase 14C
// CancelPickTaskViewModel + Phase 10B CancelReceivingViewModel.
public sealed record CancelPackTaskViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, MinimumLength = 3,
        ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
