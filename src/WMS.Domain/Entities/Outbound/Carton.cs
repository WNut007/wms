namespace WMS.Domain.Entities.Outbound;

// Phase 14D — outbound.Cartons. Physical packaging per pack task.
//
// MVP: 1:1 with PackTask (UX_Cartons_PackTask UNIQUE enforces). The
// eventual multi-carton path drops the UNIQUE in a future migration
// and adds a per-line per-carton split table.
//
// Created at PackTask.SubmitAsync time (kept atomic inside the same
// TX as the task header flip Pending → Packed).
//
// No Version on cartons — appended once at submit and never edited;
// if metadata needs changing the operator cancels (when pre-Submit) +
// regenerates.
public sealed class Carton
{
    public Guid Id { get; set; }
    public string CartonNumber { get; set; } = "";   // CTN-YYYYMMDD-NNNN
    public Guid PackTaskId { get; set; }

    public Guid? BoxTypeId { get; set; }              // nullable — operator may skip
    public decimal? WeightKg { get; set; }            // nullable — scale integration is a future TD

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
