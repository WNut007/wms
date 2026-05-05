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
    IReadOnlyList<UserTenantInfo> Tenants)
{
    public static LoginResult Failed(string reason) =>
        new(false, reason, null, Array.Empty<UserTenantInfo>());

    public static LoginResult Succeeded(string token, IReadOnlyList<UserTenantInfo> tenants) =>
        new(true, null, token, tenants);
}
