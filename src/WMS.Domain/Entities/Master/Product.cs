namespace WMS.Domain.Entities.Master;

// Maps to master.Products (migration 014 + Brand column from migration
// 003). Tenant-scoped — the connection IS the tenant boundary per
// ADR-001, no TenantId column.
//
// BaseEntity carries Id + audit + Version. Note: master.Products has NO
// Version column (low-churn metadata, optimistic concurrency not used);
// repos must omit Version from SELECT/INSERT/UPDATE SQL — Dapper leaves
// the property at default 0.
public sealed class Product : BaseEntity
{
    // SKU. Tenant-wide unique (UQ index on Code). Up to 50 chars to fit
    // vendor prefixes / variant suffixes — wider than typical 20-char
    // master codes.
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    // FK → master.ProductCategories(Id). NOT NULL — every product belongs
    // to a category for tree browse + allocation.
    public Guid CategoryId { get; set; }

    // FK → master.UnitsOfMeasure(Id). NOT NULL — stock math needs a base
    // unit per product.
    public Guid BaseUomId { get; set; }

    // 'None' | 'Lot' | 'LotAndSerial'. CHECK enforces. Default 'None' —
    // most products don't track lots; opt-in per SKU.
    public string TrackingMethod { get; set; } = "None";

    // ABC velocity classification ('A' | 'B' | 'C', possibly 'A+'/'B+').
    // Nullable — new SKUs have no velocity until the first review.
    public string? VelocityClass { get; set; }

    // Catch-weight (weigh at pick) — fresh-foods exception, not the norm.
    public bool UseCatchWeight { get; set; }

    // 'Active' | 'Inactive' | 'Discontinued' | 'Draft'. CHECK enforces.
    // PascalCase only — the controller boundary maps to/from lowercase
    // wire values (Q4).
    public string Status { get; set; } = "Active";

    // Brand display label. Added by migration 003 (Phase 6B Q2).
    // NULL for private-label / generic SKUs.
    public string? Brand { get; set; }
}
