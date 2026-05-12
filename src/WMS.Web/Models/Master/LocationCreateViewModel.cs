using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /Locations/Create. 3-section single-page form:
//   1. Identity — Warehouse, Zone (cascade), Code, Description.
//   2. Capacity & Pick — dimensions, capacity policy, rank, thresholds.
//   3. Position & Status — 3D coords, Aisle/Bay/Level, Status, IsActive.
public sealed class LocationCreateViewModel
{
    // CK_Locations_CapacityPolicy — mirror migration 007 exactly.
    public static readonly IReadOnlyList<string> AllCapacityPolicies =
        new[] { "NoLimit", "ByVolume", "ByWeight", "ByEither", "ByBoth" };

    // CK_Locations_Status — operational lifecycle (NOT bool!).
    public static readonly IReadOnlyList<string> AllStatuses =
        new[] { "Active", "Blocked", "Maintenance" };

    [Required(ErrorMessage = "Warehouse is required.")]
    [Display(Name = "Warehouse")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Zone is required.")]
    [Display(Name = "Zone")]
    public Guid ZoneId { get; set; }

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [StringLength(200)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    // Dimensions (cm)
    [Range(0, 99_999_999)]
    [Display(Name = "Length (cm)")]
    public decimal? LengthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Width (cm)")]
    public decimal? WidthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Height (cm)")]
    public decimal? HeightCm { get; set; }

    // Capacity
    [Range(0, 999_999_999_999_999)]
    [Display(Name = "Capacity volume (cm³)")]
    public decimal? CapacityVolumeCubicCm { get; set; }

    [Range(0, 999_999_999_999_999)]
    [Display(Name = "Capacity weight (kg)")]
    public decimal? CapacityWeightKg { get; set; }

    [Required(ErrorMessage = "Capacity policy is required.")]
    [Display(Name = "Capacity policy")]
    public string CapacityPolicy { get; set; } = "NoLimit";

    [Range(0, 9999)]
    [Display(Name = "Bin rank")]
    public int BinRank { get; set; } = 100;

    [Display(Name = "Allow multiple items")]
    public bool AllowMultipleItems { get; set; } = true;

    // Pick thresholds
    [Range(0, 999_999_999)]
    [Display(Name = "Min pick qty")]
    public decimal? MinPickQty { get; set; }

    [Range(0, 999_999_999)]
    [Display(Name = "Max pick qty")]
    public decimal? MaxPickQty { get; set; }

    // 3D position (ADR-011 — viz deferred to Phase 4+)
    [Display(Name = "Position X")]
    public decimal? PositionX { get; set; }

    [Display(Name = "Position Y")]
    public decimal? PositionY { get; set; }

    [Display(Name = "Position Z")]
    public decimal? PositionZ { get; set; }

    [Display(Name = "Rotation")]
    public decimal Rotation { get; set; }

    [StringLength(10)]
    [Display(Name = "Aisle")]
    public string? Aisle { get; set; }

    [Range(0, 9999)]
    [Display(Name = "Bay")]
    public int? Bay { get; set; }

    [Range(0, 9999)]
    [Display(Name = "Level")]
    public int? Level { get; set; }

    [Display(Name = "Show in 3D")]
    public bool Show3D { get; set; } = true;

    [StringLength(20)]
    [Display(Name = "Display color")]
    public string? DisplayColor { get; set; }

    [Display(Name = "Pick face")]
    public bool IsPickface { get; set; }

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Active";

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
