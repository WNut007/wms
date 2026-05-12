namespace WMS.Domain.Entities.Master;

// Maps to master.Zones (migration 006). Tenant-scoped per ADR-001;
// zones themselves are warehouse-scoped (Code is unique within a
// warehouse, not tenant-wide).
//
// Type is a closed enum driving putaway/picking routing decisions:
// 'Receiving' | 'Storage' | 'Picking' | 'Packing' | 'Shipping' |
// 'Staging' | 'Quarantine' | 'Returns'.
//
// Temperature is optional metadata (NULL/Ambient/Chilled/Frozen).
//
// LotCommingleAllowed defaults true. Phase 31 lot-tracking work
// will surface this in allocation rules; today it's display + edit
// only (no enforcement logic yet).
//
// BaseEntity.{CreatedBy, UpdatedBy, Version} unused — master.Zones
// has only CreatedAt + UpdatedAt (Migration_006 pre-dates the
// Phase 7 audit pair convention).
public sealed class Zone : BaseEntity
{
    public Guid WarehouseId { get; set; }

    // Tenant-wide NOT unique — unique only within a warehouse via
    // UX_Zones_Warehouse_Code. Same code can repeat across warehouses
    // (e.g. every warehouse has its own 'PICK-A').
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Type { get; set; } = "Storage";

    public string? Temperature { get; set; }

    // Free-form list (comma or JSON) — restrictions are advisory;
    // not DB-enforced. BLL validates when stocking decisions run.
    public string? AllowedProductTypes { get; set; }

    public bool LotCommingleAllowed { get; set; } = true;

    public bool IsActive { get; set; } = true;
}
