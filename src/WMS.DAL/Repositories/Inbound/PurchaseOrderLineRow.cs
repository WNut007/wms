namespace WMS.DAL.Repositories.Inbound;

// Read-projection for the PO Detail Lines tab (TD-029). Carries
// resolved Product code + name + UoM code so the Lines panel renders
// from a single round-trip without per-row lookups.
//
// Same separation pattern as PurchaseOrderListRow vs PurchaseOrder
// entity — entity carries raw Guid FKs, list/detail rows carry
// JOINed display fields.
public sealed record PurchaseOrderLineRow(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UomId,
    string UomCode,
    decimal ExpectedQuantity,
    decimal ReceivedQuantity,
    string Status);
