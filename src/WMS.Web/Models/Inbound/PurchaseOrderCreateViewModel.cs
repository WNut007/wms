using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Inbound;

// View-model for /PurchaseOrders/Create. 3-section V1 stepper:
// 1. Header (PO number + Vendor + Warehouse + Expected date + Notes)
// 2. Lines (editable Alpine grid backed by `Lines` list)
// 3. Review (read-only summary; submit from here)
//
// Status defaults to 'Open' server-side — no UI selection on Create.
public sealed class PurchaseOrderCreateViewModel
{
    // --- Step 1 Header ----------------------------------------------

    [Required(ErrorMessage = "PO number is required.")]
    [StringLength(50, ErrorMessage = "PO number must be at most 50 characters.")]
    [Display(Name = "PO number")]
    public string PoNumber { get; set; } = "";

    [Required(ErrorMessage = "Vendor is required.")]
    [Display(Name = "Vendor")]
    public Guid OwnerId { get; set; }

    [Required(ErrorMessage = "Warehouse is required.")]
    [Display(Name = "Warehouse")]
    public Guid WarehouseId { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Expected date")]
    public DateOnly? ExpectedDate { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    // --- Step 2 Lines -----------------------------------------------

    public List<PurchaseOrderLineViewModel> Lines { get; set; } = new();

    // --- Lookup data (controller-populated) -------------------------
    public IReadOnlyList<LookupItem> Vendors    { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Products   { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms       { get; set; } = Array.Empty<LookupItem>();
}
