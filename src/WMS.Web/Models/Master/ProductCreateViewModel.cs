using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Master;

// View-model for /Products/Create. DataAnnotations drive
// jQuery unobtrusive client-side validation; ProductCreateValidator
// (FluentValidation) adds server-side rules including async Code
// uniqueness pre-check.
//
// Lookup lists (Categories, BaseUoms) are populated by the controller
// from IProductCategoryRepository / IUomRepository before View(); the
// view binds them into <select> options.
//
// Static option lists (AllStatuses / AllTrackingMethods) mirror the DB
// CHECK constraints exactly — single source of truth lives in the
// migration; this is the UI-side mirror.
public sealed class ProductCreateViewModel
{
    // CHECK CK_Products_Status — must mirror migration 014.
    public static readonly IReadOnlyList<string> AllStatuses =
        new[] { "Active", "Inactive", "Discontinued", "Draft" };

    // CHECK CK_Products_TrackingMethod — must mirror migration 014.
    public static readonly IReadOnlyList<string> AllTrackingMethods =
        new[] { "None", "Lot", "LotAndSerial" };

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(50, ErrorMessage = "Code must be at most 50 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be at most 200 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Category is required.")]
    [Display(Name = "Category")]
    public Guid CategoryId { get; set; }

    [Required(ErrorMessage = "Base unit of measure is required.")]
    [Display(Name = "Base UoM")]
    public Guid BaseUomId { get; set; }

    [Required(ErrorMessage = "Tracking method is required.")]
    [Display(Name = "Tracking method")]
    public string TrackingMethod { get; set; } = "None";

    [StringLength(2, ErrorMessage = "Velocity class is at most 2 characters.")]
    [Display(Name = "Velocity class")]
    public string? VelocityClass { get; set; }

    [Display(Name = "Use catch-weight")]
    public bool UseCatchWeight { get; set; }

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Active";

    [StringLength(200, ErrorMessage = "Brand must be at most 200 characters.")]
    [Display(Name = "Brand")]
    public string? Brand { get; set; }

    // Populated by the controller before View(). Bound by the Razor
    // view into <select> options.
    public IReadOnlyList<LookupItem> Categories { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> BaseUoms { get; set; } = Array.Empty<LookupItem>();
}
