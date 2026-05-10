namespace WMS.BLL.Services.Outbound;

// Phase 14A — service-level Create payload. SoNumber is NOT in the
// request — service assigns SO-YYYYMMDD-NNNN at create time (matches
// the Adjustment / CycleCount / Transfer pattern from later phases).
// Lands as Draft.
public sealed record CreateSalesOrderRequest(
    Guid CustomerId,
    Guid WarehouseId,
    DateOnly OrderDate,
    DateOnly? RequestedShipDate,
    string? Notes,
    IReadOnlyList<CreateSalesOrderLineRequest> Lines);

// One line on a new SO. Owner preserved per ADR-007. UnitPrice is
// optional (free-text MVP; pricing strategy resolves in a later phase).
public sealed record CreateSalesOrderLineRequest(
    int LineNumber,
    Guid ProductId,
    Guid OwnerId,
    Guid UomId,
    decimal OrderedQuantity,
    decimal? UnitPrice,
    string? Notes);
