# ADR-017: SuperAdmin Separate Authentication Scheme

**Status**: Accepted
**Date**: 2026-05-15
**Decision makers**: Project owner
**Implemented in**: Phase 27 (v2.13.0-onboarding)

---

## Context

Phase 27 introduced **SuperAdmin** — a cross-tenant operator identity
that provisions, suspends, and reactivates customer tenants. SuperAdmin
is fundamentally different from a tenant's ADMIN user:

| Attribute | Tenant ADMIN | SuperAdmin |
|---|---|---|
| Scope | One tenant DB | Cross-tenant (master DB) |
| Identity store | `security.Users` per tenant | `master.SuperAdmins` (master DB) |
| Login URL | `/Auth/Login` | `/SuperAdmin/Login` |
| Permissions model | Function-CRUD matrix (ADR-010) | Implicit full access |
| Persona | Customer's IT lead | Platform operator (you, future team) |
| Session length | 8h sliding (operational) | 4h sliding (admin sessions decay faster) |
| Audit log | `security.AuditLog` (tenant DB) | `master.SystemAuditLog` (master DB) |

Three implementation options were considered:

1. **Role-based** — single auth scheme + role check. SuperAdmin is just a tenant ADMIN with a `SuperAdmin` role flag.
2. **Claim-based** — single auth scheme + claim-based gating. SuperAdmin sets a special claim at login.
3. **Separate scheme** — two cookie schemes coexisting (`CookieAuthenticationDefaults` for tenant + `SuperAdminAuth` for platform).

---

## Decision

**Option 3 — Two separate cookie schemes.**

ASP.NET Core supports multiple auth schemes via `.AddCookie(name, opts)`.
We register:

- **Default scheme** (`CookieAuthenticationDefaults.AuthenticationScheme`) — tenant auth, cookie `wms.auth`, 8h sliding, LoginPath `/Auth/Login`
- **`SuperAdminAuth` scheme** — platform auth, cookie `wms.superauth`, 4h sliding, LoginPath `/SuperAdmin/Login`

`RequireSuperAdminAttribute` authenticates against the dedicated scheme
and replaces `HttpContext.User` with the SuperAdmin principal so
downstream code sees the correct identity.

---

## Rationale

### Defence in depth

The two schemes are completely independent:
- Cookies have different names (`wms.auth` vs `wms.superauth`) — browser can present both, server distinguishes
- Cookie compromise on one side doesn't bridge to the other
- SignInAsync / SignOutAsync / AuthenticateAsync all take a scheme name; the wrong scheme is a no-op
- A tenant cookie cannot be elevated to SuperAdmin by claim manipulation (no role check to bypass)

### Clear boundary

The split is visible everywhere in the codebase:
- `master.SuperAdmins` vs `security.Users` (different tables, different DBs)
- `_SuperAdminLayout.cshtml` (dark slate + red brand) vs `_Layout.cshtml` (light + purple)
- `/SuperAdmin/*` routes vs `/*` routes
- `[RequireSuperAdmin]` vs `[Authorize]` filters

Anyone reading the code knows which persona they're dealing with.

### Per-persona policy

Different sliding-expiration windows reflect different threat models:
- Tenant ADMIN session: 8h to cover a full warehouse shift without timeout
- SuperAdmin session: 4h to ensure platform operators re-authenticate more often

Future expansion of either persona's policy is independent.

### Operational separation

Audit trails live in different DBs:
- Tenant ADMIN actions → `security.AuditLog` in that tenant's DB (operator + customer can both inspect)
- SuperAdmin actions → `master.SystemAuditLog` in master DB (only platform team can inspect)

This matches who can read what: customers can see their own tenant audit, can't see platform actions; platform team sees everything via master DB.

---

## Consequences

### Positive
- Strong isolation — no role-claim escape hatch from tenant to SuperAdmin
- Clearer code — controllers signal their persona via filter choice
- Independent policy evolution — change tenant cookie lifetime without touching SuperAdmin
- Audit isolation — master vs tenant audit logs reflect natural concern boundaries

### Negative
- Two cookie schemes to maintain (DI registration, filter code, scheme constants)
- Type alias needed in `WMS.BLL.Services.SuperAdmin.SuperAdminAuthService` because the BLL namespace collides with `WMS.Domain.Entities.Master.SuperAdmin` entity (`using SuperAdminEntity = ...;`)
- `RequireSuperAdminAttribute` must replace `HttpContext.User` post-auth — easy to forget, would silently use the wrong principal
- Two layouts (`_Layout` + `_SuperAdminLayout`) to keep visually distinct as the design system evolves

### Operational implications

- **SuperAdmin bootstrap**: config-driven first-run seed (`SuperAdminBootstrap.EnsureAsync`). NEVER overwrites password hash on subsequent runs. Set `MustChangePassword=true` on seed so first login forces rotation.
- **SuperAdmin lockout recovery**: direct DB UPDATE on `master.SuperAdmins` (Section 3 of runbook.md) — no UI recovery path today (TD-088).
- **Multiple SuperAdmins**: manual DB insert today; UI add path is TD-088.

---

## When to revisit

Re-open if:

1. A **third persona** needs auth (e.g. read-only auditor for SOC2). Three cookie schemes is fine; four+ starts to smell — consider a more general auth-policy framework.
2. **SuperAdmin actions need to delegate** to per-permission gating (today: implicit full access). May want to layer a permission matrix on top of the scheme.
3. **Customer-facing SuperAdmin** (e.g. a "super user" role within a tenant who can manage their own tenant fully but not cross-tenant). This is closer to a tenant-side admin elevation than a new scheme.

For now, the two-scheme model is the smallest move that gives correct isolation.

---

## Related ADRs

- [ADR-008 — 3-Step Login Flow](./ADR-008_3_Step_Login_Flow.md)
- [ADR-010 — Function-CRUD Permission Matrix](./ADR-010_Function_CRUD_Permission_Matrix.md)
- [ADR-015 — Land + Expand GTM Strategy](./ADR-015_Land_Expand_GTM_Strategy.md)
