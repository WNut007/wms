namespace WMS.Domain.Entities.Master;

// Maps to master.Warehouses (migration 002). Tenant-scoped per ADR-001.
//
// Distinct from WMS.Common.Auth.WarehouseInfo — that's a 3-field
// projection (Id, Code, Name) used only by the Step 3 tenant-warehouse
// picker on login. This is the full entity used by the /Warehouses
// list/detail UI starting in Phase 6B.
//
// Soft-delete is via IsActive (bool) — distinct from Products/Customers
// which use a richer Status string. The mock UI's "maintenance"
// intermediate state is intentionally dropped; if the business needs it
// back, that becomes a Status NVARCHAR(20) migration (TD-009).
//
// BaseEntity.Version is unused — master.Warehouses has no Version column.
public sealed class Warehouse : BaseEntity
{
    // Tenant-wide unique (UQ index on Code).
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    // Free-text address — multi-line addresses fit. Region/country
    // sub-fields are not normalised; if a structured address ever lands,
    // it's a separate concern.
    public string? Address { get; set; }

    // 'Main' | 'Satellite' | 'Branch'. CHECK enforces.
    public string Type { get; set; } = "Main";

    public string? PhoneNumber { get; set; }
    public string? ManagerName { get; set; }

    // IANA TZ id — NOT a fixed offset. NULL falls back to tenant default.
    public string? TimeZoneId { get; set; }

    // Soft-delete flag. Default true on insert.
    public bool IsActive { get; set; } = true;
}
