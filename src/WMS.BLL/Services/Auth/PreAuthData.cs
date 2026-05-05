namespace WMS.BLL.Services.Auth;

// Decoded payload of a valid pre-auth token. Returned by
// IAuthService.ValidatePreAuthTokenAsync — null result means the token
// is missing, expired, or already consumed.
public sealed record PreAuthData(
    Guid Id,
    string UserEmail,
    DateTime ExpiresAt,
    string? IpAddress);
