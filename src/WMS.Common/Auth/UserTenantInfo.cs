namespace WMS.Common.Auth;

// Tenant the requesting user is allowed to enter, joined with the
// tenant's display fields. Returned by IUserTenantMapRepository (DAL)
// and surfaced through LoginResult (BLL) — defined in Common so both
// layers can use the same shape without one referencing the other.
public sealed record UserTenantInfo(
    Guid TenantId,
    string TenantCode,
    string TenantName);
