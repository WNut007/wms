using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Inventory;

// Phase 11A — Create-form payload for /Adjustments/Create. The form
// surfaces the 6-tuple as dropdowns (Warehouse → Location, Product,
// Owner, UoM); Lot/Pallet are optional strings (rare initially).
// Reason is a closed-list dropdown; Delta is a signed decimal; Notes
// free-text.
public sealed class AdjustmentCreateViewModel
{
    [Required(ErrorMessage = "Warehouse is required.")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Location is required.")]
    public Guid LocationId { get; set; }

    [Required(ErrorMessage = "Product is required.")]
    public Guid ProductId { get; set; }

    [Required(ErrorMessage = "Owner is required.")]
    public Guid OwnerId { get; set; }

    [Required(ErrorMessage = "Unit of measure is required.")]
    public Guid UomId { get; set; }

    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }

    [Required(ErrorMessage = "Quantity delta is required.")]
    [Range(-1_000_000_000d, 1_000_000_000d,
        ErrorMessage = "Delta out of range.")]
    public decimal QuantityDelta { get; set; }

    [Required(ErrorMessage = "Reason is required.")]
    public string Reason { get; set; } = "";

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
    public string? Notes { get; set; }

    // Q3 default — operator opts in to creating a new Stock row when
    // the 6-tuple doesn't match an existing one (typical "Found"
    // scenario on a clean shelf). Negative deltas with this flag still
    // fail CK_Stock_OnHand_NonNegative naturally.
    public bool AllowCreateNew { get; set; }

    // Lookup data populated by controller for re-display on validation
    // failure. List of reasons + lookups for the dropdowns.
    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Products   { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Owners     { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms       { get; set; } = Array.Empty<LookupItem>();
    // Locations are warehouse-scoped — populated lazily via AJAX in v2.
    // For v1 this is the full set for the chosen warehouse, fetched
    // client-side after warehouse pick.
    public IReadOnlyList<LookupItem> Locations  { get; set; } = Array.Empty<LookupItem>();

    public static readonly IReadOnlyList<string> AllReasons = new[]
    {
        "Damaged",
        "Expired",
        "Lost",
        "Found",
        "ReturnedToSupplier",
        "Sample",
        "Other",
    };
}
