using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Inbound;

// View-model for /PurchaseOrders/Edit/{id}. Single-page (Phase 7
// pattern). PoNumber + Vendor + Warehouse readonly post-create
// (UpdateHeaderAsync omits them from SET).
//
// LinesLocked flag, computed server-side from
// IPurchaseOrderRepository.CountReceivedLinesAsync, drives the UX:
//   false → full line edit (add/remove rows, change qty/uom/product)
//   true  → read-only line table + banner explaining cancel-and-
//           recreate is the path forward
public sealed class PurchaseOrderEditViewModel
{
    public static readonly IReadOnlyList<string> AllStatuses =
        new[] { "Open", "Receiving", "Closed", "Cancelled" };

    public Guid Id { get; set; }

    // Display-only — readonly + disabled in the form, hidden field
    // round-trips for the cancel link / breadcrumb.
    [Display(Name = "PO number")]
    public string PoNumber { get; set; } = "";

    [Display(Name = "Vendor")]
    public string VendorCode { get; set; } = "";
    public string VendorName { get; set; } = "";

    [Display(Name = "Warehouse")]
    public string WarehouseCode { get; set; } = "";

    [DataType(DataType.Date)]
    [Display(Name = "Expected date")]
    public DateOnly? ExpectedDate { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    // Status is display-only on Edit (transitions go through Archive
    // button or the Receive flow). Render as a badge.
    public string Status { get; set; } = "Open";

    public List<PurchaseOrderLineViewModel> Lines { get; set; } = new();

    // Computed server-side: true when any line has ReceivedQuantity > 0.
    public bool LinesLocked { get; set; }

    public IReadOnlyList<LookupItem> Products { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms     { get; set; } = Array.Empty<LookupItem>();
}
