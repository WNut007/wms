namespace WMS.BLL.Services.Auth;

// Decoded payload of a valid pre-auth token. Returned by
// IAuthService.ValidatePreAuthTokenAsync — null result means the token
// is missing, expired, or already consumed.
//
// RequiresPasswordChange (P0 #4 / Phase 30A post-fix): when true, the
// token was issued for a user with MustChangePassword=true; the only
// allowed next step is POST to the in-flow change-password endpoint,
// NOT the normal tenant-select step. Standard cookie issuance is
// deferred until the password is changed.
public sealed record PreAuthData(
    Guid Id,
    string UserEmail,
    DateTime ExpiresAt,
    string? IpAddress,
    bool RequiresPasswordChange);
