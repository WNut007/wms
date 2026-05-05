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
}
