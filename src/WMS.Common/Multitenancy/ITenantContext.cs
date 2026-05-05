namespace WMS.Common.Multitenancy;

// Per-request tenancy lookup. Wraps ICurrentUser's TenantId claim so
// callers don't have to know it comes from a cookie — paves the way for
// alternate sources (e.g. an API-key tenant header) without rippling
// changes through tenant-scoped services.
public interface ITenantContext
{
    // Null between Step 1 (email/password) and Step 2 (tenant select),
    // and for any anonymous request.
    Guid? CurrentTenantId { get; }

    // Throws InvalidOperationException when CurrentTenantId is null —
    // for tenant-scoped services that have no anonymous path.
    Guid RequireTenantId();
}
