using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /Warehouses/Create. 2-step Alpine stepper:
// Identity → Operations. No lookup repos needed (no FKs out of
// master.Warehouses to dropdown sources).
public sealed class WarehouseCreateViewModel
{
    // CHECK CK_Warehouses_Type — must mirror migration 002.
    public static readonly IReadOnlyList<string> AllTypes =
        new[] { "Main", "Satellite", "Branch" };

    // --- Step 1 Identity ----------------------------------------------

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be at most 200 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Type is required.")]
    [Display(Name = "Type")]
    public string Type { get; set; } = "Main";

    [StringLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    // --- Step 2 Operations --------------------------------------------

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
