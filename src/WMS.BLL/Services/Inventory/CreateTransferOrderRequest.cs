namespace WMS.BLL.Services.Inventory;

// Phase 13 (ADR-012) — Create-form payload for ITransferOrderService
// .CreateAsync. Header fields + line set. Lands as Draft.
public sealed record CreateTransferOrderRequest(
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    string Reason,
    string? Notes,
    IReadOnlyList<CreateTransferOrderLineRequest> Lines);

public sealed record CreateTransferOrderLineRequest(
    int LineNumber,
    Guid ProductId,
    Guid OwnerId,
    Guid UomId,
    Guid? LotId,
    Guid? PalletId,
    Guid FromLocationId,
    Guid ToLocationId,
    decimal QtyRequested,
    string? Notes);

// Per-line dispatch payload — operator's actual picked qty.
public sealed record DispatchLineRequest(
    Guid LineId,
    decimal QtyDispatched);

// Per-line receive payload — destination operator's actual count.
public sealed record ReceiveLineRequest(
    Guid LineId,
    decimal QtyReceived);
