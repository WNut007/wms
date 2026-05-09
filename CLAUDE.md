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
**Custom CSS**: `wwwroot/css/wms-custom.css` (Phase 1 design system, loaded AFTER Tabler)
**Phase 8 polish modules**: `wwwroot/css/wms-forms.css` (Section tabs, status dots, panes) + `wwwroot/css/wms-detail.css` (gradient avatar, accent bars, stat tiles). Both loaded after wms-custom.css. Tokens prefixed `--wmsf-` / `--wmsd-` (purple-600 #534AB7 system) — distinct from legacy `--wms-primary` (#5D4FA0) which the sidebar still owns.

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

**Active Sprint**: Phase 8 — Master Data UI Polish shipped (v0.8.1-master-polish)
**Current Focus**: pending decision — candidates include Phase 9 (ADR-004 + Putaway header), TD-014 Customer Activity half (blocked on Phase 7+ orders), Categories / UoMs / Carriers admin CRUD, TD-016 (putaway-pair grouping)
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
Activity tab on Product Detail (Phase 6C — shipped, closes TD-010).

### Day 6 — Phase 6B (Real Master Data)

**Branch**: `feat/master-data-impl` → merged to `main` · **Tag**: `v0.7.0-real-master-data`

Components:
- Migrations `20260508_003-007`: `Brand` column on `master.Products`, `Country` column
  on `master.Customers`, then 24 + 25 + 24 idempotent seed rows for Products / Customers /
  Warehouses (codes `PROD-0001..0024`, `CUST-0001..0025`, `WH-DM01..DM24` — disjoint from
  `DEMO-001` and `WH-MAIN` already seeded by migrations 052/046).
- Domain entities: `Product`, `Customer`, `Warehouse` under `WMS.Domain.Entities.Master`.
  All inherit `BaseEntity`; master tables have no `Version` column so repos omit it from SQL.
- List-row DTOs (`ProductListRow`, `CustomerListRow`, `WarehouseListRow`) carry JOIN-derived
  columns (`StockOnHand`, `LocationCount`, `CategoryCode`) without polluting the entity surface.
- Repos: `IProductRepository`, `ICustomerRepository`, plus `IWarehouseRepository` extended
  with `GetPagedAsync` / `GetByCodeAsync` / `GetByIdAsync` / `GetListRowByCodeAsync`. The
  existing 3-field `GetActiveAsync` projection used by the login picker stays untouched.
  Read-only — Insert/Update/Archive deferred to Phase 7+ admin CRUD.
- Sort whitelists per repo (`ProductSortMapper` / `CustomerSortMapper` / `WarehouseSortMapper`)
  are the SQL-injection defence: closed-set dictionary maps trusted keys to columns; unknown
  / hostile sortBy falls through to `Name ASC`.
- Boundary mappers in `WMS.Web.Services.Mappers`: `ProductStatusMapper`, `CustomerStatusMapper`,
  `WarehouseStatusMapper` translate PascalCase ↔ lowercase wire format. Mock vocabulary
  preserved where round-trippable (`pending` ↔ Customer.Draft); irreconcilable values
  (`out_of_stock`, `maintenance`) drop to no-filter silently.
- `CategoryIconResolver` + `CustomerAvatar` compute display-only fields (icon/color from
  category code; deterministic FNV-1a-flavoured initials/color hash) the schema doesn't
  carry. Lifted from the deleted Mock services so the visual language stays identical.
- `PagedResult<T>` relocated `WMS.Web.Services.Mock` → `WMS.DAL.Common` so repos and the
  cutover-era mocks shared the type during T2-T10.
- `MockProductDataService` / `MockCustomerDataService` / `MockWarehouseDataService` deleted
  (T11). All three controllers now read from real Dapper repos. `MockDocumentStorageService`
  unrelated — kept for tests.

Test posture: 101 unit (+57 sort-mapper cases) + 150 integration (+79 mapper / +49 controller
tests) + 5 skipped (TD-006, unchanged). Pure-function helpers + controller tests live in
`WMS.IntegrationTests` because `WMS.Web` targets net8.0-windows; `WMS.UnitTests` (net8.0)
can't reference Web types.

UX gap honoured: `master.Warehouses` is bool-only — mock's "maintenance" intermediate state
dropped (TD-009). Activity tab on Products kept on hardcoded entries (TD-010). Customer
order metrics stubbed `"—"` (TD-011). Product price column dropped — pricing is
owner-scoped on `ProductOwners.SettlementPrice` (TD-012).

Foundation for: Phase 6C activity-tab wiring, Phase 7+ admin CRUD on master entities.

### Day 6 — Phase 6C (Activity Tab Wiring)

**Branch**: `feat/activity-tab-wire` → merged to `main` · **Tag**: `v0.7.1-activity-tab` · **Closes**: TD-010

Components:
- `MovementActivityMapper` (`Services/Mappers/`) — pure-function
  `StockMovement → ActivityItem`. Per-`MovementType` title + icon +
  color; signed `QuantityDelta` splits Putaway and Adjust into `(in)` /
  `(out)` variants so paired rows render distinctly. `BucketDateGroup`
  helper buckets `PerformedAt` into Today / Yesterday / This week /
  Older (UTC calendar-day anchored).
- `ProductsController.Detail` reads movement history via
  `IStockMovementRepository.GetByProductAsync(productId, limit: 20)` —
  the JOIN-through-`inventory.Stock` query Phase 6A already
  implemented. Lookup is by resolved `Product.Id` (from
  `GetListRowByCodeAsync`), not by SKU string — pinned by
  `Detail_FetchesMovements_WithProductIdFromListRow`.
- The 5 hardcoded mock activities in `ProductsController.Detail`
  deleted; `Activities = movements.Select(MovementActivityMapper.Map)
  .ToList()`. Empty list (the default for `DEMO-001` and fresh seeds —
  forward-only per ADR-014) renders the existing
  "No activity yet." empty state.
- Pre-existing TD-010 regression test inverted: was
  `Detail_KnownSku_ActivitiesStillHardcoded_TD010Regression`
  (`Equal(5, ...)`), now `Detail_NoMovements_ActivitiesEmpty_TD010Closed`
  (`Empty(...)`). Two new tests cover the wired path + the
  `Product.Id`-vs-SKU contract.

Test posture: 101 unit + 174 integration (+22 mapper, +2 net
controller — 1 inverted, 2 added) + 5 skipped (TD-006).

Out of scope (logged for follow-up):
- TD-014: Customer + Warehouse Activity tabs still on hardcoded
  entries — both need data sources that don't exist yet (outbound
  orders schema; unified warehouse activity feed across receiving +
  cycle counts + putaway).
- TD-015: `MovementActivityMapper` doesn't resolve actor names or
  location codes. Title is `"Stock received"` (no `"{user}"`),
  description is `"+5 units · ReferenceType"` (no `"at {location}"`).
  Needs batch `IUserRepository` + `ILocationRepository` lookups +
  a richer `Map()` overload taking the resolved dictionaries.

### Day 6 — Phase 6D (Activity Name Resolution)

**Branch**: `feat/td015-resolve-names` → merged to `main` · **Tag**: `v0.7.2-resolve-names` · **Closes**: TD-015 · **Spawns**: TD-016

Components:
- New `StockMovementListRow` DTO (`WMS.DAL.Repositories.Inventory`) —
  read-projection carrying resolved `PerformedByName` + `From/To`
  location codes alongside the existing movement fields. Same
  separation as `ProductListRow` vs `Product`. Entity-only IDs
  (`StockId`, `UomId`, `OwnerId`, `ReferenceId`, raw Guid Performer
  /Locations) intentionally dropped — the panel never displays them.
- `IStockMovementRepository.GetByProductAsync` return type changed
  `IReadOnlyList<StockMovement>` → `IReadOnlyList<StockMovementListRow>`.
  Repo SQL grew 3 `LEFT JOIN`s: `master.Locations × 2` (From/To codes)
  + `security.Users` for `PerformedByName`. `COALESCE(FullName, Email,
  'System')` keeps the title non-blank for NULL-`PerformedBy` rows
  (system actions). Indexes `IX_Stock_Product` + `IX_StockMovements_Stock`
  still cover the leading lookup; the LEFT JOINs are seek-by-PK.
- `MovementActivityMapper` rewritten — `Map(StockMovementListRow)`
  produces `<span>{user}</span> {verb} {abs-qty} {unit/units}{location-clause}`.
  Verb + clause vary by `MovementType` and `QuantityDelta` sign:
  Receive → "received … at {to}"; Putaway+ → "putaway … into {to}";
  Putaway− → "moved … from {from}"; Pick → "picked … from {from}";
  Transfer → "transferred … from {from} at {to}"; Adjust+/− →
  signed in title with no location clause. Description shrinks to
  `{ReferenceType} · {Notes}` — qty moved into the title.
  Actor + location codes are HTML-encoded at the mapper boundary
  (operator-supplied strings cannot break out of the `@Html.Raw`
  render).
- Strategy chosen: SQL JOIN with new DTO (audit considered batch
  lookup + per-row resolve too). JOIN matches the established
  `ProductRepository` / `WarehouseRepository` pattern; avoids
  creating `ILocationRepository` for one consumer; single
  round-trip at ≤20 rows.
- `ProductsControllerTests`: `Build()` default + `Detail_WithMovements_*`
  migrated to the new DTO + assert the resolved title format
  ("Maya received 5 units at WH-MAIN").
- `MovementActivityMapperTests` rewritten — 32 cases covering
  per-`MovementType` verb/icon/location-clause, sign handling
  including signed Adjust, pluralization (`1 unit` vs `5 units`
  vs `1.5 units`), unsigned-qty-in-title (verb conveys direction),
  HTML encoding of actor + location, System-user pass-through,
  description shape, and the 5 date-bucket cases preserved.

Test posture: 101 unit + 185 integration (+11 net mapper) + 5
skipped (TD-006).

Atomic refactor — repo signature change, DTO introduction, mapper
rewrite, controller-test fixes all in commit `bb2a114` (Phase 6A's
"controlled refactor in same commit" precedent — half-applied
state can't compile).

Out of scope (logged for follow-up):
- TD-016: Putaway operations render as 2 separate rows. Mapper
  splits the location clause by sign so each row is grammatical
  (source→"from STAGE-01", dest→"into BIN-A1") but doesn't pair
  them into a single "moved {qty} from {from} to {to}" entry.
  Naturally closes with TD-004 / ADR-004 — once the putaway
  header table lands, paired rows share a `ReferenceId` and the
  renderer can group by `(ReferenceType, ReferenceId)`.

### Day 6 — Phase 6E (Warehouse Activity Feed)

**Branch**: `feat/td014-warehouse-activity` → merged to `main` · **Tag**: `v0.7.3-warehouse-activity` · **Closes**: TD-014 Warehouse half (Customer half remains open)

Components:
- New `ReceivingActivityRow` DTO (`WMS.DAL.Repositories.Inbound`) —
  Id + ReceivingNumber + ReceivedAt + Status + PerformedByName +
  LineCount. Lightweight, distinct from the write-side
  ReceivingHeader/Lines aggregate.
- `IReceivingHeaderRepository.GetActivityByWarehouseAsync` (new) —
  per-warehouse receipt feed; SQL leans on
  `IX_ReceivingHeaders_Warehouse(WarehouseId, ReceivedAt DESC)` for
  the WHERE+ORDER, correlated `COUNT(*)` for line count, COALESCE for
  PerformedByName.
- `IStockMovementRepository.GetByWarehouseAsync` (new) — reuses
  Phase 6D's `StockMovementListRow`; filters via
  `m.StockId → Stock.LocationId → Locations.WarehouseId` chain (Stock
  rows are warehouse-scoped via location). Cross-warehouse Transfers
  surface only the row whose Stock is in this warehouse.
- New `ReceivingActivityMapper` (`Services/Mappers/`) — visually
  distinct from `MovementActivityMapper` (icon `ti-truck-delivery` +
  color `#085041` deep green vs the movement Receive's
  `ti-package-import` + `#639922`) so adjacent timeline entries
  read as related-but-different. Verb varies by Status: `Posted` →
  "posted", `Draft` → "drafted", `Cancelled` → "cancelled" (red),
  unknown → "recorded" (defensive). Date-bucket delegates to
  `MovementActivityMapper.BucketDateGroup` so headers + movements
  group under the same section headers.
- `WarehousesController.Detail` composes the feed in C# (Q1 strategy
  from the Phase 6E brief — chosen over single-SQL UNION-ALL because
  cycle counts + future sources plug in as additional `Concat` calls
  without SQL rewrites):

  ```
  Activities = receiving.Select(ReceivingActivityMapper.Map)
      .Concat(movements.Select(MovementActivityMapper.Map))
      .OrderByDescending(a => a.Timestamp)
      .Take(ActivityFeedLimit)  // 20
      .ToList();
  ```

  The 4 hardcoded mock activities deleted (including the dubious
  "created warehouse" entry that synthesised a timestamp from
  `row.CreatedAt`).
- `WarehousesControllerTests.Build()` grew two new tuple slots
  (Receiving + Movement mocks, defaulting to empty); 12 existing
  call sites migrated to `(ctrl, repo, _, _)`. 6 new tests cover
  empty-state regression, Id-vs-code contract pin, single-source
  paths, two-source merge ordering, and the 20-row cap.

Test posture: 101 unit + 206 integration (+21 net — 15 receiving
mapper + 6 controller) + 5 skipped (TD-006).

Out of scope (logged):
- TD-014 Customer half remains open — needs orders + invoices
  schemas (Phase 7+). Composition pattern from this phase ports
  cleanly when those land.

### Day 6 — Phase 6F (jQuery Fix)

**Branch**: `feat/td013-jquery-fix` → merged to `main` · **Tag**: `v0.7.4-jquery-fix` · **Closes**: TD-013

One-line fix in `Views/Shared/_ValidationScriptsPartial.cshtml`:
the partial loaded `jquery.validate.min.js` +
`jquery.validate.unobtrusive.min.js` without jQuery itself,
producing `$ is not defined` console errors on every page that
rendered it. jQuery was already present at
`wwwroot/lib/jquery/dist/jquery.min.js` (default ASP.NET Core MVC
template artifact), just unreferenced — added a `<script>` tag
ahead of the validate libs.

The original TD-013 framing under-counted the blast radius —
"jQuery missing on `_AuthLayout`" — but the partial is rendered by
three views: `Auth/Login` (under `_AuthLayout`), `Receive/Index`
and `Putaway/Index` (both under `_MobileLayout`). Fixing the
partial cleared all three at once.

Form submit was always functional because server-side
ModelState validation handles the post; this was strictly a
client-side console-error / client-validation-feedback gap.

No tests touched — no controller / service / repo logic changed.
Build green. Tests: 101 unit + 206 integration + 5 skipped —
unchanged from Phase 6E.

### Day 8 — Phase 8 (Master Data UI Polish)

**Branch**: `feat/master-data-polish` → merged to `main` · **Tag**: `v0.8.1-master-polish` · **Closes**: user feedback "UI Manage Master ดูประถม" (Phase 7 forms felt primitive)

Visual-only polish across the 9 Master Data surfaces shipped in Phase 7. No controller-logic changes, no test count delta — 349 passing throughout.

Locked decisions (3 mockups approved going in):
- **D1 Insert forms**: V1 horizontal Section tabs (numbered, with eyebrow + name + per-tab status dot) replacing the Phase 7D vertical Alpine stepper. Tabs go full-width with the active state in purple-50 / 1px purple border. Status dots: gray (untouched) / amber (touched, in progress) / green (complete) / red (validation error).
- **D2 Edit forms**: same V1 Section tabs visual, but with edit-mode dot precedence (touched > complete) — initial paint shows green dots (data already valid); any field edit flips that section's dot to amber until save. Header gains 50px gradient avatar with 2-letter initials, Status badge, and a "View" link to /Detail.
- **D3 Detail pages**: white cards with 3px purple accent bar (border-left), 50px gradient avatar (135deg #534AB7 → #7F77DD), 4-column stat tile grid, underlined tab nav with count badges, 2-column body (2fr/1fr) with sidebar Quick Actions + Properties cards (both wear the accent bar).

CSS module strategy:
- `wms-custom.css` (1940 lines, Phase 1 design system) — untouched. Owns the sidebar/topbar/legacy --wms-primary (#5D4FA0).
- New `wms-forms.css` (Phase 8) — owns Form-side polish. Tokens prefixed `--wmsf-` (purple-600 #534AB7 etc.) so the brief's purple-600 system stays distinct from --wms-primary; sidebar regression risk is zero.
- New `wms-detail.css` (Phase 8) — owns Detail-side polish. Tokens prefixed `--wmsd-`.
- Both new modules loaded after wms-custom.css in _OfficeLayout so component classes win cleanly.

Alpine helper: `wmsf-form-state.js` (new). One function `wmsfFormState(opts)` powers all 6 forms:
- `step / totalSteps` drive `x-show` pane visibility + `--wmsf-progress-pct` for the header progress bar.
- `touched: {}` tracks field-level user interaction via `@input/@change="onTouch('FieldName')"`.
- `serverErrors: {stepNum: bool}` rendered server-side from ModelState; sticky until user touches a field in that section (= they're fixing it).
- `required: {stepNum: ['FieldName', ...]}` — exposed on the state object so per-form overrides can extend it (Customer Create's B2B/B2C dynamic requiredness).
- `mode: 'create'|'edit'` flips the dot precedence: create = error > complete > touched (touched = "started"); edit = error > touched > complete (touched = "unsaved changes").
- `dotClass(sec)` → 'is-error' | 'is-complete' | 'is-progress' | '' returned to `:class` on each tab's `.wmsf-dot`.

Customer Create's B2B/B2C special case: extends the base helper via `Object.assign(wmsfFormState({...}), { customerType: '...', requiredFor(sec) {...}, ... })`. When CustomerType==='B2B', Section 02 (Contact) picks up CompanyName + TaxId as required, the dot reflects this dynamically, and inline "(required for B2B)" hints appear next to the labels via x-show.

ViewModel additions (DetailPageViewModel):
- `AvatarInitials: string` — 2-letter overlay text. Empty falls back to rendering the IconClass icon inside the gradient avatar. Customer Detail uses initials (e.g. "AC"); Products + Warehouses keep the icon.
- `Badges: List<DetailBadge>` — extra badges next to the primary StatusLabel. DetailBadge(Label, Variant) where Variant is success/warning/danger/info/neutral/purple. Customers carry CustomerType + CustomerTier + "Key account" (if flagged); Warehouses carry Type; Products carry CategoryCode.

Bug found during T14 mental review (caught before browser smoke):
- `wmsfFormState` originally kept `required` as a closure local, not on the returned state object. Customer Create's B2B `requiredFor` override fell back to `this.required[sec]` which was undefined → "Cannot read properties of undefined". Fix: expose `required` on the state object; normalised internal references from closure → `this.`.

Out of scope (logged):
- "Last updated X ago by {User}" sticky banner on Edit (brief D2) — requires UpdatedAt + UpdatedByName resolution on each Edit VM, threading IUserRepository into all 3 admin controllers. Visual coverage already strong from the new header + the form's "Unsaved changes" Alpine indicator. Will revisit if requested.
- Settings tab on Detail (brief D3) — no underlying functionality; would render an empty panel. Existing 4 tabs (Overview / Images-on-Products / Documents / Activity) stay.
- Inner-panel polish (_ActivityPanel / _DocumentsPanel / _ImagesPanel) — still on legacy inline styles. Visual delta is dominated by the layout shell + header + tabs + sidebar; deeper refactor not needed for the brief's "ดูประถม" feedback.

Commit cadence: 7 commits across 7 sub-phases (T2-T3 CSS, T4 Products Create, T5-T6 Customer/Warehouse Create, T8-T10 Edit forms, T11-T13 Detail, T14 bugfix). Final repo state: 349 tests passing, 0 build warnings.

### Day 8 — Phase 7 (Admin CRUD: Products / Customers / Warehouses)

**Branch**: `feat/phase7-admin-crud` → merged to `main` · **Tag**: `v0.8.0-admin-crud` · **Partial close**: TD-017 (3 of 9 → 5 of 12 quick actions wired)

The big one. Six new endpoint pairs (Create + Edit) across the three master entities, with the layered validation stack we'd been deferring since Phase 6B.

Locked decisions from the audit (D1–D5):
- **D1 Hybrid validation**: DataAnnotations on view-models for jQuery
  unobtrusive client-side; FluentValidation `IValidator<T>` server-
  side for cross-field + async rules. Controllers manually call
  `ValidateAsync` after the DA pass and merge errors into ModelState.
  Avoids the deprecated `FluentValidation.AspNetCore` package.
- **D2 Alpine single-page stepper**: Tabler `.steps` markup +
  `x-data="{ step: 1 }"`; click-to-step, x-cloak hides during init.
  Single POST regardless of step state — reload-safe, one controller
  action, no TempData state.
- **D3 3 lookup repos**: `IProductCategoryRepository`,
  `IUomRepository`, `ICarrierRepository` (read-only `GetActiveAsync`)
  for Create-form `<select>`s. Matches Phase 6B factory pattern.
- **D4 No Version migration**: master tables stay last-write-wins on
  Edit. Concurrent edits collide silently; acceptable for low-churn
  metadata. Add a `Version` column later if collisions surface.
- **D5 Step counts vary**: Customer=4 (Identity / Contact /
  Commercial / Status), Product=2 (Identity / Classification),
  Warehouse=2 (Identity / Operations).

Phase structure (18 tasks across 7 sub-phases):
- **7A** (T1–T3): Repository write-side — `InsertAsync` + `UpdateAsync`
  on Product / Customer / Warehouse repos. Each Update omits
  natural-key columns from SET (Code is read-only on Edit; for
  Customer also CustomerType — flipping B2B↔B2C orphans the B2B-only
  fields).
- **7B** (T4–T6): View-models + FluentValidation validators.
  6 view-models (3 Create + 3 Edit), 6 validators. Static
  `AllStatuses` / `AllTypes` / `AllTrackingMethods` mirror DB CHECK
  constraints — single source of truth lives in the migration.
  Customer's B2B cross-field rule is the most non-trivial:
  `RuleFor(x => x.CompanyName).NotEmpty().When(x => x.CustomerType
  == "B2B")`.
- **7C** (T7–T9): Controllers — partial-class split
  (`{Entity}Controller.cs` + `{Entity}Controller.Admin.cs`) to keep
  each file near the 200-line guideline. Constructors grew 4–5 deps;
  existing `Build()` test helpers updated to default-mock the new
  ones so all 95 prior tests compile unchanged. POST flow:
  ModelState (DA) → manual `ValidateAsync` (FV) → if invalid
  re-populate lookups + return View → else Insert/Update → catch
  SqlException 2627/2601 (UQ on Code → field error) +
  SqlException 547 (FK violation → form-level "please reselect").
- **7D** (T10–T12): Razor views — 6 `.cshtml` files. Create views
  use the Alpine stepper; Edit views are single-page per D5.
  Customer Create's stepper toggles a "(required for B2B)" hint on
  CompanyName + TaxId via `x-text` reactivity; the FV server-side
  rule is authoritative.
- **7E** (T13–T15): UI wiring — Index "New {entity}" buttons →
  `<a asp-action="Create">`; Detail page sidebar gains an "Edit
  {entity}" QuickAction prepended to existing list. TD-017 partial
  progress: 5 of 12 quick actions wired now (was 2 of 9).
- **7F** (T16–T18): Admin controller tests + B2B validator tests.
  +37 tests net (8 ProductsAdmin, 8 CustomersAdmin, 13
  CustomerCreateValidator, 8 WarehousesAdmin). New `BuildAdmin()`
  helpers alongside existing `Build()` so admin-test setup doesn't
  disturb read-side test ergonomics.
- **7G**: Docs (this entry) + branch merge + tag.

Foundation packages:
- `FluentValidation.DependencyInjectionExtensions 11.9.0` — the modern
  non-deprecated DI helper. `services
  .AddValidatorsFromAssemblyContaining<Program>()` auto-discovers
  validators by interface; no per-validator registration needed.

Notable patterns:
- **Static option lists on view-models**: `ProductCreateViewModel
  .AllStatuses` / `AllTrackingMethods`; same for Customer / Warehouse.
  Razor accesses these via fully-qualified type name in
  `@foreach`. Mirror DB CHECK exactly — adding a new value goes:
  migration first, then update the static list.
- **Generic `LookupItem(Id, Code, Name)` record** in `WMS.DAL.Common`
  — same shape across all 3 lookup repos. Specific tables
  (Categories with Path, UoMs with Type) can ship dedicated
  projections later.
- **NullIfBlank helper** at controller boundary — whitespace-only
  posts persist as DB NULL so CHECK constraints (e.g. CustomerTier)
  evaluate cleanly. Pinned by `BlankNullableStrings_StoreAsNull`
  regression tests on each entity.

Out of scope (logged for follow-up):
- **TD-006 expansion**: SqlException 2627/547 catch paths in admin
  controllers also need a real SQL fixture to test meaningfully —
  same family as the existing TD-006 write-path tests. Added a note
  in TD-006's plan column.
- **Categories / UoMs / Carriers / Owners admin CRUD** — Phase 7
  builds read-only lookup repos for these only. Future phase.
- **Bulk import** (CSV upload) — not addressed.
- **Soft-delete UI** — Edit form's Status=Inactive/Discontinued
  (or Warehouse IsActive=false) is the canonical archive. No
  separate "Archive" button.
- **Audit trail UI on Edit** — CreatedAt/CreatedBy/UpdatedAt/
  UpdatedBy written but not surfaced on Edit form. Existing Activity
  tab on Detail covers Products + Warehouses; Customer remains
  TD-014-blocked.

Test posture: 101 unit + 243 integration (+37 net) + 5 skipped
(TD-006). Total **349 passing** (was 312).

Validation flow trace for the curious:
1. Browser POSTs `/Products/Create`.
2. ASP.NET Core model binder runs DataAnnotations →
   ModelState populated with field-level errors (Required,
   StringLength).
3. Controller calls `_createValidator.ValidateAsync(vm, ct)` →
   FluentValidation runs (enum membership, async Code uniqueness,
   B2B cross-field).
4. Each FV failure adds an entry to ModelState.
5. If `ModelState.IsValid` is false → re-populate lookup `<select>`
   data and `return View(vm)` (Razor renders red text via
   `asp-validation-for` spans, jQuery unobtrusive on subsequent
   client-side keystrokes).
6. Else: build entity, `try INSERT/UPDATE`. SqlException 2627 /
   2601 / 547 caught and converted to friendly ModelState errors.
7. Success → `RedirectToAction("Detail", new { code })`.

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

**Last updated**: 2026-05-09 (Phase 8 — Master Data UI Polish)
**Version**: 1.12
