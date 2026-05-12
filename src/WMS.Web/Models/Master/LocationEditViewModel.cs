using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code AND WarehouseId both read-only:
//   * Code: part of composite UQ (WarehouseId, Code).
//   * Warehouse: child of warehouse — moving to another would orphan
//     stock at this location.
//
// ZoneId IS editable but the form picker constrains to current-warehouse
// zones only (so cross-warehouse moves remain impossible).
public sealed class LocationEditViewModel
{
    public static readonly IReadOnlyList<string> AllCapacityPolicies =
        LocationCreateViewModel.AllCapacityPolicies;

    public static readonly IReadOnlyList<string> AllStatuses =
        LocationCreateViewModel.AllStatuses;

    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }

    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";

    [Required(ErrorMessage = "Zone is required.")]
    [Display(Name = "Zone")]
    public Guid ZoneId { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [StringLength(200)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Length (cm)")]
    public decimal? LengthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Width (cm)")]
    public decimal? WidthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Height (cm)")]
    public decimal? HeightCm { get; set; }

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

    [Range(0, 999_999_999)]
    [Display(Name = "Min pick qty")]
    public decimal? MinPickQty { get; set; }

    [Range(0, 999_999_999)]
    [Display(Name = "Max pick qty")]
    public decimal? MaxPickQty { get; set; }

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
