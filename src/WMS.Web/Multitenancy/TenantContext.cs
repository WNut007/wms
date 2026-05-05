using WMS.Common.Auth;
using WMS.Common.Multitenancy;

namespace WMS.Web.Multitenancy;

// Reads the tenant claim through ICurrentUser. A separate type (rather
// than collapsing into ICurrentUser) keeps tenant-scoped services from
// depending on identity concerns they don't need.
internal sealed class TenantContext : ITenantContext
{
    private readonly ICurrentUser _currentUser;

    public TenantContext(ICurrentUser currentUser) => _currentUser = currentUser;

    public Guid? CurrentTenantId => _currentUser.TenantId;

    public Guid RequireTenantId() =>
        CurrentTenantId ?? throw new InvalidOperationException(
            "Tenant context not set — request reached a tenant-scoped service " +
            "before login Step 2 (tenant select) completed.");
}
