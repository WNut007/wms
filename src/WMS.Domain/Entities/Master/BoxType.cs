namespace WMS.Domain.Entities.Master;

// Maps to master.BoxTypes (migration 010). Tenant-scoped per ADR-001.
//
// Phase 14D introduced this table as a dropdown lookup on the Pack Detail
// submit form. Phase 30A.3 Block 1 adds admin CRUD so operators can
// manage their carton catalogue from the master-data UI.
//
// BaseEntity.{CreatedBy, UpdatedBy, Version} unused — master.BoxTypes
// has no audit-by columns or Version (matches Migration_010 shape). The
// repo's INSERT/UPDATE SQL omits them.
public sealed class BoxType : BaseEntity
{
    // Tenant-wide unique (UQ index on Code).
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    // Internal dimensions — usable interior for box-fit calculations.
    public decimal? InternalLengthCm { get; set; }
    public decimal? InternalWidthCm { get; set; }
    public decimal? InternalHeightCm { get; set; }

    // Volume stored separately rather than computed: admin may override
    // L*W*H when usable interior < bounding box.
    public decimal? InternalVolumeCubicCm { get; set; }

    // External dimensions — bounding box for carrier dim-weight pricing.
    public decimal? ExternalLengthCm { get; set; }
    public decimal? ExternalWidthCm { get; set; }
    public decimal? ExternalHeightCm { get; set; }

    // 3-decimal precision: small boxes weigh ~0.150 kg and rounding can
    // shift carrier billing brackets (Phase 14D context).
    public decimal? EmptyWeightKg { get; set; }
    public decimal? MaxLoadKg { get; set; }

    // Procurement unit cost — feeds 3PL packaging billing (ADR-006).
    public decimal? UnitCost { get; set; }

    public bool IsActive { get; set; } = true;
}
