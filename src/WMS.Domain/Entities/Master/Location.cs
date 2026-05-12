namespace WMS.Domain.Entities.Master;

// Maps to master.Locations (migration 007). Tenant-scoped per ADR-001;
// locations themselves are warehouse-scoped (Code unique within
// warehouse via UX_Locations_Warehouse_Code, not tenant-wide).
//
// Three orthogonal status concepts:
//   * Status — operational state (Active | Blocked | Maintenance).
//     CK_Locations_Status enforces. Default 'Active'.
//   * IsActive — soft-delete flag. Default true.
//   * Pickface — whether this location is dedicated to picking.
//
// Locations have NO Name column (Migration_007 shape — Description is
// the long-form label; Code is the scanner-friendly identifier).
//
// 3D position fields (PositionX/Y/Z, Rotation, Aisle/Bay/Level) exist
// for ADR-011 future 3D viz. Form surfaces them in an "advanced"
// section rather than hiding.
//
// BaseEntity.Version unused — master.Locations has no Version column.
public sealed class Location : BaseEntity
{
    public Guid WarehouseId { get; set; }

    public Guid ZoneId { get; set; }

    // Tenant-wide NOT unique — unique only within a warehouse.
    public string Code { get; set; } = "";

    public string? Description { get; set; }

    // Physical dimensions (cm) — interior.
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    // Capacity (BC pattern).
    public decimal? CapacityVolumeCubicCm { get; set; }
    public decimal? CapacityWeightKg { get; set; }
    // 'NoLimit' | 'ByVolume' | 'ByWeight' | 'ByEither' | 'ByBoth'.
    public string CapacityPolicy { get; set; } = "NoLimit";

    // Lower = picked/filled first. Default 100.
    public int BinRank { get; set; } = 100;

    public bool AllowMultipleItems { get; set; } = true;

    // Pick thresholds.
    public decimal? MinPickQty { get; set; }
    public decimal? MaxPickQty { get; set; }

    // 3D coordinates (ADR-011 — viz deferred to Phase 4+).
    public decimal? PositionX { get; set; }
    public decimal? PositionY { get; set; }
    public decimal? PositionZ { get; set; }
    public decimal Rotation { get; set; } // NOT NULL, default 0

    // Grouping for visualization + reporting.
    public string? Aisle { get; set; }
    public int? Bay { get; set; }
    public int? Level { get; set; }

    public bool Show3D { get; set; } = true;
    public string? DisplayColor { get; set; }
    public bool IsPickface { get; set; }

    // 'Active' | 'Blocked' | 'Maintenance'. CK enforces.
    public string Status { get; set; } = "Active";

    public DateTime? LastVerifiedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
