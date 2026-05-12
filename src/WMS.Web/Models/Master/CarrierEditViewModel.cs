using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code read-only. EVERYTHING else editable — including
// Status (per Phase 30A.3 Mod #2, the operator's first acceptance test
// of this CRUD is flipping the 4 seeded carriers Inactive → Production).
public sealed class CarrierEditViewModel
{
    public static readonly IReadOnlyList<string> AllStatuses =
        CarrierCreateViewModel.AllStatuses;

    public static readonly IReadOnlyList<string> AllTypes =
        CarrierCreateViewModel.AllTypes;

    public Guid Id { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [StringLength(20)]
    [Display(Name = "Type")]
    public string? Type { get; set; }

    [StringLength(2)]
    [Display(Name = "Country")]
    public string? Country { get; set; }

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Inactive";

    [Display(Name = "Supported service levels (JSON)")]
    public string? SupportedServiceLevels { get; set; }

    [StringLength(500)]
    [Display(Name = "Website URL")]
    [Url(ErrorMessage = "Website URL must be a valid URL.")]
    public string? WebsiteUrl { get; set; }

    [StringLength(500)]
    [Display(Name = "Logo URL")]
    [Url(ErrorMessage = "Logo URL must be a valid URL.")]
    public string? LogoUrl { get; set; }

    [Range(0, 9999)]
    [Display(Name = "Display order")]
    public int? DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
