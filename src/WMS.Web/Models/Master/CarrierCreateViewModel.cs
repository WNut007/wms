using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /Carriers/Create. Single-page form with 3 sections
// (Identity / Classification / Operational). No stepper — 10 fields
// fit one page.
public sealed class CarrierCreateViewModel
{
    // CK_Carriers_Status — mirror migration 021 exactly.
    public static readonly IReadOnlyList<string> AllStatuses =
        new[] { "Inactive", "Configured", "Tested", "Production" };

    // CK_Carriers_Type — null-or-one-of (NULL is valid).
    public static readonly IReadOnlyList<string> AllTypes =
        new[] { "Express", "Standard", "Postal", "Bike" };

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name must be at most 100 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    // Optional (NULL allowed). Empty string → null at controller.
    [StringLength(20)]
    [Display(Name = "Type")]
    public string? Type { get; set; }

    // ISO 3166-1 alpha-2. Default 'TH'.
    [StringLength(2)]
    [Display(Name = "Country")]
    public string? Country { get; set; } = "TH";

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Inactive";

    // JSON array as text. e.g. ["Same","Next","Standard","Economy"].
    // Free-form for now; Phase 31+ may ship a tag-chip widget.
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

    [Range(0, 9999, ErrorMessage = "Display order must be 0–9999.")]
    [Display(Name = "Display order")]
    public int? DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
