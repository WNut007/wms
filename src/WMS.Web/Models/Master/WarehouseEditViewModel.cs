using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code read-only (FK orphan risk). IsActive IS
// editable from this form — toggling it off is the canonical archive
// (no separate button per the brief; TD-009 acknowledges the missing
// "maintenance" intermediate state).
public sealed class WarehouseEditViewModel
{
    public static readonly IReadOnlyList<string> AllTypes =
        WarehouseCreateViewModel.AllTypes;

    public Guid Id { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Type is required.")]
    [Display(Name = "Type")]
    public string Type { get; set; } = "Main";

    [StringLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(20)]
    [Display(Name = "Phone")]
    public string? PhoneNumber { get; set; }

    [StringLength(100)]
    [Display(Name = "Manager")]
    public string? ManagerName { get; set; }

    [StringLength(50)]
    [Display(Name = "Time zone (IANA)")]
    public string? TimeZoneId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
