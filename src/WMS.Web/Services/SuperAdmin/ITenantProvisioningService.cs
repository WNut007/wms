namespace WMS.Web.Services.SuperAdmin;

// Phase 27 — synchronous tenant provisioning. ~10-30 seconds total
// (CREATE DATABASE + ~30 migrations + seed). UI shows a loading state
// during the round-trip.
//
// Steps:
//   1. Validate (code unique, format)
//   2. Insert master.Tenants row (Status='Active')
//   3. CREATE DATABASE [Tenant_{slug}] via master-DB connection
//   4. Run Tenant-tagged migrations against the new DB
//   5. Seed bootstrap ADMIN user with temp password + MustChangePassword=true
//   6. Grant ADMIN role to the user
//   7. Map admin email to tenant in master.UserTenantMap
//   8. Return ProvisioningResult with temp password (displayed ONCE)
//
// Rollback on any step failure: drop DB if created, delete tenant row.
public interface ITenantProvisioningService
{
    Task<TenantProvisioningResult> CreateTenantAsync(
        string code,
        string name,
        string adminEmail,
        string? adminFullName,
        Guid actorSuperAdminId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // Lifecycle methods — operate against master.Tenants Status enum.
    Task SuspendAsync(Guid tenantId, string reason, Guid actorSuperAdminId,
        string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task ReactivateAsync(Guid tenantId, Guid actorSuperAdminId,
        string? ipAddress, string? userAgent, CancellationToken ct = default);

    // Admin password reset — generates new strong temp password, sets
    // MustChangePassword=true on the tenant's bootstrap ADMIN user
    // (or first ADMIN if multiple). Returns the new temp password
    // (displayed ONCE).
    Task<string> ResetTenantAdminPasswordAsync(
        Guid tenantId,
        Guid actorSuperAdminId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}

public sealed record TenantProvisioningResult(
    Guid TenantId,
    string Code,
    string Name,
    string DatabaseName,
    string AdminEmail,
    string AdminTempPassword);
