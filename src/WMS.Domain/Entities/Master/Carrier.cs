namespace WMS.Domain.Entities.Master;

// Maps to master.Carriers (migration 021). Tenant-scoped per ADR-001.
//
// Two orthogonal status concepts:
//   * Status (lifecycle): Inactive → Configured → Tested → Production.
//     Default 'Inactive' so a freshly-created carrier can't route real
//     shipments before testing.
//   * IsActive (soft-delete): true by default. Lets ops temporarily
//     withdraw a Production carrier without losing its config.
//
// A carrier is "available for assignment" (Phase 7 GetActiveAsync) only
// when BOTH Status='Production' AND IsActive=1.
//
// BaseEntity.Version unused — master.Carriers has no Version column.
public sealed class Carrier : BaseEntity
{
    // Tenant-wide unique. Examples: 'FLASH', 'KERRY', 'JT', 'THAIPOST'.
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    // Optional classification. CK enforces null OR one of
    // 'Express' | 'Standard' | 'Postal' | 'Bike'. Drives UI grouping
    // and rate-card filtering.
    public string? Type { get; set; }

    // ISO 3166-1 alpha-2 (e.g. 'TH', 'SG'). Default 'TH'.
    public string? Country { get; set; }

    // 'Inactive' | 'Configured' | 'Tested' | 'Production'. CK enforces.
    public string Status { get; set; } = "Inactive";

    // JSON array of supported service-level codes (e.g.
    // ["Same","Next","Standard","Economy"]). NVARCHAR(MAX); app-layer
    // validates shape — free-form payload for now.
    public string? SupportedServiceLevels { get; set; }

    public string? WebsiteUrl { get; set; }
    public string? LogoUrl { get; set; }

    // Lower number = higher priority in carrier selection UI. NULL =
    // unsorted relative to others.
    public int? DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
