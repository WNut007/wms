using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Inventory;

// Phase 13 — Create-form payload. Header fields + multi-line grid.
public sealed class TransferOrderCreateViewModel
{
    [Required(ErrorMessage = "Source warehouse is required.")]
    public Guid FromWarehouseId { get; set; }

    [Required(ErrorMessage = "Destination warehouse is required.")]
    public Guid ToWarehouseId { get; set; }

    [Required(ErrorMessage = "Reason is required.")]
    public string Reason { get; set; } = "Other";

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<TransferOrderLineViewModel> Lines { get; set; } = new();

    // Lookups (populated by controller for re-display).
    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Products   { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Owners     { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Uoms       { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> FromLocations { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> ToLocations   { get; set; } = Array.Empty<LookupItem>();

    public static readonly IReadOnlyList<string> AllReasons = new[]
    {
        "Rebalance",
        "Replenish",
        "CustomerOrder",
        "Other",
    };
}

public sealed class TransferOrderLineViewModel
{
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }
    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    public decimal QtyRequested { get; set; }
    public string? Notes { get; set; }
}

// Per-line dispatch entry (one form, many rows).
public sealed class DispatchLineEntryViewModel
{
    public Guid LineId { get; set; }
    [Range(0.0001, 1_000_000_000d, ErrorMessage = "Dispatched qty must be positive.")]
    public decimal QtyDispatched { get; set; }
}

public sealed class DispatchTransferViewModel
{
    public List<DispatchLineEntryViewModel> Lines { get; set; } = new();
}

public sealed class ReceiveLineEntryViewModel
{
    public Guid LineId { get; set; }
    [Range(0d, 1_000_000_000d, ErrorMessage = "Received qty must be non-negative.")]
    public decimal QtyReceived { get; set; }
}

public sealed class ReceiveTransferViewModel
{
    public List<ReceiveLineEntryViewModel> Lines { get; set; } = new();
}

public sealed record CancelTransferViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Cancellation reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}

public sealed record MarkLostTransferViewModel
{
    [Required]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Loss reason is required.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Reason must be 3–500 characters.")]
    public string Reason { get; init; } = "";
}
