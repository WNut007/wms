namespace WMS.Domain.Entities.Master;

// Maps to master.UnitsOfMeasure (migration 013). Tenant-scoped per
// ADR-001.
//
// Phase 7 introduced this as a dropdown lookup for Product Create's
// BaseUomId <select>. Phase 30A.3 Block 1 adds admin CRUD.
//
// BaseEntity.Version unused — master.UnitsOfMeasure has no Version
// column. CreatedBy/UpdatedBy DO exist on this table (migration 013
// kept the audit pair).
public sealed class Uom : BaseEntity
{
    // Tenant-wide unique (UQ index on Code). Examples: 'EA', 'KG', 'L'.
    public string Code { get; set; } = "";

    // Display name. Examples: 'Each', 'Kilogram', 'Litre'.
    public string Name { get; set; } = "";

    // 'Count' | 'Weight' | 'Volume' | 'Length'. CHECK enforces.
    // Drives conversion validity (cannot convert Weight→Volume) +
    // picker grouping.
    public string Type { get; set; } = "Count";

    // Base UoM within a Type (e.g. EA for Count, KG for Weight).
    // "One base per Type" expected — NOT enforced at DB; validator
    // checks (Migration_013 comment).
    public bool IsBase { get; set; }

    public bool IsActive { get; set; } = true;
}
