using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inbound;

// Form payload for the mobile / single-screen receive flow. Codes
// (Product / Owner / Location / PO) are looked up server-side by the
// controller — operators key the natural identifiers they read off
// the paperwork, not Guids.
public sealed class ReceiveFormModel
{
    [Required(ErrorMessage = "Receiving Number is required.")]
    [Display(Name = "Receiving Number")]
    public string ReceivingNumber { get; set; } = "";

    [Required(ErrorMessage = "Product Code is required.")]
    [Display(Name = "Product Code")]
    public string ProductCode { get; set; } = "";

    [Range(typeof(decimal), "0.0001", "9999999999.9999",
        ErrorMessage = "Quantity must be positive.")]
    public decimal Quantity { get; set; }

    [Required(ErrorMessage = "Owner Code is required.")]
    [Display(Name = "Owner Code")]
    public string OwnerCode { get; set; } = "SELF";

    [Required(ErrorMessage = "Location Code is required.")]
    [Display(Name = "Location Code")]
    public string LocationCode { get; set; } = "";

    [Display(Name = "Lot Number")]
    public string? LotNumber { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Expiry Date")]
    public DateOnly? ExpiryDate { get; set; }

    [Display(Name = "Pallet Number")]
    public string? PalletNumber { get; set; }

    [Display(Name = "PO Number")]
    public string? PoNumber { get; set; }

    [Display(Name = "PO Line")]
    public int? PoLineNumber { get; set; }

    public string? Notes { get; set; }
}
