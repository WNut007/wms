namespace WMS.BLL.Services.Auth;

// Phase 25 — per-IP brute-force throttle for /Auth/Login. Sliding
// window: N attempts per window. Per-user lockout (5 fails → 30 min
// LockedUntil stamp) is enforced separately at the User row level.
//
// Singleton service backed by IMemoryCache. Pure in-process — multi-
// instance deployments need a shared-store implementation (Redis /
// DistributedCache) before horizontal scale. Logged as TD-060 if it
// becomes an issue.
//
// Anonymous IPs (null / "") are NOT throttled — the caller is expected
// to fall back to per-email throttling via the User row's lockout
// columns when the IP isn't available.
public interface ILoginRateLimiter
{
    // Returns true if the request is allowed; false if the IP has
    // exceeded the per-window threshold. Mutates internal counter as a
    // side effect of being called (so this method does both the check
    // AND the increment — atomic by design, can't be raced).
    bool TryRegisterAttempt(string? ipAddress);

    // Called on successful login. Clears the IP counter so the operator
    // doesn't get punished for a single mistyped attempt earlier in
    // the window.
    void Clear(string? ipAddress);

    // Lifetime + threshold — exposed for tests + future per-tenant
    // override (TD-061).
    int MaxAttemptsPerWindow { get; }
    TimeSpan Window { get; }
}
