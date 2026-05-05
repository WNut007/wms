namespace WMS.Common.Auth;

// Identity of the user behind the current request — sourced from cookie
// claims, no DB hit. Anonymous requests (login page, /health, static files)
// resolve this with IsAuthenticated = false and null Guids rather than
// throwing, so callers can branch instead of guarding every access.
//
// Every Service that writes CreatedBy / UpdatedBy MUST source the author
// from this — see CLAUDE.md "Audit Field FK Rules".
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    Guid? WarehouseId { get; }
    string? Email { get; }
    string? FullName { get; }
    IReadOnlyList<string> Roles { get; }
}
