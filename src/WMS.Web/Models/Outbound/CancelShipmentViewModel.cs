using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Outbound;

// Phase 14E — Cancel-shipment form payload. Same shape as Phase 14D
// CancelPackTaskViewModel + 14C CancelPickTaskViewModel.
public sealed record CancelShipmentViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, MinimumLength = 3,
        ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
