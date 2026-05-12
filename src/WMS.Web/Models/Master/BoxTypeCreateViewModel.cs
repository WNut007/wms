using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /BoxTypes/Create. Single-page form (no stepper) —
// BoxTypes has ~12 fields, all simple primitives. The Warehouse-style
// 2-section wizard would be overkill.
public sealed class BoxTypeCreateViewModel
{
    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name must be at most 50 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    // Internal dimensions — interior space for fit calculations.
    [Range(0, 99_999_999, ErrorMessage = "Length must be ≥ 0.")]
    [Display(Name = "Internal length (cm)")]
    public decimal? InternalLengthCm { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Width must be ≥ 0.")]
    [Display(Name = "Internal width (cm)")]
    public decimal? InternalWidthCm { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Height must be ≥ 0.")]
    [Display(Name = "Internal height (cm)")]
    public decimal? InternalHeightCm { get; set; }

    [Range(0, 99_999_999_999_999, ErrorMessage = "Volume must be ≥ 0.")]
    [Display(Name = "Internal volume (cm³)")]
    public decimal? InternalVolumeCubicCm { get; set; }

    // External dimensions — bounding box for carrier dim-weight pricing.
    [Range(0, 99_999_999, ErrorMessage = "Length must be ≥ 0.")]
    [Display(Name = "External length (cm)")]
    public decimal? ExternalLengthCm { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Width must be ≥ 0.")]
    [Display(Name = "External width (cm)")]
    public decimal? ExternalWidthCm { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Height must be ≥ 0.")]
    [Display(Name = "External height (cm)")]
    public decimal? ExternalHeightCm { get; set; }

    // 3-decimal precision (DECIMAL(10,3) in schema) — small parcels can
    // shift carrier billing brackets.
    [Range(0, 9_999_999, ErrorMessage = "Empty weight must be ≥ 0.")]
    [Display(Name = "Empty weight (kg)")]
    public decimal? EmptyWeightKg { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Max load must be ≥ 0.")]
    [Display(Name = "Max load (kg)")]
    public decimal? MaxLoadKg { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Unit cost must be ≥ 0.")]
    [Display(Name = "Unit cost")]
    public decimal? UnitCost { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
