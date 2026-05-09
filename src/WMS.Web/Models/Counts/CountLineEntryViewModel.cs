using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Counts;

// Phase 12 — per-line counted-quantity entry submitted from the
// inline counting form. CountedQuantity nullable: operator can clear
// to revert. LineStatus drives apply behaviour.
public sealed class CountLineEntryViewModel
{
    [Required]
    public Guid LineId { get; set; }

    [Range(0, 1_000_000_000d, ErrorMessage = "Counted quantity must be non-negative.")]
    public decimal? CountedQuantity { get; set; }

    [Required]
    public string LineStatus { get; set; } = "Pending";

    [StringLength(500)]
    public string? Notes { get; set; }
}

// Bulk-save payload — wraps a list of line entries.
public sealed class SaveCountsViewModel
{
    public List<CountLineEntryViewModel> Lines { get; set; } = new();
}

// Cancel-form payload (mirrors Phase 10B CancelReceivingViewModel).
public sealed record CancelCycleCountViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
