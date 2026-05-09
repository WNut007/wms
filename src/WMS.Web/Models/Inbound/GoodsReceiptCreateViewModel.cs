using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Inbound;

// Phase 9B — Desktop Goods Receipt Create form. 4-tab V1 Section
// stepper:
//   1. Header  — PO selector, receiving number, date, warehouse, notes
//   2. Lines   — editable grid (Phase 9A pattern); pre-filled from PO
//   3. Documents — upload deferred to Detail page (Phase 9C); shows
//                  hint here that uploads happen post-create
//   4. Activity  — empty until posted; renders placeholder
//
// Two submit paths:
//   - Save Draft  → IsDraft=true, Status='Draft', no stock movement
//   - Post Receipt → IsDraft=false, full orchestration (existing path)
public sealed class GoodsReceiptCreateViewModel
{
    // --- Tab 1 Header ----------------------------------------------

    // Optional — null = blind receipt (no PO link). When set, the
    // grid pre-fills from PO lines via AJAX.
    [Display(Name = "Purchase order")]
    public Guid? PurchaseOrderId { get; set; }

    // Server-set when PurchaseOrderId is selected; round-trips so the
    // form doesn't have to re-query on POST validate-and-redisplay.
    public string? PoNumber { get; set; }

    [Required(ErrorMessage = "Receiving number is required.")]
    [StringLength(50, ErrorMessage = "Receiving number must be at most 50 characters.")]
    [Display(Name = "Receiving number")]
    public string ReceivingNumber { get; set; } = "";

    [Required(ErrorMessage = "Receiving date is required.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Received at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [Required(ErrorMessage = "Warehouse is required.")]
    public Guid WarehouseId { get; set; }

    public string? WarehouseCode { get; set; }

    // Vendor display (from PO when set; blank for blind).
    public string? VendorCode { get; set; }
    public string? VendorName { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    // Hidden flag — controller's POST handler sets it from the button
    // clicked (Save Draft vs Post Receipt). Default false; UI sets
    // via two submit buttons with different name/value.
    public bool IsDraft { get; set; }

    // --- Tab 2 Lines -----------------------------------------------

    public List<GoodsReceiptLineViewModel> Lines { get; set; } = new();

    // --- Lookup data (controller-populated) -----------------------

    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Vendors    { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Products   { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms       { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Locations  { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Owners     { get; set; } = Array.Empty<LookupItem>();

    // Active POs for the picker — Code + Name (PoNumber + Vendor).
    public IReadOnlyList<LookupItem> OpenPurchaseOrders { get; set; } = Array.Empty<LookupItem>();
}
