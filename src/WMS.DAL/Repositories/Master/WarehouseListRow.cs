namespace WMS.DAL.Repositories.Master;

// Read-only projection for Warehouse list pages (/Warehouses list view).
// LocationCount: COUNT(*) over master.Locations for this warehouse.
// Mirrors the JOIN-aggregate pattern of ProductListRow.StockOnHand.
//
// IsActive (bool) is preserved here — the controller maps it to a
// "active"/"inactive" wire string at the boundary, keeping the DTO
// faithful to the schema (TD-009 covers the missing "maintenance"
// intermediate state).
public sealed record WarehouseListRow(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive,
    int LocationCount,
    string? Address,
    string? ManagerName,
    string? PhoneNumber,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
