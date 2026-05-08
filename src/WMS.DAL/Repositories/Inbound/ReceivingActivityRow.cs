namespace WMS.DAL.Repositories.Inbound;

// Read-only projection for the Warehouse Detail Activity tab feed —
// one row per receipt event. Carries the resolved PerformedByName +
// a denormalised LineCount so the Activity panel can render
// "{user} posted receipt RC-2026-0142 · 12 lines" without a separate
// lookup per row.
//
// Same separation pattern as StockMovementListRow vs StockMovement —
// activity-feed shape is distinct from the write-side ReceivingHeader
// + ReceivingLines aggregate, so it lives as its own tiny DTO.
public sealed record ReceivingActivityRow(
    Guid Id,
    string ReceivingNumber,
    DateTime ReceivedAt,
    string Status,
    string PerformedByName,
    int LineCount);
