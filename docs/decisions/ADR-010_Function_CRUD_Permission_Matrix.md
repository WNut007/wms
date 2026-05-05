# ADR-010: Function-CRUD permission matrix with role aggregation and 15-min cache

**Status**: Accepted
**Date**: 2026-05-06
**Decision makers**: Project owner

---

## Context

WMS ครอบคลุมหลาย module (Inbound, Inventory, Outbound, Counts, Billing,
Master, Config, Reports). แต่ละ module มีหลาย operational function
(เช่น Inbound → Purchase Orders / Putaway, Inventory → Stock / Adjustments
/ Lots / Pallets / Transfers).

ผู้ใช้แต่ละคนมี role อย่างน้อยหนึ่ง — บางคนหลาย role:

- `ADMIN` — เข้าได้ทุกที่
- `MANAGER` — เห็น operational dashboards + อนุมัติ
- `PICKER` — รับงาน pick, ไม่แตะ master
- `PACKER` — pack, weigh, label
- ในอนาคตอาจมี `RECEIVER`, `AUDITOR`, `BILLING_OPS`, ฯลฯ

แต่ "เข้าได้/ไม่ได้" ไม่พอ — ต้องระบุ **action** ด้วย:

- `PICKER` ดู (`View`) Stock ได้, แต่ปรับ (`Edit`) ไม่ได้
- `MANAGER` ดู / เพิ่ม / แก้ Adjustment ได้, แต่ **อนุมัติ** (`Approve`)
  เป็นสิทธิ์แยกต่างหาก
- `BILLING_OPS` ดู Invoice ได้แต่ลบ (`Delete`) ไม่ได้

Constraints:

- ทุก authenticated request อาจต้อง check permission อย่างน้อยหนึ่งครั้ง
  (action filter + view helper) → ไม่สามารถ hit DB ทุก request
- Multi-tenant → permission model ต้อง scope ต่อ tenant
- คน-คนหนึ่งอาจมีหลาย role → ต้อง aggregate
- Phase 1 ไม่ต้องการ user-level grant/deny override — role-level พอ

Non-goals (Phase 1):

- ไม่ต้องการ row-level / warehouse-level RBAC ใน chunk นี้ — ดูทุก
  warehouse ใน tenant ที่เลือกถ้ามี Function permission
- ไม่ต้องการ time-bound permissions
- ไม่ต้องการ permission inheritance / role hierarchies
- ไม่ต้องการ admin UI สำหรับแก้ permission — defer

---

## Decision

### Schema (tenant DB, schema = `security`)

```
security.Functions
  Id         UNIQUEIDENTIFIER PK
  Code       VARCHAR(50)  UNIQUE  — dotted, e.g. "INVENTORY.STOCK"
  Name       NVARCHAR(100)
  Module     VARCHAR(50)          — menu grouping
  Description, DisplayOrder, IsActive

security.Roles
  Id         UNIQUEIDENTIFIER PK
  Code       VARCHAR(20)  UNIQUE  — ADMIN / PICKER / PACKER / MANAGER
  Name, IsSystem, IsActive

security.UserRoles            (m:n)
  UserId, RoleId — FK to Users / Roles

security.RoleFunctionPermissions
  RoleId, FunctionId          — composite, UNIQUE (RoleId, FunctionId)
  CanView, CanAdd, CanEdit,
  CanDelete, CanApprove       — BIT NOT NULL DEFAULT 0
```

**Function code = dotted form (`MODULE.SUB`)** — module prefix doubles as
the menu grouping and as a coarse permission filter. 30 functions seeded
in Phase 1 (`Migration_20260504_042_SeedDefaultFunctions`).

**5 action flags** mirror the user-facing CRUD verbs:
`View / Add / Edit / Delete / Approve`. `Approve` is separated from
`Edit` because approval is structurally a different operation
(MANAGER may `Edit` an Adjustment but not `Approve` it).

**All flags default to 0** — permission must be granted, never assumed.

### Resolution semantics

Permission for `(userId, functionCode, action)` =
**any role the user holds** that has the corresponding `Can*` flag set
to 1.

Resolved in **one master DB JOIN** per (userId, tenantId):

```sql
SELECT f.Code AS FunctionCode,
       MAX(CASE WHEN rfp.CanView    = 1 THEN 1 ELSE 0 END) AS CanView,
       MAX(CASE WHEN rfp.CanAdd     = 1 THEN 1 ELSE 0 END) AS CanAdd,
       MAX(CASE WHEN rfp.CanEdit    = 1 THEN 1 ELSE 0 END) AS CanEdit,
       MAX(CASE WHEN rfp.CanDelete  = 1 THEN 1 ELSE 0 END) AS CanDelete,
       MAX(CASE WHEN rfp.CanApprove = 1 THEN 1 ELSE 0 END) AS CanApprove
FROM security.UserRoles ur
JOIN security.RoleFunctionPermissions rfp ON rfp.RoleId = ur.RoleId
JOIN security.Functions f                  ON f.Id      = rfp.FunctionId
WHERE ur.UserId = @userId
  AND f.IsActive = 1
GROUP BY f.Code
```

`MAX(...)` over BIT effectively does OR-aggregate across roles. The
service layer expands one row → up to five `UserPermission` tuples
`(FunctionCode, Action)`.

Inactive functions (`f.IsActive = 0`) are excluded so a function can be
disabled centrally without rewriting row-level grants.

### Caching — `IMemoryCache`, 15-minute sliding

- Cache key = `perms:{tenantId:N}:{userId:N}` (tenant prefix prevents
  cross-tenant collision when the same user email lives in multiple
  tenant DBs)
- Sliding 15 minutes — ผู้ใช้ active ก็ stay warm; idle eventually
  evicts and re-fetches
- IMemoryCache is process-local — phase 1 ไม่ต้อง Redis
- 30 functions × 5 actions = max 150 `UserPermission` rows / user →
  trivial memory cost
- `HasPermissionAsync` does a linear scan over the cached list —
  indexed lookup wouldn't pay off at this size

### Filter — `[RequirePermission(functionCode, action)]`

```csharp
[RequirePermission("INVENTORY.STOCK", PermissionAction.View)]
public async Task<IActionResult> Permissions(...) { ... }
```

`RequirePermissionAttribute` is an `IAsyncAuthorizationFilter`. It
resolves `IPermissionService` from `HttpContext.RequestServices` so the
attribute literal stays free of constructor injection.

Decision matrix:

| Request state | Result | Effective behavior (cookie scheme) |
|---|---|---|
| Anonymous | `ChallengeResult` | redirect → `LoginPath` (`/Auth/Login?ReturnUrl=...`) |
| Authenticated, missing `NameIdentifier` or `wms.tid` | `ForbidResult` | redirect → `AccessDeniedPath` (`/Auth/Forbidden`) |
| Authenticated, no permission | `ForbidResult` | redirect → `/Auth/Forbidden` |
| Authenticated, has permission | `null` (pass-through) | action runs |

Use the constants on `PermissionAction` (`View / Add / Edit / Delete /
Approve`) for the action argument so typos surface at compile time.

### What we will NOT do (yet)

❌ **User-level grant / deny overrides** — schema doesn't have
   `UserFunctionPermissions` and Phase 1 has no business case requiring
   it. When the case appears, the override table layers on top of the
   role aggregation as `MAX(role) → override_grant → override_deny`.

❌ **Cache invalidation on role change** — admin UI ที่แก้
   `UserRoles` / `RoleFunctionPermissions` ยังไม่มี (admin chunk).
   เมื่อมาถึง chunk นั้น invalidation มีสองทางเลือก: ลบ cache key
   ตรงๆ (single-process) หรือ pub/sub broadcast (multi-process). Phase
   1 single-instance → ลบ key ตรงๆ ก็พอ.

❌ **Permission inheritance / role hierarchies** — โมเดลปัจจุบันแบน
   (flat). ถ้า MANAGER ต้องสืบทอด PICKER ก็ assign ทั้งสอง role ให้ user.

❌ **Time-bound permissions** (e.g. "Auditor can read for 30 days") — ไม่มี
   business case Phase 1.

❌ **Row-level / warehouse-level RBAC** — มี permission ที่ Function
   ก็เข้าทุก warehouse ใน tenant. Warehouse-scoped permissions รอ phase
   ที่ supplier/3PL onboard.

❌ **`Manage` / `Configure` actions** — five flags ครอบคลุม CRUD + approval
   พอใช้ Phase 1; functions ที่ต้อง configure (e.g. system settings) ใช้
   `Edit` flag ไปก่อน.

❌ **Distributed cache (Redis)** — single-instance deployment Phase 1
   ใช้ `IMemoryCache` พอ. ตอน scale-out จะเปลี่ยนเป็น
   `IDistributedCache` + Redis โดยไม่กระทบ public API ของ
   `IPermissionService`.

---

## Rationale

### Why a `(Function × Action)` matrix (not single permission strings)?

Single-string approach ที่เคยพิจารณา: `"INVENTORY.STOCK.View"`,
`"INVENTORY.STOCK.Add"` ฯลฯ — รวม module + function + action เป็น
string เดียว.

ปฏิเสธเพราะ:

- ไม่ greppable เวลา grant/revoke (ต้องลบ 5 rows ด้วยมือเพื่อปิด function)
- Schema-as-data: dotted code + 5 BIT columns บ่งชี้ explicit ว่ามี
  CRUD พื้นฐาน 5 ตัว ตรงตาม UI patterns
- Aggregation across roles ทำได้ใน SQL ตรง ๆ ด้วย `MAX(BIT)`; ถ้าเป็น
  string ต้อง group + lookup table

### Why `MAX`-aggregate (not configurable AND/OR)?

Industry standard for role-based ACL: "any role that grants" wins. ถ้า
ต้องการ deny-by-explicit-rule ก็ต่อ user-level deny override ทีหลัง
(future). Phase 1 ที่ deny rules ไม่จำเป็น.

### Why 15-minute sliding cache (not per-request, not session-bound)?

- **Per-request**: 30-row JOIN per request × 5,000 orders/day × ~10
  page loads/order = 150 K joins/day. Trivial in absolute terms but
  unnecessary; a 15-min window cuts it by ~99%.
- **Session-bound** (claims in cookie): cookie size grows linearly with
  permissions; 150 claims would push the cookie to ~10 KB, near the
  4 KB header limit and dominating every request size.
- **15-min sliding**: cache stays warm for active users (most requests
  cluster within 15 min of each other); idle users re-fetch — which is
  exactly the scenario where a recent role change should appear.

### Why `IMemoryCache` (not `IDistributedCache`)?

Phase 1 = single instance behind one IIS / Kestrel. `IDistributedCache`
adds Redis dependency for zero benefit at scale-of-one. Switching is a
one-class change to `PermissionService` if/when scale demands it.

### Why expand to flat `UserPermission` list (not keep aggregated row)?

Caller code ที่จะ check `HasPermission(function, action)` อ่านง่ายกว่ามาก
ถ้า structure ที่ cache ไว้คือ list of `(FunctionCode, Action)` tuples
แทนที่จะเป็น row พร้อม 5 flags. Linear scan ที่ ≤150 elements ราคาเท่า
indexed lookup จริงๆ.

---

## Consequences

### Positive

✅ **Explicit grants** — flags default to 0, ไม่มี implicit access
   "เพราะ role ยังไม่กำหนด"
✅ **Fast** — first call hits master DB once; subsequent 15 minutes
   serve from memory
✅ **Readable** — `[RequirePermission("INVENTORY.STOCK", "View")]`
   อ่านออกเลย, ไม่ต้องเปิด lookup table
✅ **Testable** — `IPermissionService` มอ่ค mockได้, filter ก็ทดสอบผ่าน
   `DefaultHttpContext` ได้
✅ **Composable** — ผู้ใช้หลาย role ได้ "union" ของ permissions
   อัตโนมัติ ไม่ต้อง code special

### Negative

⚠️ **Cache staleness window** — admin grant/revoke ไม่ effective ทันที;
   worst case 15 นาที. ผู้ใช้ที่อยู่กลาง shift จะใช้ permissions เดิม
   ต่อจนกว่า cache จะ expire.
   → Mitigation: chunk admin จะ invalidate ตรงๆ ตอน mutation. จนกว่า
     จะถึง, 15-min ยอมรับได้ (changes ไม่บ่อย).

⚠️ **No deny rules** — โมเดลปัจจุบัน "any role grants → user has it".
   ถ้า MANAGER มี Approve แล้ว assign ให้ PICKER เพิ่ม → PICKER ก็ Approve
   ได้ด้วย (เพราะ MANAGER role).
   → Mitigation: นี่คือ standard semantic (ไม่ใช่ bug). Deny override
     rolls in กับ user-level chunk ในอนาคต.

⚠️ **Tenant-scoped cache key** — ถ้า scaled-out หลาย instance, instance
   หนึ่งบ recompute key A พอ recompute หลังจาก grant เปลี่ยน, instance
   อื่นยังถือ stale 15-min ต่อ.
   → Mitigation: phase 1 single-instance; phase scale-out ใช้
     `IDistributedCache`.

⚠️ **5 flags hardcoded ใน schema** — ถ้าจะเพิ่ม action ที่ 6 (`Manage`,
   `Configure`, `Print`, ...) ต้อง schema migration + update SQL +
   update `PermissionAction` const + update repo expansion loop.
   → Mitigation: Five = enough Phase 1; ADR-013 (Adjustment) + 014
     (Transfer) approval flows already use `Approve` separately from
     `Edit`. Adding a 6th มีต้นทุนแต่ไม่ปวดหัว.

### Neutral

- Cache memory cost = ~150 records × ~80 bytes per `UserPermission`
  ≈ 12 KB / user. ที่ 100 active users = ~1 MB. ไม่กระทบ.
- Master DB read on cache miss = 1 indexed JOIN. < 5 ms.
- `f.IsActive = 0` ใน WHERE clause = function disable ทำได้ตรงๆ
  ไม่ต้องลบ permission rows.

---

## Implementation Notes

### Files (Phase 1, delivered in chunk A7)

```
src/WMS.Common/Auth/
  PermissionAction.cs           — const class { View, Add, Edit, Delete, Approve }
  UserPermission.cs             — record (FunctionCode, Action)

src/WMS.DAL/Repositories/Security/
  IPermissionRepository.cs
  PermissionRepository.cs       — single SQL with MAX-aggregate, expansion loop
  IPermissionRepositoryFactory.cs
  PermissionRepositoryFactory.cs

src/WMS.BLL/Services/Auth/
  IPermissionService.cs
  PermissionService.cs          — IMemoryCache wrapper, 15-min sliding

src/WMS.Web/Filters/
  RequirePermissionAttribute.cs

tools/WMS.Migrate/Tenant/
  036_CreateFunctionsTable.cs
  037_CreateRoleFunctionPermissionsTable.cs
  038_CreateUserRolesTable.cs
  040_SeedDefaultRoles.cs        — ADMIN / PICKER / PACKER / MANAGER
  042_SeedDefaultFunctions.cs    — 30 functions
  043_GrantAdminAllPermissions.cs
  044_GrantBaselineRolePermissions.cs
```

### DI registration

```csharp
builder.Services.AddScoped<IPermissionRepositoryFactory, PermissionRepositoryFactory>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
```

`PermissionService` is **Scoped** despite owning a reference to the
Singleton `IMemoryCache` — the captured Scoped factory dep would
otherwise be a captive dependency under DI scope validation.
Per-request instances are cheap; the cache itself outlives them.

### Test surface

- Unit tests (`PermissionServiceTests`, 5 cases):
  - Permission granted → true
  - Action mismatch → false (no cross-action leak)
  - Function missing → false
  - Second call serves from cache (strict factory mock proves repo is
    called exactly once)
  - Different tenants don't collide on cache key
- Integration tests (`RequirePermissionAttributeTests`, 4 cases):
  - Anonymous → `ChallengeResult`
  - Auth + missing claims → `ForbidResult`
  - Auth + permission granted → `null` (pass-through)
  - Auth + permission denied → `ForbidResult`

### Smoke surface

`/Home/Permissions` is guarded by
`[RequirePermission("INVENTORY.STOCK", "View")]` and renders the
caller's full resolved permission list. Bootstrap admin holds every
permission (migration 043) so the endpoint is reachable as soon as
login completes — useful as an end-to-end smoke target without seeding
a second role.

---

## Alternatives Considered

### Alternative 1: Claim-based permissions (carried in cookie)

**Rejected because**:
- Cookie size: 150 permission rows ≈ 10 KB cookie, dominating every
  request header.
- Invalidation requires re-issuing the cookie — admin grant change
  doesn't take effect until next login.
- Cookie tampering surface — even with signing, the data is fully on
  the client.

### Alternative 2: Per-resource ACL (object-level permissions)

**Rejected because**:
- Phase 1 has only menu-level RBAC; row-level means a permission row
  per (User, Function, ResourceId) — explosion.
- Reasoning about effective access becomes a graph problem.
- Over-engineered for the problem we have.

### Alternative 3: Single permission strings ("INVENTORY.STOCK.View")

**Rejected because**:
- Bulk grant ("PICKER can view all Inbound functions") needs a wildcard
  syntax, which is its own grammar.
- Loses the structural separation between function (which capability)
  and action (what verb on it).

### Alternative 4: External authorization service (Casbin / OpenFGA / Authzed)

**Rejected because**:
- Phase 1 doesn't need cross-service authorization.
- Adds infrastructure (or SaaS dependency).
- Cache + 30-row JOIN is already fast enough.

### Alternative 5: Hard-coded role checks in code (`if (User.IsInRole("PICKER"))`)

**Rejected because**:
- Cannot grant fine-grained capability (Stock.View vs Stock.Edit).
- Cannot rebalance permissions without redeploy.
- Audit story is poor.

---

## Related ADRs

- ADR-001: Multi-tenant DB per tenant (drives tenant-scoped cache key)
- ADR-002: Dapper over EF Core (PermissionRepository)
- ADR-005: Strategy pattern (independent — different concern)
- ADR-008: 3-step login flow (provides UserId + TenantId claims that
  this matrix consumes)

---

**Last updated**: 2026-05-06
