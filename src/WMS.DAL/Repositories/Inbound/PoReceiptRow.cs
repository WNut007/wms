namespace WMS.DAL.Repositories.Inbound;

// Read-projection for the PO Detail Receipts tab (TD-030). Distinct
// from ReceivingActivityRow (used by /Warehouses Activity feed) — this
// shape carries TotalReceivedQty for the Receipts table column, which
// the activity-feed shape doesn't need.
public sealed record PoReceiptRow(
    Guid Id,
    string ReceivingNumber,
    DateTime ReceivedAt,
    string Status,
    int LineCount,
    decimal TotalReceivedQty);
