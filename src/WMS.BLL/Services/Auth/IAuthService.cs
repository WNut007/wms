using WMS.Domain.Entities.Security;

namespace WMS.BLL.Services.Auth;

// Authentication primitives for the 3-step login flow:
//
//   Step 1: AuthenticateAsync(email, password, ip, ua) → LoginResult
//             (orchestrates: tenant lookup → VerifyPassword → log → token)
//           Lower-level primitives below are exposed for tests and for
//           callers that need finer control (e.g. background seed jobs).
//   Step 2: ValidatePreAuthTokenAsync(token) → PreAuthData?
//           MarkPreAuthTokenUsedAsync(token)  (consumes the token)
//
// Tenant resolution + cookie issuing live in the AuthController layer
// (later chunks); this interface keeps the auth-data primitives tenant-
// scoped where appropriate and master-scoped (PreAuthToken /
// LoginAttempt) where needed.
public interface IAuthService
{
    // High-level Step 1 entry point. Performs tenant lookup
    // (master.UserTenantMap), password verification at the user's
    // primary tenant (IsDefault, then alphabetical), login-attempt
    // logging, and pre-auth token issuance — all in one call so
    // controllers don't have to re-orchestrate.
    //
    // Returns a discriminated LoginResult. FailureReason is stable
    // ("UnknownEmail" / "InvalidPassword") for switch-style handling;
    // the user-facing message should always be the same.
    Task<LoginResult> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

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
    // `requiresPasswordChange` (P0 #4): when true, the issued token can
    // only be consumed by the in-flow change-password endpoint, not the
    // normal tenant-select step.
    Task<string> CreatePreAuthTokenAsync(
        string email,
        string? ipAddress,
        CancellationToken ct = default,
        bool requiresPasswordChange = false);

    Task<PreAuthData?> ValidatePreAuthTokenAsync(
        string token,
        CancellationToken ct = default);

    Task MarkPreAuthTokenUsedAsync(
        string token,
        CancellationToken ct = default);

    // P0 #4 — in-flow forced password change. Verifies the token has
    // RequiresPasswordChange=true; verifies the new password against
    // PasswordPolicy; updates the user's hash + clears MustChangePassword
    // on the user row; consumes the old token; issues a NEW token (without
    // the flag) so the caller can continue the normal Step 2 / Step 3
    // login chain.
    //
    // Returns the new token + the user's tenant list on success.
    // Returns null PreAuthToken on failure (token invalid/expired/wrong
    // flag/policy violation); FailureReason explains which.
    Task<LoginResult> ApplyForcedPasswordChangeAsync(
        string token,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}
