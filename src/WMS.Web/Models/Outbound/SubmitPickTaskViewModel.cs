using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Outbound;

// Phase 14C — model-binding shape for POST /PickTasks/Submit/{id}.
// One PickedLineRow per PickTaskLine. The service does the real cross-
// line validation (every task line in submission, no extras, dups,
// reason required for shorts) — DataAnnotations here cover the
// per-field shape so jQuery unobtrusive can give immediate feedback.
public sealed class SubmitPickTaskViewModel
{
    [Required]
    public Guid Id { get; set; }

    public List<PickedLineRow> Lines { get; set; } = new();
}

public sealed class PickedLineRow
{
    [Required]
    public Guid LineId { get; set; }

    // Nullable: required when LineStatus='Picked', forbidden when
    // 'Skipped'. Service-side enforced — DataAnnotations stay light
    // because the rule is cross-field.
    public decimal? PickedQuantity { get; set; }

    [Required]
    public string LineStatus { get; set; } = "Picked";

    [StringLength(500)]
    public string? ShortPickReason { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
