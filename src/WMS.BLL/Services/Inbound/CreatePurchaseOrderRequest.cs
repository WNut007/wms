namespace WMS.BLL.Services.Inbound;

// Service-level shape for IPurchaseOrderService.CreateAsync. The
// service validates this, materialises Domain entities, and forwards
// to the repo's transactional CreateAsync.
public sealed record CreatePurchaseOrderRequest(
    string PoNumber,
    Guid OwnerId,
    Guid WarehouseId,
    DateOnly? ExpectedDate,
    string? Notes,
    IReadOnlyList<CreatePurchaseOrderLineRequest> Lines);

// One line on a new PO. LineNumber is supplied by the caller —
// auto-numbering belongs in a UI helper, not the service contract.
public sealed record CreatePurchaseOrderLineRequest(
    int LineNumber,
    Guid ProductId,
    Guid UomId,
    decimal ExpectedQuantity);
