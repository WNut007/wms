using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Counts;

// Phase 21 — model-binding shape for POST /count/save and /count/submit.
// Operator-supplied per-line values get projected to CountLineUpdate
// records by the controller. CountedQuantity nullable (operator may
// re-clear an entry → reverts to Pending). LineStatus drives whether
// the line is treated as Counted / Skipped / Pending on apply.
public sealed class MobileSaveCountViewModel
{
    public List<CountLineEntry> Lines { get; set; } = new();
}

public sealed class CountLineEntry
{
    [Required]
    public Guid LineId { get; set; }

    public decimal? CountedQuantity { get; set; }

    [Required]
    public string LineStatus { get; set; } = "Pending";

    [StringLength(500)]
    public string? Notes { get; set; }
}
