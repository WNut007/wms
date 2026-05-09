using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Master;

// View-model for /Products/Edit/{code}. Mirrors ProductCreateViewModel
// minus the Code uniqueness check + plus an Id (hidden) and a
// display-only Code (rendered as readonly input — never updated by
// ProductRepository.UpdateAsync per the brief).
public sealed class ProductEditViewModel
{
    public static readonly IReadOnlyList<string> AllStatuses =
        ProductCreateViewModel.AllStatuses;
    public static readonly IReadOnlyList<string> AllTrackingMethods =
        ProductCreateViewModel.AllTrackingMethods;

    public Guid Id { get; set; }

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

    public IReadOnlyList<LookupItem> Categories { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> BaseUoms { get; set; } = Array.Empty<LookupItem>();
}
