using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Outbound;

// Phase 14A — Create-form payload. Header fields + multi-line grid.
// SoNumber NOT in the form — service assigns SO-YYYYMMDD-NNNN at
// create time.
public sealed class SalesOrderCreateViewModel
{
    [Required(ErrorMessage = "Customer is required.")]
    public Guid CustomerId { get; set; }

    [Required(ErrorMessage = "Warehouse is required.")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Order date is required.")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.UtcNow.Date;

    [DataType(DataType.Date)]
    public DateTime? RequestedShipDate { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
    public string? Notes { get; set; }

    public List<SalesOrderLineViewModel> Lines { get; set; } = new();

    // Lookups populated by controller for re-display.
    public IReadOnlyList<LookupItem> Customers  { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Products   { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Owners     { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms       { get; set; } = Array.Empty<LookupItem>();
}

// Phase 14A — Edit-form payload. SoNumber + CustomerId + WarehouseId
// frozen post-create (read-only display via separate fields). Lines
// editable only when Status='Draft'; controller re-populates this VM
// with `LinesLocked=true` otherwise so the Razor template can render
// the lines section read-only.
public sealed class SalesOrderEditViewModel
{
    [Required]
    public Guid Id { get; set; }

    // Read-only display fields (header reference info).
    public string SoNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";

    [Required(ErrorMessage = "Order date is required.")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? RequestedShipDate { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
    public string? Notes { get; set; }

    // ReplaceLines triggers a full DELETE + INSERT on the lines side.
    // Service rejects with InvalidOperationException when SO is not
    // in 'Draft' status.
    public bool ReplaceLines { get; set; }

    public List<SalesOrderLineViewModel> Lines { get; set; } = new();

    // Read-only flag the controller sets so the template knows whether
    // to render Lines editable or read-only with an explanation banner.
    public bool LinesLocked { get; set; }

    // Lookups (same as Create).
    public IReadOnlyList<LookupItem> Products { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Owners   { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms     { get; set; } = Array.Empty<LookupItem>();
}

// Per-line shape for both Create and Edit. ASP.NET Core model-binder
// rebuilds the List<T> from indexed names like Lines[0].ProductId,
// Lines[1].ProductId, etc. (same pattern as Phase 9A PO + Phase 13
// Transfer).
public sealed class SalesOrderLineViewModel
{
    public int LineNumber { get; set; }

    [Required]
    public Guid ProductId { get; set; }

    [Required]
    public Guid OwnerId { get; set; }

    [Required]
    public Guid UomId { get; set; }

    [Range(0.0001, 1_000_000_000d,
        ErrorMessage = "Ordered quantity must be positive.")]
    public decimal OrderedQuantity { get; set; }

    [Range(0d, 1_000_000_000d,
        ErrorMessage = "Unit price cannot be negative.")]
    public decimal? UnitPrice { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
