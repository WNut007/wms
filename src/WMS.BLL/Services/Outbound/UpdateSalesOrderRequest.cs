namespace WMS.BLL.Services.Outbound;

// Phase 14A — Edit form payload. SoNumber + CustomerId + WarehouseId
// frozen post-create. OrderDate + RequestedShipDate + Notes editable
// at any non-terminal status. ReplaceLines flag triggers a full
// DELETE + INSERT — service rejects with InvalidOperationException
// when SO is not in 'Draft' status (lines locked once allocation is
// possible; allocation reversal is a Phase 14B concern).
public sealed record UpdateSalesOrderRequest(
    DateOnly OrderDate,
    DateOnly? RequestedShipDate,
    string? Notes,
    bool ReplaceLines,
    IReadOnlyList<UpdateSalesOrderLineRequest> Lines);

public sealed record UpdateSalesOrderLineRequest(
    int LineNumber,
    Guid ProductId,
    Guid OwnerId,
    Guid UomId,
    decimal OrderedQuantity,
    decimal? UnitPrice,
    string? Notes);
