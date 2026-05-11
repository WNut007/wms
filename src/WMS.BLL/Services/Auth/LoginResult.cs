using WMS.Common.Auth;

namespace WMS.BLL.Services.Auth;

// Outcome of AuthService.AuthenticateAsync (login Step 1). On success,
// PreAuthToken is the value the caller should hand to the user (cookie
// or redirect parameter) for Step 2; Tenants is the list to drive the
// tenant-select UI (smart-skipped if Count == 1).
//
// FailureReason values are stable strings the controller can switch
// on — the user-facing message ("Invalid credentials") is always the
// same to avoid leaking which half failed (timing-side leak still
// exists; tightening that is a later concern).
public sealed record LoginResult(
    bool Success,
    string? FailureReason,
    string? PreAuthToken,
    IReadOnlyList<UserTenantInfo> Tenants,
    bool RequiresPasswordChange = false)
{
    public static LoginResult Failed(string reason) =>
        new(false, reason, null, Array.Empty<UserTenantInfo>());

    public static LoginResult Succeeded(string token, IReadOnlyList<UserTenantInfo> tenants) =>
        new(true, null, token, tenants);

    // P0 #4 — login Step 1 verified credentials, but the user has
    // MustChangePassword=true. Token is flagged so only the in-flow
    // change-password endpoint will accept it; tenant select is deferred.
    public static LoginResult RequiresForcedPasswordChange(string token, IReadOnlyList<UserTenantInfo> tenants) =>
        new(true, null, token, tenants, RequiresPasswordChange: true);
}
