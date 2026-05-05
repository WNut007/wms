using WMS.Domain.Entities.Security;

namespace WMS.BLL.Services.Auth;

// Authentication primitives for the 3-step login flow:
//
//   Step 1: VerifyPasswordAsync(tenantId, email, password) → User?
//           CreatePreAuthTokenAsync(email) → token string
//   Step 2: ValidatePreAuthTokenAsync(token) → PreAuthData?
//           MarkPreAuthTokenUsedAsync(token)  (consumes the token)
//
// Tenant resolution + cookie issuing live in the AuthController layer
// (Chunks B5/B6); this interface keeps the auth-data primitives tenant-
// scoped where appropriate and master-scoped (PreAuthToken /
// LoginAttempt) where needed.
public interface IAuthService
{
    // Returns the User on a correct password against a not-locked, active
    // account; null on any failure (unknown email, wrong password, locked,
    // disabled). Caller is expected to call LogLoginAttemptAsync separately
    // — this method does not log on its own so a controller can capture
    // request context (IP, UA) once.
    Task<User?> VerifyPasswordAsync(
        Guid tenantId,
        string email,
        string password,
        CancellationToken ct = default);

    // Static helper — no I/O. Cost factor sourced from constructor config.
    string HashPassword(string password);

    Task LogLoginAttemptAsync(
        string email,
        bool success,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    // Issues a fresh random base64url token, persists it, returns the
    // string the caller should hand to the user (cookie / redirect param).
    Task<string> CreatePreAuthTokenAsync(
        string email,
        string? ipAddress,
        CancellationToken ct = default);

    Task<PreAuthData?> ValidatePreAuthTokenAsync(
        string token,
        CancellationToken ct = default);

    Task MarkPreAuthTokenUsedAsync(
        string token,
        CancellationToken ct = default);
}
