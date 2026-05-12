using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code read-only (FK orphan risk — Cartons may
// reference BoxTypes). IsActive editable from this form — unchecking
// is the canonical archive.
public sealed class BoxTypeEditViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Range(0, 99_999_999)]
    [Display(Name = "Internal length (cm)")]
    public decimal? InternalLengthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Internal width (cm)")]
    public decimal? InternalWidthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Internal height (cm)")]
    public decimal? InternalHeightCm { get; set; }

    [Range(0, 99_999_999_999_999)]
    [Display(Name = "Internal volume (cm³)")]
    public decimal? InternalVolumeCubicCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "External length (cm)")]
    public decimal? ExternalLengthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "External width (cm)")]
    public decimal? ExternalWidthCm { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "External height (cm)")]
    public decimal? ExternalHeightCm { get; set; }

    [Range(0, 9_999_999)]
    [Display(Name = "Empty weight (kg)")]
    public decimal? EmptyWeightKg { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Max load (kg)")]
    public decimal? MaxLoadKg { get; set; }

    [Range(0, 99_999_999)]
    [Display(Name = "Unit cost")]
    public decimal? UnitCost { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
