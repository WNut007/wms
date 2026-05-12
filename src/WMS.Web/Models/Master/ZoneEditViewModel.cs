using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code AND WarehouseId both read-only:
//   * Code: part of the composite UQ index (UX_Zones_Warehouse_Code).
//   * WarehouseId: changing it would orphan child Locations whose
//                  ZoneId points here.
//
// Operators who need to "move a zone to another warehouse" must
// deactivate this one + create a new zone under the target warehouse.
public sealed class ZoneEditViewModel
{
    public static readonly IReadOnlyList<string> AllTypes =
        ZoneCreateViewModel.AllTypes;

    public static readonly IReadOnlyList<string> AllTemperatures =
        ZoneCreateViewModel.AllTemperatures;

    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }

    // Surfaced for the locked-warehouse display field.
    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
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
