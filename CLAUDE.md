# CLAUDE.md

> 📌 **Read this file FIRST before any work in this codebase.**

This is the WMS (Warehouse Management System) rebuild project. This file is your north star.

---

## 🎯 Project Overview

**System**: Warehouse Management System for B2B + B2C Marketplace + 3PL/SaaS
**Volume target**: 5,000+ B2C orders/day
**Architecture**: Multi-tenant (DB per tenant), 5-layer architecture
**Tech stack**: .NET Core MVC + Dapper + SQL Server + Telerik + htmx + SignalR

### UI Stack

**Framework**: Tabler.io (Free, MIT) — Bootstrap 5 base
**Icons**: Tabler Icons (5800+ outline) — `<i class="ti ti-{name}"></i>`
**Charts**: ApexCharts (Phase 2)
**Date picker**: Flatpickr (Phase 2)
**Select**: Choices.js (Phase 2)
**Grids**: Tabulator (Phase 2 — for complex grids only)
**Interactivity**: HTMX 2.0 + Alpine 3.14

**Design system**: `docs/UI_DESIGN_REFERENCE.md` (LOCKED v1.0)
**Custom CSS**: `wwwroot/css/wms-custom.css` (loaded AFTER Tabler)

**Color tokens**:
- Primary: `#5D4FA0` → `#7B5DBF` (purple gradient — sidebar)
- Hover: `#4F46E5` (Indigo — primary buttons)
- Hero: `#312E81` (Indigo dark — login)
- Status: green / amber / red / blue / gray (semantic)

**Typography**: Inter throughout, JetBrains Mono for codes

**Layouts**: `_OfficeLayout` uses bespoke `.wms-app` shell (purple sidebar + white topbar).
`_AuthLayout` is a minimal hero-friendly shell. `_MobileLayout` keeps its bespoke compact shell.

**Delivery**: CDN today (Phase 1 internal dev). Move to local files (libman/npm) before production.

**Deferred** (Phase 2): Kendo UI, SmartAdmin Extended

**13 mockups approved** — see `docs/UI_DESIGN_REFERENCE.md`.

**Read these documents before starting**:
- `docs/01_Master_Design.md` — System architecture overview
- `docs/02_Database_Schema.md` — All tables and relationships
- `docs/03_Roadmap.md` — Implementation phases
- `docs/04_Quick_Reference.md` — Decision cheatsheet

---

## 🏗️ Architecture Rules (NEVER VIOLATE)

### 1. Multi-Tenant Always

Every data query MUST filter by tenant. Failing to do so is a security incident.

```csharp
// ✅ CORRECT
public async Task<List<Stock>> GetStocksAsync(Guid tenantId, Guid productId)
{
    using var conn = _tenantDbFactory.GetConnection(tenantId);
    return await conn.QueryAsync<Stock>(
        "SELECT * FROM inventory.Stock WHERE ProductId = @p", 
        new { p = productId });
}

// ❌ WRONG - no tenant context
public async Task<List<Stock>> GetStocksAsync(Guid productId)
{
    // Cross-tenant data leak risk!
}
```

### 2. Owner-Aware Stock

Stock is keyed by (LocationId, ProductId, LotId, PalletId, OwnerId, UomId). 
Same SKU from different owners is DIFFERENT stock.

### 3. Use Dapper, NOT EF Core

This project uses Dapper for performance. Do not introduce EF Core.

```csharp
// ✅ CORRECT
var stocks = await conn.QueryAsync<Stock>(
    @"SELECT s.*, p.Name as ProductName 
      FROM inventory.Stock s
      JOIN master.Products p ON s.ProductId = p.Id
      WHERE s.WarehouseId = @w", new { w = warehouseId });

// ❌ WRONG - don't add EF Core
var stocks = await _context.Stocks.Where(...).ToListAsync();
```

### 4. Strategy Pattern for Configurable Behaviors

Allocation, rotation, putaway, picking, pricing — all use strategy pattern.
NEVER hardcode FIFO/FEFO/Push/Pull. Always resolve from configuration.

```csharp
// ✅ CORRECT
var strategy = await _strategyResolver.ResolveAsync(context);
var stocks = strategy.Apply(candidateStocks);

// ❌ WRONG - hardcoded
var stocks = candidateStocks.OrderBy(s => s.ExpiryDate).ToList(); // FEFO hardcoded
```

### 5. Frontend: MPA + htmx, NOT SPA

Server-rendered Razor pages. htmx for partial updates. Alpine.js for client state.
DO NOT introduce React/Vue/Angular.

### 6. Mobile: PWA, NOT Native

3 mobile workflows (Receiver, Picker, Packer) are PWA apps with separate manifests.
Use Bootstrap 5 + Alpine + htmx (NO Kendo on mobile).

---

## 🔐 Audit Field FK Rules (Important!)

`CreatedBy` / `UpdatedBy` columns on operational tables are FK-constrained:

- **Tenant DB** → `security.Users(Id)` — `ON DELETE NO ACTION`
- **Master DB** → `master.SuperAdmins(Id)` — `ON DELETE NO ACTION`

Passing a random or non-existent Guid throws an FK violation at INSERT/UPDATE.
NO ACTION blocks hard-deletion of any user that has audit rows pointing at them.

### When writing Services

**✅ DO — pass a valid User Guid:**

```csharp
// From ICurrentUser (HttpContext-scoped)
await _service.SaveAsync(entity, _currentUser.UserId);

// From background-job context (Hangfire)
await _service.SaveAsync(entity, jobContext.TriggeredByUserId);
```

**✅ DO — pass NULL for true system actions:**

```csharp
// Migration seeds, public-API writes, jobs without user context
await _service.SaveAsync(entity, createdBy: null);
```

**❌ DON'T — pass a fabricated Guid:**

```csharp
entity.CreatedBy = Guid.NewGuid();   // orphan — FK violation
entity.CreatedBy = someRandomGuid;   // FK violation if not in security.Users
```

**❌ DON'T — hard-delete users with audit history:**

```csharp
// Will fail: ON DELETE NO ACTION blocks
await conn.ExecuteAsync("DELETE FROM security.Users WHERE Id = @id", ...);

// Use soft-delete instead
await conn.ExecuteAsync(
    "UPDATE security.Users SET IsActive = 0 WHERE Id = @id", ...);
```

### BaseService Pattern (recommended)

Centralize audit stamping so individual services can't forget — and can't
accidentally invent a Guid:

```csharp
public abstract class BaseService<T> where T : BaseEntity
{
    protected readonly ICurrentUser _currentUser;

    protected void StampCreate(T entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = _currentUser?.UserId;  // null is OK
    }

    protected void StampUpdate(T entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser?.UserId;
    }
}
```

Skipped tables (no FK — see migration headers for rationale):
`master.SuperAdmins`, `master.LoginAttempts`, `master.SystemAuditLog`,
`master.PreAuthTokens`, `master.SystemSettings`, all `security.*` tables.

---

## 📂 Code Organization

### Naming Conventions

```
- Controllers: PascalCase, end in "Controller" (e.g., StockController)
- Services: I{Name}Service interface + {Name}Service class
- Repositories: I{Entity}Repository + {Entity}Repository
- Models: PascalCase
- Database: schema.TableName (e.g., inventory.Stock)
- SQL parameters: @camelCase
```

### Folder Structure (per project)

```
WMS.BLL/
├── Services/
│   ├── Stock/
│   │   ├── IStockService.cs
│   │   ├── StockService.cs
│   │   └── Tests/  (or in WMS.UnitTests)
│   ├── Order/
│   ├── Pick/
│   └── ...
├── Strategies/
│   ├── Allocation/
│   ├── Rotation/
│   └── Pricing/
└── Validators/
```

### File Limit Guideline
- Service files: max 300 lines
- Controllers: max 200 lines
- Models: max 100 lines per class
- Big files = split into partials or composition

---

## 🔧 Development Workflow

### Branch Strategy
```
main → release branch (deploys to production)
develop → integration branch
feature/* → from develop
hotfix/* → from main
```

### Commit Messages
```
feat(picker): add 4-tier scan validation
fix(billing): correct aging bracket calc
refactor(stock): extract reservation logic
test(allocation): add strategy resolver tests
docs(adr): document putaway template decision
```

### PR Requirements
1. Migration script (if DB changes)
2. Unit tests for new logic
3. Integration test for critical paths
4. CLAUDE.md updated (if architectural change)
5. ADR document (if new pattern introduced)

---

## 🧪 Testing Strategy

### Unit Tests (xUnit)
- Test business logic in services
- Mock repositories with Moq
- Aim for 70%+ coverage on BLL layer

### Integration Tests
- Use Testcontainers for SQL Server
- Test critical paths end-to-end:
  - Receive → Putaway → Stock
  - Order → Allocate → Pick → Pack → Ship
  - Billable activities → Invoice
- Use real database, real Dapper

### Manual Testing
- Each phase gate: full UAT
- Mobile testing on actual scanners
- Performance: load test before launch

---

## 🚨 Critical Behaviors

### When Adding a New Feature

1. **Read relevant section in design docs**
2. **Find similar patterns in existing code**
3. **Write tests first (or alongside)**
4. **Follow existing naming + folder conventions**
5. **Update CLAUDE.md if introducing pattern**

### When Modifying Existing Code

1. **Understand the WHY** before changing
2. **Check git blame** for context
3. **Run existing tests** before/after
4. **Don't break public API** without coordination

### When Stuck

1. **Re-read design docs** — answer might be there
2. **Check `docs/decisions/`** — ADRs explain past choices
3. **Ask user** — don't guess on architecture
4. **Spike in branch** — prove concept before integrating

---

## 🎯 Current Phase

**Active Sprint**: Phase 6A — Stock Movement Log shipped (v0.6.0-movement-log)
**Current Focus**: Phase 6B (Real Master Data + Activity tab wiring)
**Blockers**: none

Update this section weekly during standups.

### Day 5 — UI Phase 2 (Dashboard)

**Branch**: `feat/dashboard-impl` → merged to `main`

Components:
- `HomeController.Index` + `DashboardViewModel` (mock data)
- Dashboard view (`Views/Home/Index.cshtml`)
- Live Feeds area chart (ApexCharts)
- 4 progress bars
- 4 metric cards (donut + sparkline + chips)
- Mini KPI stats top-right

Tech: ApexCharts 3.45.2 via CDN.

Mock data is hard-coded. Real queries + SignalR live updates planned for Phase 3.

### Day 5 — UI Phase 3 (List Page)

**Branch**: `feat/list-page-impl` → merged to `main`

Components:
- `MockWarehouseDataService` (50 seeded warehouses)
- `WarehousesController` (Index + Data JSON endpoint)
- `Views/Warehouses/Index.cshtml`
- List view (Tabler-style table with sort)
- Grid view (cards with hover effects)
- View toggle (localStorage persistence)
- Filter bar (search + status + region + type with chip rotation)
- Bulk action toolbar
- Server-side pagination
- Empty / loading state patterns
- Sidebar Master Data sub-menu (Warehouses wired; Products / Customers placeholders)

Pattern reusable for: Products, Customers, Carriers, Channels, Order Sources, etc.

Mock data hard-coded. To be replaced with real queries in Phase 4.

### Day 5/6 — UI Phase 4 (Master Detail + Document Pattern)

**Branch**: `feat/master-detail-impl` → merged to `main`

Components:
- `IDocumentStorageService` interface (storage abstraction — Phase 5+ swaps in real impl)
- `MockDocumentStorageService` (in-memory store, seeded for Products / Warehouses / Customers)
- `DetailPageViewModel` (shared, lives under `ViewModels/Detail/`)
- `Views/Shared/_DetailLayout.cshtml` (universal Detail page layout)
- 4 tab partials: `_OverviewPanel`, `_ImagesPanel`, `_DocumentsPanel`, `_ActivityPanel`
- `MockProductDataService` (50 seeded products) + `ProductsController` (list + Detail + SVG placeholder image)
- `MockCustomerDataService` (40 seeded customers) + `CustomersController` (list + Detail)
- `WarehousesController.Detail` (re-uses shared layout)
- Sidebar Master Data: Products + Customers wired to live pages
- List rows + grid cards on all 3 entities navigate to `/{Entity}/Detail/{id}`
- `Services/RelativeTime.cs` shared formatter

Storage strategy:
- Hybrid (Local FS + DB metadata + interface) chosen
- Mock implementation in Phase 4 — controllers code to `IDocumentStorageService`
- Real `LocalFileStorageService` planned for Phase 5+
- Cloud (Azure Blob) optional later via the same interface

Pattern proven:
- Detail page layout reused across 3 entities (`Views/Shared/_DetailLayout.cshtml`)
- `ShowImagesTab` flag controls Images-tab visibility (true for Products only)
- Mock storage seeded; works for upload-UI testing without disk writes
- Reusable for: Carriers, Channels, Order Sources, etc.

Out of scope (Phase 5+): real file upload endpoint, disk writes, lightbox,
drag-to-reorder, document download, real activity log, edit forms.

### Day 6 — UI Phase 5 (Real File Storage + DB Schema)

**Branch**: `feat/real-storage` → merged to `main` · **Tag**: `v0.5.0-real-storage`

Components:
- `DocumentStorageOptions` — bound from `Storage` section (Provider, Local.RootPath, MaxFileSizeMB, AllowedExtensions)
- Migration `20260508001` — `documents.Files` table + schema; FK `CreatedBy/UpdatedBy → security.Users` (NO ACTION); filtered index `IX_Files_Entity` on (EntityType, EntityId, CreatedAt DESC) WHERE IsArchived = 0
- `DocumentFile` domain entity (`WMS.Domain.Entities.Documents`)
- `IDocumentRepository` + `DocumentRepository` (Dapper) + `DocumentRepositoryFactory` (uses `ITenantConnectionFactory`)
- `LocalFileStorageService` implements `IDocumentStorageService` against the local filesystem
- `DocumentsController` — `POST /Documents/Upload`, `GET /Documents/Download/{id}`, `DELETE /Documents/{id}`, `GET /Documents/List?entityType=&entityId=`
- `_DocumentsPanel` rewritten — drag/drop dropzone, fetch-based upload/delete, client-side search, refresh-after-mutate

Storage layout:
- Disk path: `{RootPath}/{tenantId:N}/{entityType}/{entityId}/{fileId:N}{ext}`
- StorageKey persisted relative to RootPath so RootPath moves don't break references
- Original filename preserved on metadata for `Content-Disposition`; on-disk filename is `{Guid}{ext}` (collision-free)
- Path traversal defence: each segment sanitised + final resolved path must remain under RootPath

Validation rules (LocalFileStorageService.UploadAsync, before bytes hit disk):
- Extension lower-cased + must be in `AllowedExtensions`
- Size ≤ `MaxFileSizeMB × 1024²` (re-checked post-write for non-seekable streams)
- Failed metadata insert rolls back the disk write (orphan-free)

DI swap (Program.cs):
- `Storage:Provider == "Mock"` → keeps `MockDocumentStorageService` (handy for tests not wiring SQL)
- Anything else → `LocalFileStorageService` (Scoped — captures `ITenantContext`)

Out of scope (Phase 6+): virus scan, EXIF strip / image re-encode, soft-delete via `IsArchived`, lightbox + drag-to-reorder for Images tab, signed download URLs, Azure Blob / S3 providers (drop-in via the same interface).

### Day 6 — Phase 6A (Stock Movement Log)

**Branch**: `feat/movement-log-impl` → merged to `main` · **Tag**: `v0.6.0-movement-log` · **ADR**: ADR-014

Components:
- Migration `20260508002` — `inventory.StockMovements`. `StockId`-FK'd, signed `QuantityDelta`,
  no `TenantId` (DB-per-tenant per ADR-001), CHECK on the closed `MovementType` enum
  (`Receive/Putaway/Pick/Adjust/Transfer/Return/Cycle`), 3 indexes (per-Stock with INCLUDE,
  partial Reference skipping NULL ReferenceId rows, global PerformedAt feed), no audit
  columns beyond `PerformedBy`/`PerformedAt` (rows are immutable — mirrors `security.AuditLog`).
- Domain types: `StockMovement` entity, `StockMovementType` enum, `StockMovementContext` record.
  `WMS.Domain` → `WMS.Common` project reference added; the enum lives in Common alongside
  `StockKey` so the Domain entity can reference it without crossing layers.
- `IStockMovementRepository` (read-side: `GetByStockAsync`, `GetByReferenceAsync`,
  `GetByProductAsync` — last one JOINs through `inventory.Stock.ProductId`).
- `IStockRepository.UpsertOnHandAsync` + `TransferStockAsync` signatures changed:
  `Guid? userId` replaced with `StockMovementContext` (controlled refactor — both call
  sites updated in the same commit).
- Atomic INSERT: every Stock mutation writes its matching `StockMovements` row(s) inside
  the same `BEGIN TRAN; … COMMIT;` batch. SET XACT_ABORT ON ensures rollback on any
  failure (THROW 50001/50002/50003 → no movements written).
- `ReceivingService` writes `MovementType=Receive`, `ReferenceType='ReceivingLine'`,
  `ReferenceId=<line guid>`. `ReceivingHeaderService.PostReceivingAsync` already inserts
  the line before the stock upsert — no reorder needed; just plumbed `line.Id` through
  via a new optional `ReceivingLineId` field on `ReceiveStockRequest`.
- `PutawayService` writes 2 movements per operation (source -qty, dest +qty),
  both `MovementType=Putaway`, `ReferenceType='Putaway'`, `ReferenceId=null` (TD-004 — closes
  when ADR-004 introduces a putaway header).

Test posture: 44 unit + 18 integration + 5 skipped (TD-006 — write-path SQL needs a real
SQL Server fixture; intent-complete tests in place, just need the fixture).

Forward-only — pre-existing Stock rows synthesized no history. New TD-004 (Putaway
ReferenceId null), TD-005 (ADR-004 missing), TD-006 (write-path test fixture) logged.

Foundation for: ADR-013 Adjustment, ADR-012 Transfer, future Pick/Pack, Cycle Count,
Activity tab (Phase 6B — wires `_ActivityPanel` to `GetByProductAsync`).

---

## 🔑 Auth Architecture (Day 3 decisions)

These choices govern the 3-step login flow + every tenant-scoped service.
Change them only via a new ADR.

### Session: Cookie Authentication (not JWT)

- MPA + Razor — cookies are the natural fit
- `HttpOnly` + `SameSite=Lax` + `SecurePolicy=SameAsRequest`
- 8-hour sliding expiration so a shift doesn't get bounced
- Server-side invalidation on logout (cookie scheme)
- JWT can be layered later for 3rd-party APIs without disturbing this

### Password Hashing: BCrypt (`BCrypt.Net-Next`)

- Cost factor **12** in Production, **4** in Dev/Test (faster suite)
- Built-in salt — no separate column

### Tenant DB Connection: IMemoryCache (5-min sliding)

- Cache key: `tenant:{tenantId:N}:conn`
- Resolved string lives in `IMemoryCache` with 5-minute sliding TTL
- Master DB hit at most once per tenant per 5 min — picks up
  `master.Tenants.DatabaseName` changes within minutes without restart
- Underlying `SqlConnection` pool handles per-connection reuse below

### 3-Step Login Flow (ADR-008)

1. **Step 1**: Email + Password → issue PreAuthToken (`master.PreAuthTokens`, 5-min TTL)
2. **Step 2**: Choose Tenant → exchange PreAuthToken for session cookie with `wms.tid` claim (skip if user has 1 tenant)
3. **Step 3**: Choose Warehouse → set `wms.wid` claim (skip if user has 1 warehouse in selected tenant)

### Service Interfaces

| Interface | Purpose | Status |
|-----------|---------|--------|
| `ICurrentUser` | UserId / TenantId / WarehouseId / Roles from cookie claims | ✅ A1 |
| `ITenantContext` | Wraps TenantId claim for tenant-scoped services | ✅ A1 |
| `ITenantConnectionFactory` | `IDbConnection` for tenant DB (cached) | ✅ A1 |
| `IAuthService` | login, password verify, issue tokens | A3 |
| `IPermissionService` | `HasPermission(function, crud)` resolution + cache | A7 |

---

## 📚 Important Decisions (See ADRs)

- ADR-001: Multi-tenant DB per tenant
- ADR-002: Dapper over EF Core
- ADR-003: MPA + htmx over SPA
- ADR-004: Hybrid putaway (template + scoring)
- ADR-005: Strategy pattern for configurable behaviors
- ADR-006: Activity-based billing
- ADR-007: Owner concept for VMI/3PL
- ADR-008: 3-step login flow — cookie auth, smart-skip, pre-auth tokens, BCrypt cost split, tenant validation middleware
- ADR-009: Pack video (browser MediaRecorder)
- ADR-010: Function-CRUD permission matrix — 5 action flags, MAX-aggregate across roles, IMemoryCache 15-min sliding, [RequirePermission] filter
- ADR-011: 3D Warehouse Monitor — schema in Phase 1, implementation deferred to Phase 4 (post-launch)
- ADR-012: Inter-warehouse Transfer — 9-state workflow, header+lines, status history, owner-aware
- ADR-013: General Stock Adjustment — separate from cycle count, reason-driven, approval workflow, billing hooks
- ADR-014: Stock Movement Log — materialised log table, transactional with Stock mutations, signed `QuantityDelta`, `StockId`-FK'd, forward-only

(Add ADRs in `docs/decisions/` when making architectural decisions)

---

## 🔧 Tech Debt Management

Tech debt items are tracked in `docs/TECH_DEBT.md` — log when discovered,
close with commit hash. Reference in code via `// TODO(TD-XXX): ...`.
See the file's "Process" section for workflow.

---

## 🛠️ Useful Commands

```bash
# Run tests
dotnet test

# Run specific test
dotnet test --filter "FullyQualifiedName~StockServiceTests"

# Build solution
dotnet build

# Run web app
cd src/WMS.Web && dotnet run

# Apply migrations
dotnet run --project tools/WMS.Migrate

# Generate test data
dotnet run --project tools/WMS.SeedData
```

---

## ⚠️ Things NOT to Do

- ❌ DO NOT bypass tenant filtering
- ❌ DO NOT introduce EF Core
- ❌ DO NOT use SPA frameworks (React/Vue/Angular)
- ❌ DO NOT hardcode strategies (FIFO, Push, etc.)
- ❌ DO NOT skip activity logging in operational flows
- ❌ DO NOT modify schema without migration script
- ❌ DO NOT commit secrets (use User Secrets / Azure Key Vault)
- ❌ DO NOT add EntityFramework, Newtonsoft.Json (use System.Text.Json)
- ❌ DO NOT change architectural patterns without ADR
- ❌ DO NOT install Three.js or 3D libraries (Phase 4 only)
- ❌ DO NOT skip filling X/Y/Z coords (needed even if 3D not yet built)
- ❌ DO NOT use Adjustment for Cycle Count results (use counts.CountAdjustments)
- ❌ DO NOT skip OwnerId in Transfer lines (preserve owner identity)
- ❌ DO NOT auto-apply Adjustments without approval workflow
- ❌ DO NOT pass fabricated Guids to CreatedBy/UpdatedBy (FK violation — use ICurrentUser or NULL)
- ❌ DO NOT hard-delete users with audit history (use IsActive = 0 instead)

---

## 📞 Quick Help

**Architecture questions**: See `docs/01_Master_Design.md`
**Database questions**: See `docs/02_Database_Schema.md`
**Roadmap questions**: See `docs/03_Roadmap.md`
**Quick lookups**: See `docs/04_Quick_Reference.md`

**Stuck on a specific feature?** Check the corresponding section in design docs.

---

**Last updated**: 2026-05-08 (Phase 6A — Stock Movement Log)
**Version**: 1.5
