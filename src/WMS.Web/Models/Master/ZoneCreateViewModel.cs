using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /Zones/Create. WarehouseId is required on Create —
// zones can't exist without a parent warehouse, and zone Code uniqueness
// is scoped to (WarehouseId, Code).
//
// Single-page form with sections (Identity / Classification /
// Operations). No stepper — 7 fields fit one page.
public sealed class ZoneCreateViewModel
{
    // CK_Zones_Type — must mirror migration 006 exactly.
    public static readonly IReadOnlyList<string> AllTypes =
        new[] { "Receiving", "Storage", "Picking", "Packing",
                "Shipping", "Staging", "Quarantine", "Returns" };

    // CK_Zones_Temperature — nullable enum (NULL is valid).
    public static readonly IReadOnlyList<string> AllTemperatures =
        new[] { "Ambient", "Chilled", "Frozen" };

    [Required(ErrorMessage = "Warehouse is required.")]
    [Display(Name = "Warehouse")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name must be at most 100 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Type is required.")]
    [Display(Name = "Type")]
    public string Type { get; set; } = "Storage";

    [Display(Name = "Temperature")]
    public string? Temperature { get; set; }

    [StringLength(500)]
    [Display(Name = "Allowed product types")]
    public string? AllowedProductTypes { get; set; }

    [Display(Name = "Lot commingle allowed")]
    public bool LotCommingleAllowed { get; set; } = true;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
