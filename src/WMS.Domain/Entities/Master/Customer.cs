namespace WMS.Domain.Entities.Master;

// Maps to master.Customers (migration 018 + Country column from migration
// 004). Tenant-scoped per ADR-001.
//
// BaseEntity.Version is unused — master.Customers has no Version column
// (see Product.cs note for the same caveat).
public sealed class Customer : BaseEntity
{
    // Tenant-wide unique (UQ index on Code).
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    // 'B2B' | 'B2C'. CHECK enforces. Drives ordering UX, pricing model,
    // and whether the B2B-only fields below are populated.
    public string CustomerType { get; set; } = "B2C";

    // B2B-only legal-entity fields. NULL for B2C. The DB does not enforce
    // "B2B requires CompanyName/TaxId" — BLL validates at write time.
    public string? CompanyName { get; set; }
    public string? TaxId { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    // TrueCommerce stratification ('Tier1'..'Tier4'). CHECK allows NULL —
    // new customers haven't been tiered until the first periodic review.
    public string? CustomerTier { get; set; }
    public decimal? AnnualRevenue { get; set; }
    public int? OrdersPerMonth { get; set; }
    public decimal? AvgOrderValue { get; set; }

    // Opt-in flags set by sales/admin. Default false.
    public bool IsKeyAccount { get; set; }
    public bool IsStrategic { get; set; }

    // Lower number = higher allocation priority. NULL falls through to
    // default rules.
    public int? AllocationPriority { get; set; }
    public int? SafetyStockDays { get; set; }

    // 0.00–100.00. DECIMAL(5,2) supports up to 999.99 for reporting
    // overshoot.
    public decimal? PromisedFillRate { get; set; }

    // FK → master.Carriers(Id), wired by migration 027 with ON DELETE
    // SET NULL.
    public Guid? PreferredCarrierId { get; set; }
    public string? DefaultPaymentTerms { get; set; }

    // 'Active' | 'Inactive' | 'Suspended' | 'Draft'. CHECK enforces.
    // Mock 'pending' maps to 'Draft' at the controller boundary (Q4).
    public string Status { get; set; } = "Active";

    // Free-text country label. Added by migration 004 (Phase 6B Q2).
    // ISO-2 conversion deferred — see migration comment.
    public string? Country { get; set; }
}
