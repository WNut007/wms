using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inbound;

// Form payload for the mobile / single-screen putaway flow. Codes
// (Product / Owner / From-Location / To-Location, plus optional Lot /
// Pallet identifiers) are looked up server-side by the controller.
public sealed class PutawayFormModel
{
    [Required(ErrorMessage = "Product Code is required.")]
    [Display(Name = "Product Code")]
    public string ProductCode { get; set; } = "";

    [Display(Name = "Lot Number")]
    public string? LotNumber { get; set; }

    [Display(Name = "Pallet Number")]
    public string? PalletNumber { get; set; }

    [Required(ErrorMessage = "Owner Code is required.")]
    [Display(Name = "Owner Code")]
    public string OwnerCode { get; set; } = "SELF";

    [Required(ErrorMessage = "From Location is required.")]
    [Display(Name = "From")]
    public string FromLocationCode { get; set; } = "RECV-01";

    [Required(ErrorMessage = "To Location is required.")]
    [Display(Name = "To")]
    public string ToLocationCode { get; set; } = "";

    [Range(typeof(decimal), "0.0001", "9999999999.9999",
        ErrorMessage = "Quantity must be positive.")]
    public decimal Quantity { get; set; }
}
