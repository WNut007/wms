namespace WMS.DAL.Repositories.Outbound;

// Phase 14B — read-projection for the Detail Allocations panel.
// Resolved Location code + Lot/Pallet numbers + AllocatedBy name via
// JOINs through inventory.Stock + Lots + Pallets + security.Users.
//
// Used by IOrderAllocationRepository.GetActiveBy{Line,SalesOrder}Id-
// Async — single round-trip, no per-row lookups needed for the panel.
public sealed record OrderAllocationRow(
    Guid Id,
    Guid SalesOrderLineId,
    int LineNumber,
    Guid StockId,
    string LocationCode,
    string? LotNumber,
    string? PalletNumber,
    decimal AllocatedQuantity,
    string Status,
    DateTime AllocatedAt,
    string AllocatedByName);
