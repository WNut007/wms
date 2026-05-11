namespace WMS.BLL.Services.SuperAdmin;

// Phase 27 — closed-set event types emitted to master.SystemAuditLog.
// Same discipline as the tenant-side AuditEventTypes (Phase 24/25):
// free-form vocabulary at the schema level, but the BLL stays
// disciplined and only emits from this list.
public static class SystemAuditEventTypes
{
    // SuperAdmin auth
    public const string SuperAdminLoginSuccess  = "SuperAdminLoginSuccess";
    public const string SuperAdminLoginFailure  = "SuperAdminLoginFailure";
    public const string SuperAdminLockout       = "SuperAdminLockout";
    public const string SuperAdminPasswordChange = "SuperAdminPasswordChange";

    // Tenant lifecycle
    public const string TenantCreated       = "TenantCreated";
    public const string TenantSuspended     = "TenantSuspended";
    public const string TenantReactivated   = "TenantReactivated";
    public const string TenantAdminPasswordReset = "TenantAdminPasswordReset";

    // Severity
    public const string SeverityInfo    = "Info";
    public const string SeverityWarning = "Warning";
    public const string SeverityError   = "Error";

    // EntityType
    public const string EntitySuperAdmin = "SuperAdmin";
    public const string EntityTenant     = "Tenant";
}
