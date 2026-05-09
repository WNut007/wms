using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Counts;

// Phase 12 — Create-form payload. Operator picks Warehouse + optional
// Location filter; service snapshots positive-OnHand stock at scope.
public sealed class CycleCountCreateViewModel
{
    [Required(ErrorMessage = "Warehouse is required.")]
    public Guid WarehouseId { get; set; }

    // Optional — null = whole-warehouse scope.
    public Guid? LocationFilter { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
    public string? Notes { get; set; }

    // Lookup data populated by controller for re-display on validation
    // failure.
    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Locations  { get; set; } = Array.Empty<LookupItem>();
}
