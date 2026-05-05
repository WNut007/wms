namespace WMS.Common.Multitenancy;

// Tells whether a tenant is currently allowed to operate (Status = 'Active').
// Read by TenantValidationMiddleware on every authenticated request, so the
// implementation is expected to cache aggressively — the master DB read
// should hit at most once per tenant per cache window.
public interface ITenantStatusReader
{
    Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct = default);
}
