namespace WMS.DAL.Repositories.Master;

// Read-only projection for Product list pages (/Products list view).
// Carries the JOIN-derived columns (StockOnHand, CategoryCode) that
// shouldn't pollute the Product entity surface — entities map columns
// 1:1 with their backing table; aggregates and joined columns belong
// on dedicated read DTOs.
//
// StockOnHand: SUM(QuantityOnHand) over inventory.Stock for this
// product across all locations / lots / pallets / owners. ISNULL'd to
// 0 in SQL so products with no Stock rows show as 0 rather than NULL.
// Per Phase 6B §3 — uses raw OnHand, not OnHand-Allocated. If
// "available" semantics needed later, repo SQL switches to
// SUM(QuantityOnHand - QuantityAllocated).
public sealed record ProductListRow(
    Guid Id,
    string Code,
    string Name,
    string Status,
    string? Brand,
    Guid CategoryId,
    string CategoryCode,
    decimal StockOnHand,
    DateTime? UpdatedAt);
