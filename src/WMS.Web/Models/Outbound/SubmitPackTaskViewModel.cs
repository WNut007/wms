using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Outbound;

// Phase 14D — model-binding shape for POST /PackTasks/Submit/{id}.
// One PackedLineRow per PackTaskLine + the single Carton's metadata
// (1:1 with task for MVP). Cross-line + cross-field validation lives
// server-side in PackTaskService.SubmitAsync's ValidateRequestShape;
// DataAnnotations here cover per-field shape only.
public sealed class SubmitPackTaskViewModel
{
    [Required]
    public Guid Id { get; set; }

    public List<PackedLineRow> Lines { get; set; } = new();

    // Carton metadata — operator can leave both null (BoxType not
    // chosen + scale integration deferred → no weight). NULL flows
    // through to the DB as-is.
    public Guid? BoxTypeId { get; set; }

    [Range(0d, 10000d, ErrorMessage = "Weight must be non-negative.")]
    public decimal? WeightKg { get; set; }

    [StringLength(500)]
    public string? CartonNotes { get; set; }
}

public sealed class PackedLineRow
{
    [Required]
    public Guid LineId { get; set; }

    public decimal? PackedQuantity { get; set; }

    [Required]
    public string LineStatus { get; set; } = "Packed";

    [StringLength(500)]
    public string? ShortPackReason { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
