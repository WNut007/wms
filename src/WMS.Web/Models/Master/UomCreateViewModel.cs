using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /Uoms/Create. Single-page form — only 5 fields, no
// stepper needed.
public sealed class UomCreateViewModel
{
    // CHECK CK_UnitsOfMeasure_Type — mirror migration 013 exactly.
    // Drives conversion validity (Weight ↔ Volume cannot convert) and
    // picker grouping in upstream UI.
    public static readonly IReadOnlyList<string> AllTypes =
        new[] { "Count", "Weight", "Volume", "Length" };

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name must be at most 50 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Type is required.")]
    [Display(Name = "Type")]
    public string Type { get; set; } = "Count";

    // Default false — most UoMs are derivatives. Validator enforces
    // "one base per Type" if user ticks this on.
    [Display(Name = "Base unit for this type")]
    public bool IsBase { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
