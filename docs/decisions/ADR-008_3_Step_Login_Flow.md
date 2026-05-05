# ADR-008: 3-step login flow with cookie auth, smart-skip, and pre-auth tokens

**Status**: Accepted
**Date**: 2026-05-06
**Decision makers**: Project owner

---

## Context

WMS เป็น **multi-tenant + multi-warehouse**:

- ทุก tenant มี DB ของตัวเอง (ADR-001)
- ผู้ใช้คนหนึ่ง (`email`) อาจมีสิทธิ์เข้าหลาย tenants และหลาย warehouses
- ทุก operational request ต้องรู้ตอนนั้นกำลังทำงานอยู่ใน **(tenant, warehouse)** อะไร — ใช้ตัดสินทั้ง connection string + การ filter ข้อมูล

ปัญหาที่ต้อง solve ในการ login:

1. **Identity** — รู้ได้อย่างไรว่า email + password ถูก
2. **Tenancy** — user มีสิทธิ์เข้า tenant ไหน, ตอนนี้เลือก tenant ไหน
3. **Workspace** — กำลังทำงานที่ warehouse ไหน
4. **Continuity** — request ถัดๆ ไปต้องรู้ทั้ง 3 ข้อโดยไม่ต้องถามใหม่ทุกครั้ง

ข้อจำกัด / context:

- Frontend = MPA + htmx + Razor (ADR-003) — JWT bearer ไม่เหมาะ
- Backend = Dapper, ไม่มี ORM session (ADR-002)
- Mobile workflows = PWA (Receiver / Picker / Packer) — ใช้ auth model เดียวกับ desktop
- Throughput target = 5,000+ B2C orders/day → ทุก request ต้องไม่ hit master DB เพื่อ resolve identity

---

## Decision

### Session = ASP.NET Cookie Authentication

- Scheme name = built-in cookie scheme
- Cookie name = `wms.auth`
- `HttpOnly = true`
- `SameSite = Lax`
- `SecurePolicy = SameAsRequest` (HTTPS in prod, HTTP allowed in dev)
- Lifetime = **8 hours sliding** — ครบ shift หนึ่งกะ ไม่หลุดกลางทาง
- Server-side invalidation = sign-out clears the cookie

### Login is split into 3 steps

```
Step 1 — POST /Auth/Login
  Email + Password
    │
    ├─ AuthenticateAsync orchestrates:
    │     • lookup tenants for email (master.UserTenantMap, JOIN master.Tenants Active)
    │     • verify password at the user's primary tenant DB
    │     • log to master.LoginAttempts (Success / FailureReason)
    │     • on success: issue pre-auth token (master.PreAuthTokens, 5-min TTL, single-use)
    └─> set short-lived HttpOnly wms.preauth cookie → 302 /Auth/SelectTenant

Step 2 — /Auth/SelectTenant
    │
    ├─ GET: validate wms.preauth → fetch tenant list
    │   ├─ Count == 1 → smart-skip (auto-select)
    │   └─ Count >= 2 → render picker (radio list)
    │
    ├─ POST: re-fetch tenants, confirm posted SelectedTenantId is in user's list
    │
    └─ on success: SignInAsync wms.auth with claims
                     [NameIdentifier, Email, Name, wms.tid]
                   → MarkPreAuthTokenUsed (UsedAt timestamp)
                   → 302 /Auth/SelectWarehouse

Step 3 — /Auth/SelectWarehouse
    │
    ├─ GET: fetch tenant's active warehouses
    │   ├─ Count == 0 → SignOut + render NoWarehouseAccess (defensive)
    │   ├─ Count == 1 → smart-skip
    │   └─ Count >= 2 → render picker
    │
    ├─ POST: re-fetch + confirm selection
    │
    └─ on success: SignInAsync wms.auth carrying every existing claim
                   plus wms.wid → 302 /
```

### Pre-auth token (Step 1 → Step 2 hand-off)

- Persisted in `master.PreAuthTokens` (Token, UserEmail, ExpiresAt, UsedAt, IpAddress)
- Token format = **32 random bytes, base64url** (~43 characters; column reserves 500)
- Lifetime = **5 minutes**
- **Single-use** — `ValidatePreAuthTokenAsync` requires `UsedAt IS NULL`
- Carried via short-lived cookie `wms.preauth` (HttpOnly, Path=`/Auth`, MaxAge 5min)
- `MarkPreAuthTokenUsedAsync` stamps `UsedAt = SYSUTCDATETIME()` exactly when the
  full session cookie is issued in Step 2

### Tenant validation middleware

- Runs after `UseAuthentication`, before `UseAuthorization`
- Reads `wms.tid` claim
- Uses `ITenantStatusReader` (5-min sliding `IMemoryCache`) to check
  `master.Tenants.Status = 'Active'`
- If inactive → `SignOutAsync` + 302 `/Auth/Login`
- Pushes `{TenantId, UserId}` onto Serilog `LogContext` so every log line
  downstream is tagged

### Password hashing — BCrypt with environment-aware cost factor

- Library = `BCrypt.Net-Next`
- Cost = **12 in Production** (~250 ms / hash)
- Cost = **4 in Development** (~5 ms / hash) — dev seeds + integration runs don't crawl
- Test suite uses 4 directly (`AuthServiceTests` constants)
- The bootstrap admin password hash (`ChangeMe!2026`, cost 12) is committed
  in `Migration_20260504_041_SeedAdminUser` and **must be changed on first login**

### Authoritative checks at each step

- Step 2 POST never trusts the posted `SelectedTenantId` — re-fetches the
  user's tenant list and confirms membership before issuing the cookie
- Step 3 POST does the same for `SelectedWarehouseId` against the tenant's
  active warehouse list
- A user disabled in `security.Users` after `master.UserTenantMap` was
  populated → Step 2 detects the inconsistency, clears the cookie, returns
  to login

### What we will NOT do (yet)

❌ **JWT bearer auth** — fights the MPA model; cookies are correct here.
   A separate JWT scheme can be layered later for 3rd-party API consumers
   without touching this flow.

❌ **Lockout policy auto-promote** — the schema has
   `FailedLoginAttempts` + `LockedUntil` columns and `VerifyPasswordAsync`
   already respects an existing lock, but the rule that says "5 failures
   in 15 minutes → set `LockedUntil`" is deferred. Today the counter
   increments and the operator unlocks manually if needed. A follow-up
   chunk picks the threshold + window after the project owner decides.

❌ **`ReturnUrl` forwarding through Steps 1 → 2 → 3** — `LoginViewModel`
   captures it, but it's currently dropped after Step 1. Re-introducing
   it requires either an extra cookie or extending the pre-auth token
   payload; deferred to the auth-polish chunk.

❌ **Timing-attack mitigation** — `UnknownEmail` returns in microseconds
   (no DB hit) while `InvalidPassword` takes ~250 ms (BCrypt). Both
   show the user the same `"Invalid email or password."` message, but
   a network observer can still distinguish. Tightening this needs an
   artificial delay on the unknown-email branch; deferred.

❌ **Per-tenant password divergence** — Step 1 verifies the password at
   the user's *primary* tenant only (IsDefault DESC, then Code ASC).
   This assumes the user's password is identical across every tenant
   they have access to — operators control rollouts. If that assumption
   breaks, Step 1 needs to either ask for the tenant first or try every
   tenant; both are larger reworks.

❌ **2FA / SSO / password policies** — phase 2 concerns.

❌ **`IsLocalUrl` sanitisation on `ReturnUrl`** — comes back when
   ReturnUrl forwarding is reintroduced.

---

## Rationale

### Why cookies (not JWT)?

- MPA + Razor + htmx already round-trip the session via cookies for
  anti-forgery; a parallel JWT is gratuitous.
- Server-side invalidation is built in — JWT requires a separate
  blacklist or short TTL + refresh dance.
- Cookie size is bounded; we keep claims tight (5 entries) to stay well
  under the 4 KB header soft-limit.

### Why 3 steps (not 1, not subdomain)?

| Alternative | Rejected because |
|---|---|
| **Single-step login carrying tenant on URL** (`/login?tenant=DEMO`) | Tenant id leaks into logs, referers, browser history; supports only one tenant per user without UX hacks. |
| **Subdomain tenancy** (`tenant1.wms.local`) | Cert per tenant, DNS coordination, complicates dev/QA, breaks single-cookie-domain assumptions. |
| **Combined login + tenant + warehouse on one screen** | The picker only makes sense after auth; pre-loading every user's tenants → warehouses on the public login page is wasted DB traffic. |

The 3-step flow lets each step own one concern, and **smart-skip** removes
all friction from the common case (one tenant, one warehouse — which is
the bootstrap admin today and most production operators).

### Why pre-auth tokens?

Step 1 happens before the user has any session cookie. To carry "I just
proved this email's password" forward to Step 2, options were:

1. **URL parameter** — leaks into logs, referers, copy-paste paste-bins.
2. **Session cookie** — but full session shouldn't exist until tenant
   is chosen.
3. **Pre-auth token in DB + short cookie** ✓ — chosen.

The token is in `master.*` because Step 1 hasn't picked a tenant yet.
Single-use enforcement (`UsedAt`) makes a leaked token worth at most one
Step 2 attempt within 5 minutes.

### Why BCrypt cost 12 / 4?

- 12 is the common floor for production (~250 ms on commodity hardware).
- Lower than 12 = falling behind GPU-cracking economics.
- 4 in dev because the seed migration writes one admin hash + the
  integration tests round-trip several; the cumulative wait at cost 12
  was 5+ seconds and unnecessary for correctness.

### Why a tenant validation middleware (not just the claim)?

Suspending or deleting a tenant must take effect promptly, even if a
user is mid-session with a still-valid cookie. The middleware does
exactly that without paying a master-DB hit per request — `IMemoryCache`
sliding window covers the common case where the same tenant id repeats.

---

## Consequences

### Positive

✅ Standard ASP.NET cookie auth — well-tested, easy to reason about.
✅ Smart-skip is a real UX win for single-tenant / single-warehouse users
   (no extra clicks to land at `/`).
✅ Authoritative server-side checks at every step — a tampered POST
   never leaks a tenant the user isn't allowed into.
✅ Tenant-suspend → next request → forced sign-out, with at most 5
   minutes of cache drift.
✅ Audit trail in `master.LoginAttempts` for every attempt, success or
   failure.
✅ `LogContext` enrichment means every log line written during an
   authenticated request carries `{TenantId, UserId}` for debugging.

### Negative

⚠️ **3 redirects on first login** — first-login latency is the sum of
   Step 1 + Step 2 + Step 3. The smart-skip path keeps this to two 302s
   (~tens of ms each) for the common case.
   → Mitigation: smart-skip already covers the common case; users with
     multiple tenants/warehouses are inherently slower because they have
     a choice to make.

⚠️ **Cookie re-issued at each step** — every SignInAsync call writes a
   fresh `wms.auth` cookie because the principal can't be mutated in
   place.
   → Mitigation: `CompleteWarehouseSelectionAsync` carries forward
     existing claims and drops any prior `wms.wid` before appending,
     so the cookie doesn't grow unbounded over re-pick scenarios.

⚠️ **Password sync assumption** — Step 1 verifies at the primary tenant
   only. If operators ever let per-tenant passwords diverge, Step 1
   silently locks users out of every tenant whose password is different
   from the primary's.
   → Mitigation: documented in `IAuthService.AuthenticateAsync` comment;
     ADR explicitly forbids divergence.

⚠️ **PreAuthToken row growth** — every login attempt that gets through
   Step 1 inserts a row, used or not. After 5 minutes the row is dead
   weight.
   → Mitigation: `IX_PreAuthTokens_Cleanup` indexes `ExpiresAt` for a
     background sweep; the sweep job itself is deferred to ops chunk.

### Neutral

- `master.LoginAttempts` grows linearly with traffic; partition / archive
  is a future ops concern.
- 5-minute window for tenant-status drift after suspend is the trade-off
  for not hitting master DB on every request.
- Cookie auth ties session lifetime to the cookie — clients that clear
  cookies are signed out immediately; deliberate.

---

## Implementation Notes

### Files (Phase 1 implementation, all delivered in chunks A1–A6)

```
src/WMS.Common/
  Auth/
    ICurrentUser.cs               — claim-backed identity reader
    WmsClaimTypes.cs              — wms.tid / wms.wid claim names
    UserTenantInfo.cs             — DTO for Step 2 picker
    WarehouseInfo.cs              — DTO for Step 3 picker
  Multitenancy/
    ITenantContext.cs             — wraps wms.tid claim
    ITenantConnectionFactory.cs   — tenant-DB SqlConnection factory
    IMasterConnectionFactory.cs   — master-DB SqlConnection factory
    ITenantStatusReader.cs        — Active-status reader, IMemoryCache 5-min

src/WMS.DAL/
  Multitenancy/
    TenantConnectionFactory.cs    — IMemoryCache 5-min sliding
    MasterConnectionFactory.cs
    TenantStatusReader.cs
  Repositories/
    Master/
      IUserTenantMapRepository.cs — Step 1: which tenants for this email
      UserTenantMapRepository.cs
      IWarehouseRepository.cs     — Step 3: which warehouses in this tenant
      WarehouseRepository.cs      (+ factory)
    Security/
      IUserRepository.cs          — security.Users CRUD bounded to a tenant
      UserRepository.cs           (+ factory)

src/WMS.BLL/
  Services/Auth/
    IAuthService.cs               — high-level + primitives
    AuthService.cs
    LoginResult.cs                — Step 1 outcome DTO
    PreAuthData.cs                — Step 2 token payload DTO

src/WMS.Web/
  Auth/
    CurrentUser.cs                — HttpContext-backed ICurrentUser impl
  Multitenancy/
    TenantContext.cs              — claim-backed ITenantContext impl
    TenantValidationMiddleware.cs — runs after UseAuthentication
    TenantValidationMiddlewareExtensions.cs
  Controllers/
    AuthController.cs             — Login / SelectTenant / SelectWarehouse
                                    / Logout / Forbidden
  Models/Auth/
    LoginViewModel.cs
    TenantSelectViewModel.cs
    WarehouseSelectViewModel.cs
  Views/Auth/
    Login.cshtml
    SelectTenant.cshtml
    SelectWarehouse.cshtml
    NoWarehouseAccess.cshtml
    Forbidden.cshtml

tools/WMS.Migrate/
  Master/010_SeedDemoTenant.cs    — DEMO tenant + admin → DEMO map
  Tenant/046_SeedDefaultWarehouse — WH-MAIN
```

### Schema

- **Master DB**: `Tenants`, `UserTenantMap`, `LoginAttempts`,
  `PreAuthTokens`, `SuperAdmins`, `SystemAuditLog`, `SystemSettings`
- **Tenant DB**: `security.Users`, `security.Roles`, `security.UserRoles`,
  `security.Functions`, `security.RoleFunctionPermissions`,
  `security.AuditLog`

Audit FKs (CreatedBy / UpdatedBy) are wired with `ON DELETE NO ACTION`
to enforce soft-delete discipline (CLAUDE.md "Audit Field FK Rules").

### Test surface

- Unit tests: `AuthServiceTests` covers BCrypt round-trip + cost-factor
  guard (4 cases). Orchestration tests for `AuthenticateAsync` are
  deferred to the integration suite — every branch writes to
  `master.LoginAttempts` via Dapper which can't be cleanly mocked.
- Integration tests: `TenantValidationMiddlewareTests` covers all 6
  decision branches via hand-built `DefaultHttpContext`.
- Smoke tests (curl + cookies) verify all 3 steps end-to-end against the
  DEMO tenant + WH-MAIN seeds, including failure paths.

---

## Alternatives Considered

### Alternative 1: JWT bearer on every request

**Rejected because**:
- MPA + Razor doesn't ship Authorization headers from `<form>` posts
  without JS plumbing.
- Server-side invalidation needs a blacklist.
- Anti-forgery is already cookie-based — adding JWT means two parallel
  systems.

### Alternative 2: Subdomain-per-tenant tenancy

**Rejected because**:
- TLS cert per tenant (or wildcard with DNS coordination).
- Local development needs `/etc/hosts` entries per tenant.
- Tenants share no cookie domain, breaking the smart-skip flow.
- Multi-tenant UX (the rare user with access to several) becomes
  multi-domain UX, with no SSO across them.

### Alternative 3: Single-step login (no Step 2 / Step 3)

**Rejected because**:
- Some users have access to multiple tenants (e.g. consultants, super
  admins, support). One step couldn't disambiguate.
- Warehouse context is needed by ~every operational route — putting it
  in cookies is preferable to URL params.

### Alternative 4: Per-route tenant resolution from header / query

**Rejected because**:
- Every form, link, and htmx swap would have to thread tenant id around.
- Easier to leak across-tenant in template bugs.

### Alternative 5: Combined screen (email + tenant picker on one page)

**Rejected because**:
- Pre-loading every email's tenants on the public login form means a
  master DB lookup before authentication — fingerprinting target.

---

## Related ADRs

- ADR-001: Multi-tenant DB per tenant (drives the tenant claim)
- ADR-002: Dapper over EF Core (UserRepository / UserTenantMapRepository)
- ADR-003: MPA + htmx over SPA (drives cookie auth)
- ADR-010: Function-CRUD permission matrix (depends on this flow's
  TenantId / UserId claims)

---

**Last updated**: 2026-05-06
