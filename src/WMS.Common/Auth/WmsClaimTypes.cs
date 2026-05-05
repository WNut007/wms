namespace WMS.Common.Auth;

// Custom claim type names — stable across releases so persisted cookies
// keep resolving after a redeploy. ASP.NET's built-in ClaimTypes covers
// NameIdentifier / Email / Name / Role; we only define what the framework
// doesn't already name.
public static class WmsClaimTypes
{
    // Per-tenant identifier set after Step 2 of the login flow. Absent
    // until tenant selection completes, present for the rest of the session.
    public const string TenantId = "wms.tid";

    // Per-warehouse identifier set after Step 3 of the login flow. Absent
    // until warehouse selection completes (or is auto-skipped for users
    // bound to a single warehouse).
    public const string WarehouseId = "wms.wid";

    // Display-friendly tenant code (e.g. "DEMO") set alongside TenantId.
    // Sourced from master.Tenants.Code at Step 2 so layouts / nav can
    // render it without a per-request master DB hit. Stale on tenant
    // rename until next login (acceptable — tenant rename is rare).
    public const string TenantCode = "wms.tcode";

    // Display-friendly warehouse code (e.g. "WH-MAIN") set alongside
    // WarehouseId. Sourced from the tenant DB's master.Warehouses.Code
    // at Step 3.
    public const string WarehouseCode = "wms.wcode";
}
