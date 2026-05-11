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
**Phase 8 / 8.5 polish modules**: `wwwroot/css/wms-forms.css` (Section tabs, status dots, panes) + `wwwroot/css/wms-detail.css` (gradient avatar, accent bars, stat tiles) + `wwwroot/css/wms-picker.css` (Auth Step 3 picker — header strip, search, region chips, grouped rows). All loaded after wms-custom.css. Tokens prefixed `--wmsf-` / `--wmsd-` / `--wmsp-` (all purple-600 #534AB7 system) — distinct from legacy `--wms-primary` (#5D4FA0) which the sidebar still owns. Phase 8.5 also added Section 16 of wms-custom.css for global hover + anchor-underline reset (closes "ทุกหน้ามี hover ที่ปุ่ม/menu ไม่เอา underline" feedback).

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

> ## 🚀 v2.0.0-outbound-mvp — shipped 2026-05-10
>
> The Outbound MVP chain is closed end-to-end on the desktop. Operator can take an SO from `Draft` to `Shipped` without leaving the WMS:
>
> ```
> Draft → Open → Allocating | Allocated
>                ↓ (Generate pick — Phase 14C)
>                Picking → Picked | PartiallyPicked
>                          ↓ (Generate pack — Phase 14D)
>                          Packed
>                          ↓ (Generate shipment — Phase 14E)
>                          Shipped
> ```
>
> **What's in v2.0.0** (10 SO states + 4 outbound entities + 5 lifecycle services across allocation/pick/pack/ship): SalesOrder admin CRUD (14A) · FIFO allocation primitive (14B, ADR-005) · Pick task generation + execution (14C) · Pack task workflow with carton (14D) · Ship workflow with carrier + tracking (14E). Inbound MVP (PO + GR + Movement Log) shipped at v1.0.0.
>
> **What's NOT in v2.0.0** (deferred to post-MVP, all logged as TDs):
> - List pages for `/PickTasks` + `/PackTasks` + `/Shipments` (TD-036 / TD-037 / TD-038 — operator reaches them via Generate redirect today)
> - Mobile picker PWA (was sequenced as Phase 14E in original roadmap; desktop ship landed first to close the chain — same `IPickTaskService.SubmitAsync` entry point will plug in)
> - Pack video (ADR-009 spec needed first — MediaRecorder + retention + PDPA audit)
> - Scale integration / weight verification / box-suggestion algorithm
> - Multi-carton splitting + multi-shipment per SO (UNIQUE drops in future migrations)
> - Carrier FK lookup integration (`master.Carriers` exists with 4 seeded; Production-status filter blocks dev dropdown — free-text MVP avoids the friction)
> - Carrier API integration / label printing / tracking auto-assignment / manifest workflow / tracking events ingestion
> - Post-Submit reversal ("return to stock" for any of pick/pack/ship)
> - ScanEach vs ScanAndQty per-product pack modes
>
> **Test posture at v2.0.0**: 811 passing (288 unit + 518 integration + 5 skipped). Build clean. 31 outbound migrations applied (20260510_001 through _031).

**Active Sprint**: Day 10-11 · Phase 10A + 10B + 11A + 12 + 13 + 14A + 14B + 14C + 14D + 14E + 15A + 16 + 17 + 18 + 19 + 20 + 21 + 22 + **23** shipped → tags `v1.0.1-po-detail-complete` + `v1.0.2-inbound-hardened` + `v1.1.0-adjustments` + `v1.2.0-cycle-counts` + `v1.3.0-transfers` + `v1.4.0-so-crud` + `v1.5.0-allocation` + `v1.6.0-pick-task` + `v1.7.0-pack` + `v1.8.0-ship` + **`v2.0.0-outbound-mvp`** milestone + `v2.1.0-list-pages` + `v2.2.0-mobile-pick` + `v2.3.0-pack-video` + `v2.4.0-mobile-receive` + `v2.5.0-mobile-pack` + `v2.6.0-mobile-putaway` + `v2.7.0-mobile-count` + `v2.8.0-mobile-locate` + **`v2.9.0-reports`** · 🎉 MOBILE SUITE COMPLETE (6/6) · 📊 **REPORTS FOUNDATION SHIPPED** — first v3.0.0 chapter phase (Inventory dashboard + Order analytics + Operational KPIs + Excel export)
**Current Focus**: v3.0.0 SaaS launch features — Phase 24 tenant admin (v2.10.0), Phase 25 security hardening (v2.11.0), Phase 26 deployment (v2.12.0), Phase 27 onboarding tooling (v2.13.0); Phase 19.5 serial-aware mobile bundle (TD-040 + TD-042 + TD-043 — needs serial schema first); ADR-004 putaway header (TD-004); carrier FK integration.
**Blockers**: none

Update this section weekly during standups.

### Day 11 — Phase 23 (Reports Foundation — first v3.0.0 chapter phase)

**Branch**: `feat/reports-foundation` → merged to `main` · **Tag**: `v2.9.0-reports` · **Strategy**: v3.0.0 Land + Expand (enterprise must-have #1, high demo value for prospects)

First post-mobile-suite phase. Inaugural v3.0.0 chapter. Reports = enterprise prospect must-have — covers Inventory dashboard, Order analytics, Operational KPIs, with Excel export across all three.

**Audit findings (Scenario A — clean room with one library add)**:
1. ✅ ApexCharts 3.45.2 already loaded via CDN in `_OfficeLayout.cshtml:32`. Dashboard (Day 5) uses it. **Decision: reuse ApexCharts**, no library install.
2. ✅ `SidebarMenuViewComponent` already places `REPORTS` in `ModuleOrder` with `ti-chart-bar` icon (built for future use). Sidebar is permission-driven — adding REPORTS.VIEW function surfaces module automatically.
3. ❌ No `ReportsController`, no `Views/Reports/`. Clean room.
4. ❌ No `REPORTS.*` function seeded (Migration_042 lacks). New migrations needed.
5. ❌ No ClosedXML/EPPlus. **Decision: add ClosedXML 0.104.1** (MIT, mature, no Excel-install dep — EPPlus moved to commercial).
6. ✅ Aggregation pattern (`SUM(CASE WHEN ...)`) already used across every chip-count repo. Mirror the pattern.
7. ⚠️ **6th-instance spec rename audit TRIGGERED** (per `feedback_spec_rename_audit.md`). Brief mentioned "total stock value (current_qty × product_cost)". Reality: Product entity has NO `Cost` field. Pricing lives in `master.ProductOwners.SettlementPrice` (owner-scoped per ADR-007 — same product can have different prices per Owner). Applied silently → report shows stock **quantity**, logged as TD-045 for owner-scoped value rollup.

**Data layer** (new `WMS.DAL.Repositories.Reports/` namespace):
- `ReportRows.cs` — 14 read-projection records: `InventorySummary`, `StockByWarehouseRow`, `StockAgingBucket`, `TopProductRow`, `SlowMoverRow`, `OrderStatusCount`, `OrdersByDateRow`, `TopCustomerRow`, `FulfillmentCycleRow`, `MovementByDayRow`, `CycleCountVarianceSummary`, `OnTimeShippingSummary`, `TopOperatorRow`.
- `IReportRepository` + `ReportRepository` + `ReportRepositoryFactory` — 13 aggregation methods. Single class, one tenant-bound connection per call (factory pattern matching every other repo in the codebase).
- All SQL inline Dapper, JOIN-rich, idempotent. No new tables (pure reads against existing schema).

**Query highlights**:
- **`GetStockAgingBucketsAsync`** — CTE-driven; left-joins all 4 buckets so empty buckets still render with zeros (chart axis stays stable).
- **`GetSlowMoversAsync`** — CTE aggregates Stock per product → MAX(LastMovementAt), then filters by `DATEDIFF >= threshold` OR `IS NULL` (never-moved). Sort prioritises never-moved first.
- **`GetFulfillmentCycleAsync`** — JOINs Shipments back to SalesOrders, groups by `YEAR*100+MONTH` int for stable sort + `DATENAME(MONTH)` label for display, single pass.
- **`GetOnTimeShippingAsync`** — SO with NULL `RequestedShipDate` counts as on-time (no deadline = no miss). Single-row aggregate.
- **`GetCycleCountVarianceAsync`** — 4 correlated subqueries in one SELECT (Total sessions / Applied sessions / Variance lines / Counted lines) for the KPI stat row.

**Surfaces** (4 view actions + 3 export actions on the new ReportsController):
- `GET /Reports` — landing page. 3 cards (Inventory / Orders / KPIs) with gradient icons + descriptions. CTA arrows animate on hover.
- `GET /Reports/Inventory` — 4 stat tiles (Total on hand / Allocated / Distinct products / Locations) + 3 ApexCharts (bar: stock by warehouse; donut: 4 aging buckets; horizontal bar: top 10 SKUs) + table (slow movers, 20 rows).
- `GET /Reports/Orders?range=...` — 4 stat tiles (Total / Active / Cancelled with % / Top customer count) + 4 ApexCharts (donut: by status; area: trend over time; horizontal bar: top customers; line: avg fulfillment days/month). Preset bar chip strip (today/week/month/quarter/year, default month).
- `GET /Reports/Kpis?range=...` — 4 stat tiles with color-coded thresholds (Picks / Packs / On-time % green ≥95/amber ≥85/red / Accuracy % green ≥99/amber ≥95/red) + multi-line chart (picks vs packs daily, shared x-axis) + horizontal bar (top 10 pickers) + stat table (cycle count variance summary).
- `GET /Reports/ExportInventory|ExportOrders|ExportKpis` — `FileContentResult` with multi-sheet `.xlsx` (5 sheets each — Summary first, then per-data-source detail sheets). ClosedXML, bold purple header row (#534AB7), tabular numbers, AdjustToContents for column widths.

**Date range presets**:
- `DateRangePreset.Resolve(name)` — closed set: today / week / month / quarter / year. Default = month (30 days). UTC-anchored half-open `[from, to)`.
- `NormalisePreset(name)` — folds unknown / empty / null to default. Used by ViewModels to display the active chip.
- Custom date picker = TD-047.

**ViewModel builders** (private methods on controller):
- `BuildInventoryAsync` / `BuildOrdersAsync` / `BuildKpisAsync` — extracted so view actions AND export actions consume the **exact same query bundle** per report. Single source of truth — view always matches export.
- View actions: `View(await BuildXxxAsync(...))` one-liners.
- Export actions: `var vm = await BuildXxxAsync(...); return File(...)`.

**Sidebar**: Reports submenu (4 entries — Overview / Inventory / Order Analytics / Operational KPIs). Submenu renders when user has REPORTS.VIEW. Pattern matches Master / Outbound / Inbound submenu style.

**Permission**: single `REPORTS.VIEW` function seeded by Migration_20260511_033, granted to MANAGER role by Migration_20260511_034 (Picker/Packer omitted — operational roles don't need dashboards). ADMIN gets it via BLL bypass. Per-report split = TD-046.

**Tests** (+26 net):
- `ReportsControllerTests` (+11) — Index returns View; Inventory builds VM and returns ViewResult; Orders parses range param + flows to VM (Theory across 7 inputs); Kpis returns VM with computed properties; 3 Export* tests verify `FileContentResult` + content type + filename pattern.
- `DateRangePresetTests` (+15) — Theory across 7 preset inputs for label, 1 for half-open range invariant, 7 for NormalisePreset closed-set fallback.
- Real SQL aggregation queries lack integration tests (TD-048 — same TD-006 family).

Test posture: **927 passing** (was 901 / +26). 288 unit + 639 integration + 5 skipped.

**Out of scope** (logged as TDs):
- **TD-045** — Stock VALUE roll-up (current is quantity-only). Requires `master.ProductOwners.SettlementPrice` JOIN; owner-scoped per ADR-007. Spec rename pattern applied.
- **TD-046** — Per-report permission split (REPORTS.INVENTORY / REPORTS.ORDERS / REPORTS.KPIS for SoD on enterprise tenants).
- **TD-047** — Reports sub-features deferred: custom date picker, PDF export, CSV export, scheduled reports (Hangfire-driven), saved filters, per-warehouse/customer scoping, drill-through links, real-time auto-refresh.
- **TD-048** — Integration tests for the 13 SQL aggregation queries — needs SQL fixture (TD-006 family).

**Patterns established**:
- **ApexCharts in WMS**: server-side `JsonSerializer.Serialize` of chart data inline in Razor → `@Html.Raw` into `<script>` body. Unicode + escape handling for free, no AJAX. Loaded via existing `_OfficeLayout` CDN reference.
- **ClosedXML helper pattern**: static class, one method per VM, returns `(byte[] Bytes, string FileName, string ContentType)` tuple. Controller wraps in `File()`. Multi-sheet workbooks via `wb.Worksheets.Add(name)`. Bold + purple-tint header rows for visual consistency.
- **Half-open date range presets**: `[from, to)` UTC-anchored; SQL filters use `>= @from AND < @to` consistently. UTC keeps reports deterministic across globally distributed tenants.
- **View + export share a builder**: extract private `BuildXxxAsync` from each view action; export action reuses it. Single source of truth per report — operator's screen always matches their downloaded file.

**Spec compliance check**:
- ✅ /Reports accessible from sidebar (permission-gated)
- ✅ 3 sub-reports (Inventory / Orders / KPIs)
- ✅ Inventory: total stock + warehouse breakdown + aging + top SKUs + slow movers
- ✅ Orders: by status + top customers + cycle time + trend
- ✅ KPIs: picks/packs per day + variance + on-time % + top performers
- ✅ Date range filter (fixed presets — today/week/month/quarter/year)
- ✅ Excel export per report (3 endpoints, multi-sheet)
- ✅ Charts (ApexCharts — bar / donut / line / area variants)
- ⚠️ Total stock VALUE → quantity only (TD-045 spec rename: no Cost field; owner-scoped price in ProductOwners)
- ⚠️ Custom date picker → TD-047
- ⚠️ PDF + CSV export → TD-047

**Notes**: Audit completed in ~10 min. ApexCharts CDN already loaded was the biggest unlock — zero new chart-library decision needed. ClosedXML 0.104.1 was the only library install (~25 KB pkg + transitives). Pre-implementation 6th-instance spec rename audit caught the Cost-vs-SettlementPrice gap before T2 — memory `feedback_spec_rename_audit.md` proved its value at 6th instance. v3.0.0 "Land + Expand" first move: reports module is operator-visible immediately + makes enterprise demos compelling.

---

### Day 11 — Phase 22 (Mobile Locate PWA — Scenario A, closes mobile suite)

**Branch**: `feat/mobile-locate-pwa` → merged to `main` · **Tag**: `v2.8.0-mobile-locate` · **Spec**: `docs/mockups/mobile-specs/phase-22-mobile-locate-spec.md` (Implementation Notes appended T3)

🎉 **Final mobile-suite phase. 6 of 6 mobile ops now shipped** (Pick / Receive / Pack / Putaway / Cycle Count / Locate). See **Mobile Suite Retrospective** at end of this entry.

Read-only utility — find any item or location. Simplest of the 5 mobile phases (no state machine, no service mutations). Pure presentation-layer addition with two new JOIN-rich read methods on existing `IStockRepository`.

Built in ~1h vs 3-4h spec estimate.

**Audit findings (Scenario A confirmed)**:
1. ✅ `IStockRepository.GetByProductAsync` + `GetByLocationAsync` exist (entity-only). New JOIN-rich row DTOs added for the rich projection mobile views need.
2. ✅ `inventory.Lots.ReceivedDate` (DATE) + `ExpiryDate` (DATE nullable) drives lot-age display via `DATEDIFF`.
3. ✅ `master.Zones.Type` drives status badge color (Storage = purple, Receiving/Staging = blue, Picking = green) — same enum used by Phase 20 putaway queue.
4. ✅ Product/Location lookup by code — same inline-Dapper pattern from PutawayController + ReceiveController.
5. ❌ No serial schema (per Phase 19+20 audits) → smart search drops serial detection. **TD-043 family deferral** — product OR location only.
6. ❌ No `/locate/` route, no Phase 1 surface — clean room (no retirement).
7. **No spec rename triggered** — 5th-instance audit clean (memory `feedback_spec_rename_audit.md` informed but didn't fire).

**Surfaces (4 actions on the new mobile LocateController)**:
- `GET /locate` — search entry. No data load (recent searches live in client-side localStorage). Renders search input + scan area HERO + recent list.
- `GET /locate/search?q=...` — smart search:
  1. Try `Product.Code` (Active only) → redirect to `/locate/item/{id}`
  2. Try `Location.Code` (warehouse-scoped, Active only) → redirect to `/locate/loc/{id}`
  3. Skip serial detection (TD-043 — no schema)
  4. Not found → bounce back to `/locate` with banner ("...Serial scanning is Phase 19.5 / TD-043.")
- `GET /locate/item/{productId}` — multi-location view per product. 404 on missing product. Renders product card + 3-col stat tiles (Total / Available / Allocated, computed sums) + per-location cards sorted by Lot.ExpiryDate ASC (FEFO awareness) then LocationCode.
- `GET /locate/loc/{locationId}` — items at location. 404 on missing OR cross-warehouse (operator can only browse current-warehouse bins). Renders location HERO card + 4-tile stat grid (Distinct items / Total qty / Capacity policy / Stock rows) + per-item cards sorted by ProductCode.

**DAL extensions** (2 new methods on `IStockRepository` + 2 record DTOs):
- `LocateItemRow` — per-product multi-location row (StockId, Qty, Loc, ZoneType for status, Owner, Lot info with `LotAgeDays` via DATEDIFF, ExpiryDate, Pallet, UoM, CreatedAt). Sorted by `Lot.ExpiryDate ASC` (FEFO awareness).
- `LocateLocationRow` — per-location multi-item row (StockId, Qty, Product Code+Name, Owner, Lot info, Pallet, UoM, CreatedAt). Sorted by ProductCode.
- `GetItemViewAsync(productId)` — JOIN Locations + Zones + Owners + UoMs + LEFT JOIN Lots + Pallets. WHERE `QuantityOnHand > 0`.
- `GetLocationViewAsync(locationId)` — same JOIN backbone, swap emphasis from location to product metadata.

**UI** (3 views):
- `Locate/Index.cshtml` — pure CSS `.lc-*` token namespace, `.no-scrollbar`. Search input (mono, autofocused) → submits as GET to `/locate/search`. Big scan area HERO (dashed primary border, barcode icon + "native scanner is future TD" note). Recent searches via Alpine localStorage state (dedup by q-string, cap 10 entries, "X min ago" relative time, Clear button).
- `Locate/Item.cshtml` — purple-accent product card + 3-col stat tile grid (Total gray / Available green / Allocated amber). Per-location cards with border-left color by `Zone.Type` (Storage purple / Pick face green / Staging+Receiving blue / Other gray). Lot age display ("12 days old · expires Mar 15") with traffic-light coloring (fresh <30d green / aged <90d amber / stale red). Tap-through to `/locate/loc/{LocationId}`.
- `Locate/Loc.cshtml` — purple-bordered location HERO card with map-pin icon + LocationCode (mono 18px purple) + Zone meta + status badge (Active green / Blocked red / Maint amber). 4-tile stat grid. Per-item cards with package icon + ProductCode + Lot info + qty. Tap-through to `/locate/item/{ProductId}` for round-trip navigation.

**Sidebar**: "Locate (mobile)" entry under Inventory module after "Transfers". `inventoryActive` Or-chain widened to include "Locate".

**Manifest**: `/locate/manifest.json` with `#534AB7` theme color.

**Tests** (+6 net):
- Index: NoWarehouse redirect + Happy returns view
- Search: NoWarehouse redirect + BlankQuery early bail-out (verifies `ITenantConnectionFactory.CreateConnection` is NOT called)
- Item: NoWarehouse redirect
- Loc: NoWarehouse redirect

The smart-search routing happy path + Item/Loc inline header lookups use `ITenantConnectionFactory` + raw Dapper which can't be cleanly mocked (TD-041 family — same as Phase 18 ReceiveController, Phase 20 PutawayController). **Out of test scope** as a deliberate trade-off (lightweight controller, would need a service-provider fixture to cover end-to-end). Inline lookups are 5 lines each; risk is low.

Test posture: **901 passing** (was 895 / +6). 288 unit + 608 integration + 5 skipped.

**Out of scope** (logged):
- **TD-043 family** — Smart search with serial detection (needs `inventory.LotSerials` schema, bundle with TD-040 + TD-042 in Phase 19.5)
- Movement history view (audit trail per item/location)
- "Start cycle count from location" action (Phase 21 link from Loc page — would pre-populate location filter)
- Favorites/saved searches per user
- Recently viewed (per-user history; client-side localStorage suffices for MVP)
- Photo of location (visual confirmation)
- Native barcode scanner integration
- Service worker offline caching
- PWA icons
- Loc view inline-header-lookup test coverage (TD-041 family)
- Search routing happy path test coverage (TD-041 family)

**Spec compliance check**:
- ✅ /locate accessible from sidebar
- ✅ Search bar works (type then submit)
- ✅ Big scan area renders (manual entry for MVP — native scanner is future TD per spec)
- ✅ Recent searches display (client-side localStorage; favorites = TD per brief)
- ✅ Smart search detects type (product OR location — serial deferred per spec audit)
- ✅ Item view shows multi-location list with status colors per Zone.Type
- ✅ Location view shows items at bin
- ✅ Stat tiles accurate (Total / Available / Allocated)
- ✅ Lot age displayed for FEFO awareness (with traffic-light coloring)
- ✅ Hidden scrollbars throughout
- ✅ Touch targets ≥ 38px
- ✅ PWA installable (manifest with #534AB7 theme)
- ⚠️ Serial scan detection → deferred (TD-043 bundle)
- ⚠️ Favorites toggle → deferred (TD)
- ⚠️ "View movement history" / "Start cycle count" action buttons → deferred (TDs; round-trip nav via tap-through covers basic exploration)

**Notes**: Audit completed in ~5 min — all required schema bits exist (Stock+Locations+Zones+Lots already JOINed elsewhere), only missing piece was the JOIN-rich projections themselves. Two-method DAL addition + 4-action controller + 3 views = ~1h ship. Pattern reuse from Phase 20 putaway hit ~80% (location card design + Zone.Type-based status coloring + inline Dapper for header lookups).

---

## 🎉 Mobile Suite Retrospective (post-Phase-22)

**Mobile suite v2.2.0 → v2.8.0 — six PWA workflows shipped over Day 10-11.**

### What's in the suite
| Phase | Tag | Workflow | Surface | LoC (controller + views) |
|---|---|---|---|---|
| 16 | v2.2.0-mobile-pick | Pick tasks | `/pick` | ~250 |
| 18 | v2.4.0-mobile-receive | Goods receipt | `/receive` | ~640 (replaces Phase 1) |
| 19 | v2.5.0-mobile-pack | Pack tasks | `/pack` | ~690 |
| 20 | v2.6.0-mobile-putaway | Putaway from staging | `/putaway` | ~530 (replaces Phase 1) |
| 21 | v2.7.0-mobile-count | Cycle count | `/count` | ~750 |
| 22 | v2.8.0-mobile-locate | Stock browser | `/locate` | ~610 |

### Time totals
- **Spec estimate (sum)**: 16-22h
- **Actual (sum)**: ~10h
- **Velocity multiplier**: ~1.8x faster than estimate
- All 5 phases (18, 19, 20, 21, 22) used the audit-first protocol; 3 of 5 had to defer features (TD-040/TD-042/TD-043/TD-044) but no phase paused for user decision after Path D was approved on Phase 19.

### Patterns established (worth remembering)
1. **Audit-first protocol** — pre-T1 grep + read of relevant entities/services/migrations. Catches spec-vs-reality gaps cheaply. `feedback_audit_first_for_lookup_integration.md` + `feedback_spec_rename_audit.md` capture the playbook.
2. **Spec rename pattern** — 3 instances of "spec names a column that doesn't exist; capability lives under different schema name" (Phase 18: IsSerialTracked → TrackingMethod; Phase 19: 'LotOnly' → 'Lot'; Phase 20: IsStaging → Zone.Type IN). 4th and 5th-instance checks (Phase 21 + 22) passed clean.
3. **Per-line card layout** — Phase 18 receive established the shape (purple-accent border-left + product/location header + side-by-side qty grid + variance indicator + quick-adjust + collapsible fields). Reused near-line-for-line in Phases 19, 20, 21.
4. **All-lines-on-one-page > per-location wizard** — operator pre-walks the aisle physically + types at the end, not card-by-card with the device. TD-044 (per-location wizard) logged for Phase 21 if pickers ask for it.
5. **Constructor injection from start** — every mobile controller has every dep via constructor. Submit happy paths covered end-to-end (no TD-041 equivalent for Phases 19/21). The PutawayController + LocateController use one inline `ITenantConnectionFactory` for tiny header lookups (5 lines each); test gap accepted as TD-041 family.
6. **Client-side recent + localStorage** — Phase 22 Locate's recent searches use Alpine + localStorage with no backend table. Should be the default for any "recent N items" UX where the data is per-device.
7. **Two-forms-share-hidden-inputs trick** — Phase 21 Cycle Count's Save vs Submit buttons each post their own `<form>`, with line-level hidden inputs wired to BOTH via the `form="form-id"` HTML attribute. Avoids JS form-mutation hacks.
8. **PURPLE > GREEN for primary submit** — across all 5 phases the user direction was consistent: stick with `#534AB7` for the mobile primary submit button. Green is reserved for semantic success (suggestion cards, variance match indicator, Active status badges). Documented in spec appendices.

### TD bundle for Phase 19.5 (serial-aware mobile)
The serial schema gap blocks 3 mobile features:
- **TD-040** — Mobile receive serial entry (per-line `LotSerials` capture)
- **TD-042** — Mobile pack scan-incremental UX (operator scans into carton)
- **TD-043** — Mobile pack smart-scan with serial detection + Mobile locate smart-scan with serial detection (auto-detect by serial inventory lookup)

Bundle when `inventory.LotSerials` (or equivalent) lands. Estimated ~3-4h for the schema + ~2h per mobile surface to wire in.

### Other deferred (not Phase 19.5)
- **TD-004** — ADR-004 putaway header (PutawayTask + PutawayTaskLine schema; would let mobile putaway track per-task SLA + multi-line splits)
- **TD-041 family** — Inline `ITenantConnectionFactory` test coverage (needs service-provider fixture; Phase 18, 20, 22 all share)
- **TD-044** — Per-location wizard for mobile cycle count
- Native barcode scanner integration (5 phases mention this)
- Service worker offline caching (5 phases mention this)
- PWA icons (5 phases mention this — would polish "Add to home screen" UX)
- "Start cycle count here" / "View activity" actions on Locate Loc page (Phase 21 cross-link)

### Foundation for v3.0.0+ (SaaS launch)
The 6 mobile PWAs are the warehouse-staff surface. Desktop-side workflows (PO admin, SO admin, Pick/Pack/Ship execution, Cycle Count approval, Adjustment workflow, Transfer workflow) covered by Phases 7, 9-15. **Outbound MVP shipped as v2.0.0**; **Inbound MVP shipped as v1.0.0**. v3.0.0 candidates: tenant onboarding flow, billing module (per ADR-006 activity-based), reports/dashboards, manifest workflow, carrier API integration, Pack video for B2C policy, post-Submit reversal flows for pick/pack/ship, full ASN handling, formal vendor master.

### Day 11 — Phase 21 (Mobile Cycle Count PWA — Scenario A, all-lines-on-one-page UX)

**Branch**: `feat/mobile-count-pwa` → merged to `main` · **Tag**: `v2.7.0-mobile-count` · **Spec**: `docs/mockups/mobile-specs/phase-21-mobile-cycle-count-spec.md` (Implementation Notes appended T3)

Fifth mobile-suite expansion (5th mobile PWA after Phase 16 picker + Phase 18 receive + Phase 19 pack + Phase 20 putaway). Audit confirmed clean **Scenario A** — Phase 12 service surface is well-aligned with the spec (no field rename triggered this phase, 4th-instance memory check passed clean). Only Locate remains as Phase 22 to complete the mobile suite.

Built in ~1.5h vs 4h spec estimate. Pure presentation-layer addition — zero new schema, zero new service code, zero new DAL.

**Audit findings (resolved before T1)**:
1. ✅ `ICycleCountService` exists with full surface — `CreateAsync`, `SaveCountedQuantitiesAsync`, `SubmitForReviewAsync`, `ApproveAndApplyAsync`, `CancelAsync`, `GetByIdAsync` (Phase 12 desktop equivalent).
2. ✅ State machine: `Counting → Review → Applied | Cancelled` (4 states, matches spec).
3. ✅ `CountLineUpdate(LineId, CountedQuantity, LineStatus, Notes)` record matches per-line save shape exactly.
4. ✅ `CYC-YYYYMMDD-NNNN` number format matches spec; LineStatus enum `'Pending|Counted|Skipped'` matches spec.
5. ✅ `ICycleCountRepository.GetPagedAsync(filter)` for queue + `GetLineRowsByIdAsync(id)` for the JOIN-resolved per-line projection (ProductCode + ProductName + LocationCode + UomCode + OwnerCode + LotNumber + PalletNumber all pre-resolved).
6. ❌ No Phase 1 mobile cycle count surface — purely additive (no retirement).
7. ❌ No `/count/` route exists today — clean room.
8. **No spec rename triggered** — 4th-instance memory check passed clean. Phase 12 design closely matches the spec author's mental model.

**One UX deviation from spec** (documented in appendix):
Spec describes a per-location wizard ("Location 16 of 24" with `[Save & next location →]`). Built **all-lines-on-one-page** for mental-model consistency with Phase 18-20. Per-location wizard = TD-044 candidate. Reasoning: operator pre-walks the aisle physically + types quantities at the end, not card-by-card with the device.

**Surfaces (4 actions on the new CountController)**:
- `GET /count` — queue. Two paged calls (Counting + Review) merged into 2 sections. Page size 50 each. Counting on top (operator-actionable); Review below (read-only — desktop approves).
- `GET /count/{sessionId}` — task page. Loads `CycleCountDetail` (header) + `GetLineRowsByIdAsync` (richer projection for per-card render). 404 on Applied/Cancelled (operator hits desktop /CycleCounts for terminals).
- `POST /count/save/{id}` — bulk `SaveCountedQuantitiesAsync`; bounces back to task page (operator continues counting).
- `POST /count/submit/{id}` — Save THEN `SubmitForReviewAsync` (Counting → Review). Bounces to queue. Skips the Save call when zero lines in payload (operator may submit an already-saved session).
- `POST /count/cancel/{id}` — `window.prompt`-driven, controller-level 3-char min reason gate (mirror Phase 19+20).

**UI**:
- `Count/Index.cshtml` — pure CSS `.cn-*` token namespace, `.no-scrollbar`. Two sections:
  - "Active sessions" (Counting border-left purple): progress bar `CountedLineCount/LineCount` + `%` + StartedByName
  - "Pending approval (desktop)" (Review border-left amber): variance chip (red short / green match)
  - Empty state: `ti-clipboard-check` + "No cycle count sessions. Start one from the desktop /CycleCounts page."
- `Count/Task.cshtml` — single-page-all-lines. Single view handles BOTH Counting (editable form) and Review (read-only summary). Per-line cards (purple-accent border-left):
  - **Location card** (top): map-pin icon + LocationCode (mono purple, 14px) + LineNumber + Lot/Pallet meta when applicable
  - **Product row**: package icon + ProductCode (mono) + ProductName
  - **Side-by-side qty grid**: Expected tile (gray, "From system") | Counted tile (white with thicker purple border for focus, inline number input + "Tap to edit")
  - **Live variance indicator**: ✓ Match (green) / ↓ N short of expected (red) / ↑ N over expected (amber) / ⊘ Skipped (neutral)
  - **Quick adjust row**: -1 / +1 / -10 / +10 (32px tap targets, disabled when Skipped)
  - **Skip button** (toggle): "Skip this location" / "⊘ Skipped — tap to undo"
  - Auto-flip status (Phase 12 desktop pattern reused): empty → Pending; type qty → Counted; clear → Pending; quick-adjust always sets Counted
  - Review state: amber lock banner + 3-col stat tile grid (Match/Short/Over with pre-computed counts) + read-only Counted values
- **Sticky-bottom (Counting only)**: Submit for review (purple primary, gated on `hasAnyCount()`) + Save progress (purple-outline secondary) + Cancel session button below sticky
- **Form architecture**: TWO forms in DOM (count-form for Save, submit-form for Submit), each line's hidden inputs wired to BOTH via the `form="form-id"` attribute — operator clicks the right button, that form posts. Avoids JS form-mutation hacks.

**Sidebar**: "Cycle Count (mobile)" entry under Counts module after "Cycle Counts" desktop. `countsActive` Or-chain widened to include "Count" route.

**Manifest**: `/count/manifest.json` with `#534AB7` theme color (matches design system).

**Tests** (+16 net):
- Index: NoWarehouse redirect + Happy partitions Counting from Review (verifies the 2-paged-call shape + ViewBag.ReviewRows wiring)
- Task: NotFound 404 + TerminalStatus Theory (Applied/Cancelled both → 404) + Counting returns view with line rows (verifies `GetLineRowsByIdAsync` integration) + Review also renders
- Save: NoWarehouse redirect + Happy bounces back to task with "Saved N" message (verifies CountLineUpdate projection) + ServiceThrows redirects with error
- Submit: Happy saves THEN submits + AlreadyReview idempotent + NoLines skips Save but still calls Submit
- Cancel: BlankReason rejected (no service call) + Happy bounces to queue + AlreadyCancelled idempotent

Submit happy path IS exercised end-to-end (CountController has zero inline service-locators — every dep is constructor-injected, lesson from Phase 18 TD-041 / Phase 20 applied).

Test posture: **895 passing** (was 879 / +16). 288 unit + 602 integration + 5 skipped.

**Out of scope** (logged):
- **TD-044** — Per-location wizard with progress bar ("Location 16 of 24" + Save & next button per spec). All-lines-on-one-page is faster to operate when the operator has pre-walked the aisle.
- **Apply variance via mobile** — mobile MVP is Counting + Submit only; desktop approves via `ApproveAndApplyAsync` (separation of duties: counter ≠ approver enforced at service layer).
- **Re-count flow** — count line again with reason capture
- **Photo capture per variance** — bundle with pack-video infrastructure (ADR-009)
- **Multi-counter sessions** (collaborative counting on the same session)
- **Always-focused barcode input**
- **Service worker offline caching**
- **PWA icons** (manifest `icons:[]` empty)
- **Filter chips on queue** — spec mentions All/Counting/Review/Done chips; today the 2-section layout is sufficient (Counting + Review). Adding Done/Cancelled chips would require widening the Index ViewBag with extra paged calls — defer until operators ask.
- **CountedAt-based "Started by + time ago"** — `StartedAt` rendered but no relative-time helper; minor

**Spec compliance check**:
- ✅ /count accessible from sidebar
- ✅ Queue shows active + review sessions
- ✅ Tap session → task page
- ✅ Side-by-side qty visible (Expected | Counted)
- ✅ Variance auto-flags with color (Match green / Short red / Over amber / Skip neutral)
- ✅ Quick adjust works (-1 / +1 / -10 / +10)
- ✅ Auto-flip status on qty entry (matches Phase 12 desktop)
- ✅ Skip option works (toggle)
- ✅ Review state shows stat tiles (3-col Match/Short/Over)
- ✅ Notes captured per-line (textarea per-card; spec showed session-level notes too — operator can use the per-line Notes today)
- ✅ Submit for review works (Counting → Review)
- ✅ Hidden scrollbars throughout
- ✅ PWA installable (manifest with #534AB7 theme)
- ⚠️ Per-location wizard with progress bar → all-lines-on-one-page (TD-044)
- ⚠️ Filter chips (All/Counting/Review/Done) → 2-section layout for now
- ⚠️ Session-level notes on review page → per-line Notes only

**Notes**: Audit completed in ~10 min — Phase 12's clean design + the 3 prior mobile phases' patterns meant zero unknowns. Memory `feedback_spec_rename_audit.md` informed the audit checklist; nothing triggered this phase. The TWO-forms-share-hidden-inputs trick (`form="form-id"` attribute) is a clean way to handle Save vs Submit without JS form mutation — worth remembering for Phase 22 if Locate's read-only nature still needs save-then-do paths.

### Day 10-11 — Phase 20 (Mobile Putaway PWA — Scenario A per spec audit, replaces Phase 1 form)

**Branch**: `feat/mobile-putaway-pwa` → merged to `main` · **Tag**: `v2.6.0-mobile-putaway` · **Replaces**: Phase 1 single-page PutawayController · **Spec**: `docs/mockups/mobile-specs/phase-20-mobile-putaway-spec.md` (corrections appended T3)

Third mobile-suite expansion (4th mobile PWA after Phase 16 picker + Phase 18 receive + Phase 19 pack). Pre-implementation audit confirmed **Scenario A** with one apply-silently rename — 3rd consecutive instance of "spec names a column that doesn't exist; capability lives under a different schema name". New memory entry `feedback_spec_rename_audit.md` captures the pattern.

**Audit findings (resolved before T1)**:
1. **`master.Locations.IsStaging` does not exist.** Reality: `master.Zones.Type` enum has `'Receiving' | 'Storage' | 'Picking' | 'Packing' | 'Shipping' | 'Staging' | 'Quarantine' | 'Returns'`. Filter via `Zone.Type IN ('Receiving','Staging')` — applied silently. Same shape as Phase 18 (`IsSerialTracked` → `TrackingMethod`) and Phase 19 (`'LotOnly'` → `'Lot'`). 3rd instance → memory written.
2. **`IPutawayService` exists** — atomic source→dest move via `IStockRepository.TransferStockAsync` + paired `StockMovements` writes per ADR-014. Reused as-is.
3. **Movement Log integration** already in place via `StockMovementContext(MovementType=Putaway, ReferenceType='Putaway', ReferenceId=null)` (TD-004 placeholder for ADR-004 putaway header).
4. **No PutawayTask header/lines table.** Queue derived from Stock at staging-zone locations — no migration this phase. TD-004 (ADR-004 putaway header) remains future work; today's mobile flow is "find Stock at staging zones" → "move to suggested storage bin" → atomic.
5. **No suggested-location service.** Built inline as a new `IStockRepository.GetSuggestedPutawayLocationAsync` method using existing schema fields (`BinRank`, `IsPickface`, `ZoneId`, `Status`).
6. **Phase 1 PutawayController + form** (typed-codes flat form) → replaced entirely per Phase 18 Decision 3A precedent.
7. **No sidebar entry for Putaway today** → added new "Putaway (mobile)" under Inbound after "Receive (mobile)".
8. Demo seed creates a `'RECV'` (Receiving) zone — queue has data on a fresh setup.

**Surfaces (3 actions on the new PutawayController)**:
- `GET /putaway` — queue. Stock at Receiving/Staging-zone locations in operator's current warehouse, FIFO oldest first. Empty state with `ti-package-off` + "All caught up!". Aged badge (>24h waiting) computed client-side from `CreatedAt`.
- `GET /putaway/{stockId}` — task page. Loads Stock entity (for 6-tuple) + queue row (for display) + suggested target. 404 when row missing or drained. **Stock-not-in-queue → 404** (e.g., already at Storage zone — that's a Transfer workflow, not Putaway).
- `POST /putaway/submit/{stockId}` — calls `IPutawayService.PutawayStockAsync`. Operator override (`ToLocationCode`) wins; suggestion is the implicit fallback — both go through the same warehouse-scoped location lookup. Bounce-to-queue on success. Service exceptions (insufficient stock, same source/dest) → bounce back to task with error.

**DAL extensions** (2 new methods on `IStockRepository` + 2 record DTOs):
- `PutawayQueueRow` — JOIN-rich projection (Locations + Zones + Products + Owners + Lots + Pallets + UoMs) for per-card render. LEFT JOINs on Lots + Pallets so non-lot/pallet products still render.
- `SuggestedLocationResult` — top-1 storage-zone candidate + reason list.
- `GetPutawayQueueAsync(warehouseId, ct)` — Stock at `Zone.Type IN ('Receiving','Staging')` with positive OnHand, FIFO oldest first.
- `GetSuggestedPutawayLocationAsync(warehouseId, productId, ct)` — Storage-zone candidates (`IsActive=1`, `Status='Active'`) scored by:
  1. **Same-product Stock count DESC** — cluster picks (existing same-product Stock at the location → raises pick-face hit rate)
  2. **BinRank ASC** — BC pattern: lower fills first
  3. **IsPickface ASC** — preserve dedicated pick faces for pulls
  Returns null when no Storage-zone location qualifies. Reasons rendered as pre-formatted strings (e.g., `"Same product nearby (3 stock rows)"`, `"Low bin rank (15)"`, `"Pick face (last-resort target)"`). Capacity-aware tie-break is a TD — needs product-volume data not seeded today.

**UI** (2 new views — replace Phase 1):
- `Putaway/Index.cshtml` — queue. Pure CSS `.pw-*` token namespace, `.no-scrollbar`. Header: "Putaway / N items awaiting putaway". Chip row: All / Today / Aged (>24h flagged amber). Cards: ProductCode (mono) + ProductName · qty · UoM, source Location code (mono) + wait time + Lot (when applicable). Aged cards get amber border-left.
- `Putaway/Task.cshtml` — 3-section per spec:
  1. **Item card** (purple-accent border-left): package icon + product code/name + 2-col meta grid (From location | Qty available + UoM) + conditional Lot/Pallet rows
  2. **Suggested location hero** (green-accent #1D9E75 — semantic success): "Suggested location" label + BIG location code (22px mono purple) + zone metadata + "Why this location" section with green ✓ bullets per reason. When no suggestion: amber callout "No suggestion. Scan a target bin below."
  3. **Submit form**: override scan area (2px dashed primary border, mono input) + Quantity input (defaults to full Stock.OnHand) + sticky-bottom Submit (purple #534AB7, NOT spec's GREEN — green reserved for the suggestion card). Dynamic submit label: `"Confirm putaway → {target code}"`. "Back to queue" link below sticky submit.

**Sidebar**: "Putaway (mobile)" entry under Inbound after "Receive (mobile)". `inboundActive` Or-chain already covers Putaway via `IsActive("Putaway")`.

**Manifest**: theme color updated `#1f2937` → `#534AB7` (matches design system). Scope/start_url already correct.

**Tests** (+14 net):
- Index: NoWarehouse redirect + Happy returns view with queue rows
- Task: NoWarehouse redirect · StockNotFound 404 · StockEmpty 404 (OnHand≤0) · StockNotInQueue 404 (positive OnHand but at non-staging zone) · Happy with suggestion · Happy with no-suggestion (still renders, amber callout)
- Submit: NoWarehouse redirect · ZeroQuantity rejected (no service call) · StockNotFound 404 · NoOverrideNoSuggestion rejected · HappyWithSuggestion bounces to queue (verifies PutawayRequest construction + carton-style success message) · ServiceThrows redirects back

Submit's override-code path uses `ITenantConnectionFactory` + inline Dapper which can't be cleanly mocked without a service-provider fixture (same TD-041 family as Phase 18 ReceiveController inline location resolver). Suggestion-fallback path IS exercised end-to-end.

Test posture: **879 passing** (was 865 / +14). 288 unit + 586 integration + 5 skipped.

**Out of scope** (logged for follow-up):
- **Override-code resolution test** (same TD-041 family as Phase 18 — inline `HttpContext.RequestServices` use)
- **Capacity-aware suggested-location ranking** — needs per-location current vs `CapacityVolumeCubicCm` calc + product volume data (not seeded today)
- **Multi-location split** (one item moved to multiple bins — single-call PutawayService doesn't support it; would need iterative submit)
- **Override-with-reason capture** (Why operator overrode the suggestion — no audit trail field today; ADR-004 putaway header would carry this)
- **Putaway batches** (bulk move: select N items, all to same target)
- **Smart routing** (closest-available-bin algorithm using Location PositionX/Y/Z fields seeded for ADR-011 3D viz)
- **Service worker offline caching**
- **PWA icons** (manifest `icons:[]` empty)
- **Reserve-on-tap** (when operator opens task page, lock the Stock row from another operator picking it up — race today is "tap → other operator drains it → 404 on submit"; service-level CK rejects insufficient stock so no corruption, just operator friction)

**Spec compliance check** (Scenario A with one rename):
- ✅ /putaway accessible from sidebar
- ✅ Queue shows staging items
- ✅ Filter chips (All/Today/Aged) work, no horizontal scrollbar
- ✅ Tap item → task page
- ✅ Suggested location card displays prominently (green hero)
- ✅ Reasons listed as ✓ bullets
- ✅ Override scan area (dashed primary border)
- ✅ Confirm putaway → success → bounce to queue
- ✅ Hidden scrollbars throughout
- ✅ Touch targets ≥ 38px
- ✅ PWA installable (manifest with #534AB7 theme)
- ⚠️ Spec said `IsStaging` flag → reality `Zone.Type IN ('Receiving','Staging')`, applied silently
- ⚠️ "Capacity available" reason → deferred (TD; needs product-volume data)
- ⚠️ "Pick zone match" reason → adapted to "Pick face (last-resort target)" since putaway should AVOID pick faces (operator pulls from there, not pushes to it)

**Notes**: Audit caught the IsStaging gap in ~10 min (grep returned zero, then I read Migration_20260504_006 for Zones and found `Zone.Type` enum with the exact 'Staging' value). 3rd instance of the same audit→rename pattern — wrote `feedback_spec_rename_audit.md` per the user's brief on third-occurrence threshold. Build clean, tests green, no chunk hiccups.

### Day 10 — Phase 19 (Mobile Pack PWA — Path D per spec audit)

**Branch**: `feat/mobile-pack-pwa` → merged to `main` · **Tag**: `v2.5.0-mobile-pack` · **Spec**: `docs/mockups/mobile-specs/phase-19-mobile-pack-spec.md` (497 lines) + Path D corrections appended in T3

Second mobile-suite expansion (third mobile PWA after Phase 16 picker + Phase 18 receive). Pre-implementation audit caught material spec-vs-backend mismatches; user picked **Path D** (per-line card pattern, no scan UI, mirror Phase 18 receive ~70%). Shipped in ~1.5h vs 5h spec estimate — pattern reuse + dropping smart scan = bigger savings than Phase 18.

**Audit findings (resolved before T1, all locked decisions)**:
1. **Serial inventory table missing.** No `master.ProductSerials`, no `inventory.LotSerials`, no schema for serial inventory anywhere. Spec's smart-scan-by-serial cannot exist without ~2-3h schema add. **Decision D-A**: Defer all serial logic to Phase 19.5 (TD-043). Bundle with TD-040 (mobile receive serial entry) and TD-042 (scan-incremental UX) when the serial schema lands.
2. **PackTask state is 3-state, not 5-state.** Per `IPackTaskService.cs` lines 4-13 + memory `feedback_state_machine_minimalism.md`: `Pending → Packed | Cancelled` only. No `Packing` intermediate state. Spec's queue chip "[Pack {N}]", progress bar, and "Resume" CTA assume an InProgress state that doesn't exist. **Decision D-B**: Drop Packing chip + progress bar + Resume. Queue shows Pending only. Same shape as Phase 18 receive's "Open + Receiving" active queue.
3. **TrackingMethod value: `'Lot'` not `'LotOnly'`.** Spec used the wrong enum string. Applied silently (Phase 18 already used `LotAndSerial` correctly).
4. **Pack workflow is batch-submit, not scan-incremental.** `IPackTaskService.SubmitAsync` takes `IReadOnlyList<PackedLineEntry>` + carton metadata in **one shot**. Spec's "scan items into carton one at a time" UX implies an iterative service that doesn't exist. **Decision D-C**: Mirror Phase 18 receive's per-line card pattern. Each PackTaskLine gets Picked (read-only) + Packed input + quick-adjust + variance, carton metadata strip at bottom, single Submit.
5. **Single-carton MVP** per `UX_Cartons_PackTask` UNIQUE (memory `feedback_one_to_one_via_unique_for_future_n_to_m.md`). Already noted in spec's deferred section.

**Path D scope**: Per-line cards (Expected = PickedQuantity, Packed input). Quick-adjust -10/-1/+1/+10. Variance indicator (Match / Short / Skipped — color-coded). Status select (Packed | Skipped) — Skipped zeros qty + flips reason required. ShortPackReason input. Carton metadata strip (BoxType + Weight + Notes) at bottom. Single Submit button (purple #534AB7 — matches Phase 18 mobile shell + sidebar, **NOT** spec's GREEN per user direction). Cancel via `window.prompt()` with required 3-char reason. Bounce-to-queue on submit.

**Zero new schema, zero new service code, zero new DAL** — pure presentation-layer addition reusing `IPackTaskService.SubmitAsync` + `CancelAsync` from Phase 14D, `IPackTaskRepository.GetPagedAsync` from Phase 15A, and the existing desktop `SubmitPackTaskViewModel` + `PackedLineRow` for model binding.

**Surfaces (4 actions on the new PackController)**:
- `GET /pack` — queue. Single paged call (Pending FIFO). Page size 50. Empty state with `ti-package-off` icon. Tap → /pack/{id}.
- `GET /pack/{taskId}` — task page. Loads `PackTaskDetail` + bulk product meta (`GetMetaByIdsAsync`) + BoxType lookup. Non-Pending tasks return 404 — operator hits desktop `/PackTasks/Detail/{id}` for terminal tasks.
- `POST /pack/submit/{taskId}` — projects `SubmitPackTaskViewModel` into `SubmitPackTaskRequest`, hits `IPackTaskService.SubmitAsync`. Bounce-to-queue on success. Serial-tracked guard rejects whole submit with "use desktop / TD-043" banner. Service exceptions (state violations) → bounce back to task with error.
- `POST /pack/cancel/{taskId}` — `window.prompt`-driven reason capture. Controller-level 3-char min reason gate (mobile bypasses FluentValidation since reason comes via prompt(), not a model-bound VM — same shape as Phase 16 picker cancel). Idempotent on already-Cancelled.

**UI**:
- `Pack/Index.cshtml` — pure CSS, `.pk-*` token namespace, `.no-scrollbar` utility. Mobile-card list with PackNumber (mono) + SoNumber + customer + LineCount + GeneratedByName. Empty state.
- `Pack/Task.cshtml` — per-line cards. Each card: line number + product code (mono) + product name + Picked/Packed stats grid + quick-adjust row + live variance indicator + collapsible Status select / Reason / Notes fields. Carton metadata strip below all lines (green-accent border, BoxType select + Weight number input + Notes textarea). Sticky bottom Submit (purple) + Cancel button (separate form, prompt-driven). Default Packed = PickedQuantity (zero-click submit when everything matches).
- **Serial-tracked banner** (`TrackingMethod=='LotAndSerial'`): amber callout, qty input + quick-adjust + collapsible fields all disabled, "use desktop / TD-043" message — same shape as Phase 18 receive.

**Sidebar**: "Pack (mobile)" entry under Outbound, after "Pick (mobile)". `outboundActive` Or-chain widened to include "Pack" route highlighting.

**Tests** (+15 net):
- `Index_NoWarehouse_RedirectsToSelectWarehouse` — guard
- `Index_Happy_ReturnsViewWithPendingTasks` — verifies Pending-only filter + view binds list
- `Task_NotFound_Returns404` + `Task_TerminalStatus_Returns404` Theory (Packed/Cancelled both → 404) + `Task_Happy_LoadsViewWithMetadata` — confirms ViewBag.SoNumber + ProductMeta plumbing
- `Submit_NoWarehouse_RedirectsToSelectWarehouse` + `Submit_TaskNotFound_Returns404` + `Submit_SerialTrackedLine_RejectedWithUseDesktopMessage` (verifies TD-043 guard fires + service NOT called) + `Submit_Happy_BouncesToQueue` (verifies Index redirect + carton number in success message) + `Submit_ServiceThrows_RedirectsToTaskWithError` (state-violation surface)
- `Cancel_BlankReason_RedirectsToTaskWithError_NoServiceCall` + `Cancel_TooShortReason_Rejected` (≥3-char gate) + `Cancel_Happy_BouncesToQueue` + `Cancel_AlreadyCancelled_IdempotentMessage`

Submit happy path **IS** exercised end-to-end (unlike Phase 18's TD-041) because PackController has zero inline service-locators — every dep is constructor-injected. Lesson learned from TD-041: don't reach into `HttpContext.RequestServices` even for one-off helpers; the test friction outweighs the injection ceremony savings.

Test posture: **865 passing** (was 850 / +15). 288 unit + 572 integration + 5 skipped.

**Out of scope** (logged as TD-042 + TD-043 — same Phase 19.5 family as TD-040):
- **TD-042**: Mobile pack — scan-incremental UX (operator scans item → appends to active carton → close carton). Needs either (a) backend service rework for incremental writes, or (b) frontend session that accumulates scans then converts to per-line PackedQuantity at Submit.
- **TD-043**: Mobile pack — smart-scan with serial detection (auto-detect product code vs serial number, validation chain UI). Blocked on serial inventory schema — bundle with TD-040 + TD-042.
- Multi-carton splitting (UNIQUE drops in future migration; spec already noted).
- Carton hero card with gradient + real-time weight estimate (spec's aesthetic; carton strip is sufficient for MVP).
- Urgency grouping in queue (spec's UX; data model has no priority/ship-date on PackTask itself, would need denormalization).
- "Scan SO/carton to start" bottom action (no scanner integration).
- Always-focused barcode input.
- Service worker offline caching.
- PWA icons (manifest's `icons:[]` empty).

**Spec compliance check** (Path D corrections applied):
- ✅ Queue page (Pending tasks)
- ✅ Task page with per-line cards
- ✅ Carton metadata section (strip, not gradient hero)
- ✅ Sticky-bottom submit + cancel
- ✅ Native `window.prompt()` for cancel reason
- ✅ `.no-scrollbar` applied throughout
- ✅ Touch targets ≥ 38px (quick-adjust 32px borderline same as Phase 18)
- ✅ Bounce-to-queue UX on submit
- ✅ PWA manifest with design-system theme color
- ✅ Serial-tracked products show desktop redirect banner (mirrors Phase 18 TD-040)
- ⚠️ Smart scan endpoint (spec §"Smart Scan Detection") → deferred TD-043
- ⚠️ Carton hero card with gradient → simplified to carton metadata strip
- ⚠️ Urgency grouping in queue → flat FIFO list (no per-task priority data)
- ⚠️ GREEN submit button → PURPLE per user direction (matches Phase 18 mobile shell)

**Notes on chunk-by-chunk hiccups**: None. Pre-implementation audit ran ~15 min and avoided the entire serial-table rabbit hole. Pattern reuse from Phase 18 receive hit ~80% as predicted (Task.cshtml structure ports almost line-for-line; only adaptation was Status select + ShortPackReason flow). Constructor injection from the start kept all 13 test methods covering every endpoint.

### Day 10 — Phase 18 (Mobile Receive PWA — first mobile-suite spec-driven build)

**Branch**: `feat/mobile-receive-pwa` → merged to `main` · **Tag**: `v2.4.0-mobile-receive` · **Replaces**: Phase 1 single-page ReceiveController · **Spec**: `docs/mockups/mobile-specs/phase-18-mobile-receive-spec.md` (380 lines) + `docs/mockups/mobile-specs/mobile-design-system.md` (450 lines, foundation for Phase 18-22+)

First mobile phase implemented from a pre-written spec. The spec set both the design language (purple #534AB7 primary, semantic colors, 38-44px touch targets, .no-scrollbar utility, etc.) and the per-screen layout. Phase 16 mobile picker established the implementation pattern; Phase 18 follows it ~80% and adds spec-driven components.

**Audit findings (resolved before T1)**:
- `master.Products.IsSerialTracked` — **doesn't exist as named**. Schema has `TrackingMethod` enum: `None | Lot | LotAndSerial`. Decision 1B: read `TrackingMethod == "LotAndSerial"` as the serial trigger; no migration needed.
- `PostReceivingLineRequest` has no serial field. Decision 2B: defer serial entry UI to Phase 18.5; submit-time guard rejects serial-tracked lines with a "use desktop" banner. Logged as TD-040.
- Phase 1 `ReceiveController` already exists at `/receive` (single-page form). Decision 3A: replace entirely. The Phase 1 single-page UX is retired; new queue+task is the production target. Posted.cshtml + ReceiveFormModel deleted.

**No new schema, no new service code** — pure presentation-layer addition reusing `IReceivingHeaderService.PostReceivingAsync` from Phase 9 + `IPurchaseOrderRepository.GetPagedAsync` from Phase 9A + new `IProductRepository.GetMetaByIdsAsync` (the only DAL extension — bulk product lookup for the per-line render).

**Surfaces (4 actions on the new ReceiveController)**:
- `GET /receive` — queue. Two paged calls (Open FIFO + Receiving FIFO). Receiving on top (returning operator), Open below.
- `GET /receive/{poId}` — task page. Loads PO detail + bulk product meta (one round-trip via `GetMetaByIdsAsync`). Closed/Cancelled POs return 404.
- `POST /receive/submit/{poId}` — projects `MobileReceiveSubmitViewModel` into `PostReceivingRequest`. Server-side ReceivingNumber assignment (`RCV-YYYYMMDD-HHmmss-{poId8}`). Bounce-to-queue on success. Lines with qty=0/null silently dropped. Serial-tracked lines reject the whole submit with field-pointing error.
- `POST /receive/cancel/{poId}` — operator backs out, no DB state to revert (receipts only persist on submit). Reason captured for future audit. Idempotent.

**DAL extension**:
- `IProductRepository.GetMetaByIdsAsync(ids)` → `IReadOnlyDictionary<Guid, ProductLineMeta(Code, Name, TrackingMethod)>`. Bulk projection for the per-line render — one round-trip vs N per-line lookups. Uses Dapper `@ids` list expansion (good up to ~2100 params).

**UI**:
- `Receive/Index.cshtml` — queue. Filter chips (All/Open/Receiving with counts) per design-system spec. PO cards with monospace number + status badge (Open=primary-light, Receiving=warning-light) + vendor meta + stats row. Tap → /receive/{poId}. Empty state with `ti-inbox` icon. TempData banners.
- `Receive/Task.cshtml` — per-line cards. Each card: line number + product code (mono) + product name. Stats grid: Expected (gray=system) | Received (purple input). Already-received hint when `ReceivedQuantity > 0`. Quick-adjust row (-10/-1/+1/+10, 32px tap targets). Live variance indicator (green ✓ Matches / amber ↓ N under / red ↑ N over). Collapsible fields below: Location code (required) + Lot (optional) + Pallet (optional). **Serial-tracked banner** (`TrackingMethod=='LotAndSerial'`): amber callout, qty input + quick-adjust + collapsible fields all disabled, "use desktop" message. Sticky bottom: Submit + Cancel (window.prompt for reason). Default Received qty = Outstanding (Expected − ReceivedSoFar) so common case is zero-click.
- Both views: pure CSS (no Bootstrap reliance for the receive-specific chrome — keeps the mobile look pixel-true to the spec without fighting Tabler defaults).
- Manifest: `/receive/manifest.json` updated to design-system theme color #534AB7 + name "WMS Receive".

**Sidebar**: "Receive (mobile)" entry under Inbound was already in place from Phase 1 — no change needed.

**Tests** (+12 net):
- `Index_NoWarehouse_RedirectsToSelectWarehouse` — guard
- `Index_Happy_MergesReceivingThenOpen` — verifies queue ordering invariant
- `Task_NotFound_Returns404` + `Task_TerminalStatus_Returns404` Theory (Closed/Cancelled both → 404) + `Task_Happy_LoadsProductMetadata`
- `Cancel_BlankReason_RedirectsWithDiscardedMessage` + `Cancel_WithReason_IncludesReasonInMessage`
- `Submit_NoWarehouse_RedirectsToSelectWarehouse` + `Submit_PoNotFound_Returns404` + `Submit_AllLinesBlank_RedirectsBackWithError` + `Submit_SerialTrackedLine_RejectedWithUseDesktopMessage` (verifies the TD-040 guard fires + service is NOT called)

Submit happy path is **NOT** exercised end-to-end — the inline location resolver uses `HttpContext.RequestServices.GetRequiredService<ITenantConnectionFactory>` which can't be cleanly mocked without a service-provider fixture. Logged as **TD-041** (same family as TD-006 SQL fixture).

Test posture: **850 passing** (was 838 / +12). 288 unit + 557 integration + 5 skipped.

**Out of scope** (logged as TD-040 family — also see ADR-009 TD-039 family for related mobile pack notes):
- Mobile receive — serial entry mode (per spec section §"Serial-tracked Sub-state"; needs `PostReceivingLineRequest.SerialNumbers` + a `inventory.LotSerials` table). **TD-040** = Phase 18.5.
- Mobile receive — controller submit happy-path test (needs service-provider fixture for inline location resolver). **TD-041**.
- Mobile receive — always-focused hidden barcode input
- Mobile receive — `navigator.vibrate` feedback on scan
- Mobile receive — service worker offline caching
- Mobile receive — PWA icons (manifest's `icons:[]` empty)
- Mobile receive — per-line wizard (one-line-at-a-time)
- Mobile receive — 4-tier scan flow (Location → Pallet → SKU → Lot)
- Mobile receive — multi-receipt session (combining multiple POs)
- Mobile receive — auto-resolve location from product's expected put-zone (currently operator types it)

**Spec compliance check**:
- ✅ Queue with chip filters (All / Open / Receiving) + counts
- ✅ PO cards per spec (status badges, mono PO number, vendor meta, stats row)
- ✅ Task page breadcrumb + vendor info bar + per-line cards
- ✅ Stats grid (Expected vs Received with semantic colors)
- ✅ Quick-adjust buttons (-10/-1/+1/+10)
- ✅ Live variance indicator (Match/Under/Over color-coded)
- ✅ Sticky-bottom submit + cancel
- ✅ Native `window.prompt()` for cancel reason
- ✅ `.no-scrollbar` applied throughout
- ✅ Touch targets ≥ 38px (chip 26px is borderline but spec allows; quick-adjust 32px; submit 46px)
- ✅ Bounce-to-queue UX on submit
- ✅ PWA manifest with design-system theme color
- ⚠️ "Serial-tracked products show serial entry mode" → ships as "show desktop redirect banner" (TD-040)

**Notes**:
- Pattern reuse hit ~80% as predicted — Phase 16's PickController shape ported almost line-for-line.
- The Phase 1 single-page form retirement was a clean swap; no orphan references after deleting `ReceiveFormModel.cs` + `Posted.cshtml`.
- Pre-written spec was a major velocity multiplier — design decisions (colors, components, behavior) were already locked, freeing the chunk to focus on plumbing.

### Day 10 — Phase 17 (Hangfire + Pack Video MVP — ADR-009)

### Day 10 — Phase 17 (Hangfire + Pack Video MVP — ADR-009)

**Branch**: `feat/pack-video` → merged to `main` · **Tag**: `v2.3.0-pack-video` · **Closes**: deferred items from v2.0.0 outbound-mvp callout (pack video + automatic retention) · **Publishes**: ADR-009 Pack Video

Two components shipped together because retention closes the privacy/storage gap that pack video would otherwise leave open.

---

**PART A — Hangfire infrastructure** (foundation; reusable for future jobs)

- `Hangfire.AspNetCore` + `Hangfire.SqlServer` 1.8.14
- Storage: **WMS_Master DB** (system DB; per-deployment, single dashboard, single job queue — NOT per-tenant). Schema name `HangFire`, auto-prepared on first run via `PrepareSchemaIfNecessary=true`.
- Server: `WorkerCount = min(4, ProcessorCount)` (sized for dev; production override expected). `ServerName = "{MachineName}:wms-web"` so multi-instance deployments don't collide. 5-min `CommandBatchMaxTimeout` + `SlidingInvisibilityTimeout`. `DisableGlobalLocks=true` per SQL Server recommendations.
- Dashboard at `/hangfire` — gated by `HangfireDashboardAuthFilter` (custom `IDashboardAuthorizationFilter`). MVP requires `IsAuthenticated` only; tightening to ADMIN role check via `IPermissionService` is a TD logged in the file.
- Foundation enables: pack-video retention (Phase 17), email notifications, stock-aging cleanup, scheduled reports, SO auto-allocation, cycle-count scheduling — all future TDs.

---

**PART B — Pack Video MVP** (record + store + playback + auto-cleanup)

**ADR-009** drafted in `docs/decisions/ADR-009_Pack_Video.md`. Covers: recording trigger (operator click), upload coupling (separate POST endpoint), browser format (WebM/VP9), storage path (reuse `IDocumentStorageService`), permissions (`OUTBOUND.ORDERS`), retention (10-day auto), microphone (muted by default — privacy), 11-item TD-039 family for deferred sub-features. Explicit alternatives section rejecting coupled-to-submit, continuous recording, server-side transcoding, audio capture, per-station policy enforcement.

**Schema** (1 migration):
- `20260510_032` — `outbound.PackVideos`. PackTaskId FK CASCADE; DocumentFileId FK NO ACTION → `documents.Files` (the actual blob; retention job deletes documents.Files first then PackVideos, so the FK never blocks). DurationSec int (captured client-side from MediaRecorder). RecordedAt + RecordedBy audit pair. 2 indexes (per-task playback lookup + retention-job WHERE filter). `CK_PackVideos_DurationSec_NonNegative`. No Version (recordings appended, never edited; matches PackTaskLines/Cartons convention).

**Storage options widening**:
- `MaxFileSizeMB`: 25 → 50 (covers a 60-second 720p WebM ~30 MB typical)
- `AllowedExtensions`: + `.webm` + `.mp4` (mp4 future-proofing for server-transcoding TD)
- `appsettings.json` mirrored

**Service** (`IPackVideoService` in `WMS.Web.Services.Outbound` — lives in Web because dependency is `IDocumentStorageService` which is also Web-layer):
- `UploadAsync(tenantId, packTaskId, content, fileName, contentType, durationSec, currentUserId)` — validates pack task is `Packed` (Pending → no carton sealed yet, video meaningless; Cancelled → won't ship). Storage write FIRST via `IDocumentStorageService.UploadAsync` (entityType=`PackTask`, category=`PackVideo`). Metadata INSERT after. **No TX** — if metadata insert fails, the orphan blob is collected by the retention job within 10 days.
- `GetStreamAsync` — lookup metadata → resolve DocumentFileId → storage stream. Returns null when either side is missing (handles rare race where retention ran between the two reads).
- `DeleteAsync` — storage delete first, then metadata. Mirrors retention pattern; storage failure leaves metadata for re-attempt rather than orphan blobs.
- `GetLatestForPackTaskAsync` — UI surface for the "Watch video" link.

**Controller** (3 endpoints added to `PackTasksController`):
- `POST /PackTasks/UploadVideo/{id}` — `IFormFile` + `durationSec` form field. `RequestSizeLimit(60 * 1024 * 1024)` attribute (storage validates 50MB; extra 10MB allows HTTP framing). Returns JSON `{videoId}` on success — client updates UI without full reload. `StorageValidationException` → 400 with friendly message; `InvalidOperationException` → 400 (state errors).
- `GET /PackTasks/Video/{videoId}` — playback. `File()` result with `enableRangeProcessing=true` so the framework handles simple Range requests over the FileStream (proper streaming TD).
- `DELETE /PackTasks/Video/{videoId}` — admin/debug. NoContent on success, NotFound when missing.

**UI** (`_PackTaskVideoPanel.cshtml`):
- New custom tab "Video" on `Detail` (status-conditional — only shown on Packed tasks; tab count badge shows 1 if a video exists, 0 if not).
- Browser MediaRecorder integration: 1280x720 prefer, **audio:false** per ADR-009 (privacy + bandwidth). Prefers VP9 codec, falls back to whatever browser offers.
- Live `<video>` preview while recording. Elapsed-second counter; "past 60s soft cap" warning past the limit (no hard stop).
- Stop button finalizes recording, builds Blob, posts via fetch+FormData. On success: full page reload (in-place insert is a TD).
- Anti-forgery token via dedicated hidden form rendered by the panel itself (deterministic — no DOM scraping).
- Safari handling: detects `MediaRecorder` absence + shows `wms-banner-warning` "Chromium browser required, Safari support is a TD".
- Camera permission denied → in-panel error message instead of a console-only failure.
- If a video already exists: HTML5 `<video>` player streaming from `/PackTasks/Video/{id}` above the recorder controls (operator can review + record a new take).

**Retention job** (`PackVideoRetentionCleanupJob`):
- `[DisableConcurrentExecution(timeoutInSeconds: 600)]` + `[AutomaticRetry(Attempts = 2)]`.
- Iterates active tenants from `master.Tenants` (raw Dapper — no purpose-built ITenantsRepository today; if a 2nd job needs it, refactor to a shared interface).
- Per-tenant: `IPackVideoRepository.GetOlderThanAsync(cutoff)` → per-video: `TryDelete` on-disk bytes → `IDocumentRepository.DeleteAsync` → `IPackVideoRepository.DeleteAsync`. Per-video failures logged + skipped (next run idempotently retries; already-deleted rows just disappear from the next GetOlderThanAsync result).
- **Why direct on-disk delete instead of `LocalFileStorageService.DeleteAsync`**: the storage service depends on `ITenantContext` (HTTP-scoped). Job has no HTTP request → would need a mutable `JobTenantContext` + scope-per-iteration. Replicating the path-resolve + delete pattern inline is 5 lines, no DI gymnastics. Documented in the file's header.
- `RecurringJob.AddOrUpdate` registered post-build. Cron `"0 3 * * *"` (03:00 UTC daily, configurable via `appsettings.json` `PackVideoRetention` section). UTC timezone explicit. Stable JobId (`pack-video-retention-cleanup`) so AddOrUpdate replaces rather than duplicates across restarts.

**Tests** (+9 net cases — all video-endpoint):
- `UploadVideo`: NoFile / EmptyFile → 400 guards · Happy → JSON `{videoId}` · `StorageValidationException` → 400 with message · `TaskNotPacked` (service throws InvalidOp) → 400 with message
- `Video` (GET): NotFound → 404 · Happy → `FileStreamResult` with content-type + `EnableRangeProcessing=true`
- `DeleteVideo`: NotFound → 404 · Happy → 204 NoContent

Test posture: **838 passing** (was 829 / +9). 288 unit + 545 integration + 5 skipped.

**Out of scope** (logged as TD-039 in ADR-009):
- Pack video — Safari support via server-side transcoding
- Pack video — PDPA access audit log (`documents.VideoAccessLog` per access)
- Pack video — per-station policy (honor `PackStations.VideoEnabled`, needs admin UI)
- Pack video — per-channel policy (B2C requires, B2B opt-in; needs SO-channel link first)
- Pack video — per-tenant retention override
- Pack video — admin role check on `/hangfire` dashboard
- Pack video — range-request streaming for `GET /PackTasks/Video/{id}` (`enableRangeProcessing=true` is a partial answer)
- Pack video — thumbnail extraction (preview frame on the carton tile)
- Pack video — continuous recording mode
- Pack video — mobile pack PWA with video
- Pack video — finer perm split (`OUTBOUND.VIDEO` read/write)

**Notes**:
- Caught a layering issue mid-T4: initially put `IPackVideoService` in `WMS.BLL.Services.Outbound` but it depends on `IDocumentStorageService` which lives in `WMS.Web.Services.Storage`. BLL → Web is the wrong direction. Moved the service to `WMS.Web.Services.Outbound`. Pragmatic choice — the proper fix is moving the storage abstraction down to BLL/Common, but that's a separate refactor. Documented in commit `12c1873`.
- `RecurringJob.AddOrUpdate` requires the job-id `string` parameter to be stable across restarts (otherwise you get duplicates). Pulled from `PackVideoRetentionOptions.JobId` so it's constant + visible in config.
- 1 new TD added to the operational-followups list: monitor disk usage on the storage root before going to prod — even with retention, peak usage between recording and cleanup can be substantial (10 days × N tasks/day × ~30 MB).

### Day 10 — Phase 16 (Mobile Picker PWA — single-page-per-task MVP)

### Day 10 — Phase 16 (Mobile Picker PWA — single-page-per-task MVP)

**Branch**: `feat/mobile-picker-pwa` → merged to `main` · **Tag**: `v2.2.0-mobile-pick` · **Closes**: post-MVP gap from v2.0.0 callout (mobile picker PWA)

Second post-MVP phase. Operator-facing mobile surface for picking. **Pragmatic single-page-per-task**, NOT the 4-tier scan-flow vision from `docs/01_WMS_Master_Design.md` (deferred TD). Mirrors Phase 1 ReceiveController precedent — operator-friendly flat form, not wizardry.

**Zero schema changes, zero service changes** — reuses Phase 14C `IPickTaskService.SubmitAsync` + `CancelAsync` entry points and Phase 15A `IPickTaskRepository.GetPagedAsync`. Pure presentation-layer addition.

**Surfaces**:
- `/pick/manifest.json` — PWA manifest (scope `/pick/`, start_url `/pick`, standalone display, portrait orientation; no icons yet — TD)
- `GET /pick` — queue. Mobile cards (one per task) listing Pending|InProgress pick tasks. Two paged calls (Pending FIFO + InProgress FIFO via existing `GetPagedAsync`); InProgress at top (returning operator), Pending below (queue order). Page size 50 per status — generous for a single picker session.
- `GET /pick/{id}` — task page. Mobile-card-stacked form (one card per `PickTaskLine`). Per-card: Expected (read-only) + Picked qty input (large, `inputmode="decimal"` for mobile keyboard) + Status select (Picked|Skipped) + ShortPickReason (visible only when needed). Sticky-bottom submit card with live tally. Cancel button below uses native `window.prompt()` to collect the reason — mobile-friendlier than a CSS `:target` modal would be on small screens. Terminal status (Picked|PartiallyPicked|Cancelled) renders read-only summary cards instead.
- `POST /pick/submit/{id}` — projects `SubmitPickTaskViewModel` (reused from desktop) into `SubmitPickTaskRequest`, hits `IPickTaskService.SubmitAsync` (zero service rework). Mobile UX: bounces back to queue on success so operator grabs the next task instead of staring at terminal page.
- `POST /pick/cancel/{id}` — inline `reason` form field, calls `IPickTaskService.CancelAsync`. 3-char min reason validated at controller (mirrors the desktop validator).

**Sidebar**: Outbound submenu now has 5 entries — added "Pick (mobile)" below Shipments. `outboundActive` Or-chain widened to highlight when on `/pick` routes. Same pattern as the existing "Receive (mobile)" sidebar entry under Inbound.

**Auth + tenant**: Inherits the existing 3-step login + warehouse selection. `WarehouseId is null` guards the queue Index → redirects to `/Auth/SelectWarehouse` (matches Receive precedent).

**Tests** (+9 net):
- `Index_NoWarehouse_RedirectsToSelectWarehouse` — guard
- `Index_Happy_MergesInProgressThenPending` — verifies the queue ordering invariant (returning operator's task at top)
- `Task_NotFound_Returns404` + `Task_Happy_ReturnsViewWithSoNumber` — Detail GET, including SO# resolution into ViewBag
- `Submit_Happy_RedirectsToQueue` — verifies route-id-wins-over-form-id + the bounce-to-queue UX (different from desktop which redirects to Detail)
- `Submit_ServiceThrows_RedirectsToTaskWithError` — error path stays on the task page so operator can fix
- `Cancel_BlankReason_RedirectsBackWithError_NoServiceCall` — controller-level reason guard (mobile bypasses FluentValidation since reason comes via prompt(), not a model-bound VM)
- `Cancel_Happy_CallsService_RedirectsToQueue` + `Cancel_AlreadyCancelled_IdempotentMessage`

Test posture: **829 passing** (was 820 / +9). 288 unit + 536 integration + 5 skipped.

**Refactor along the way**: PickController initially inherited `BaseController` (mirroring Receive/Putaway) which resolves `CurrentUser`/`TenantContext` from `HttpContext.RequestServices`. Caught at T3 that this is the minority pattern (only 2-3 mobile controllers use it; none have tests). Refactored to constructor injection in the same chunk before writing tests — matches every other controller in the codebase + makes mocking trivial.

**Out of scope** (logged as TDs):
- 4-tier scan flow (Location → Pallet → SKU → Lot scan-then-validate) per design doc
- Always-focused hidden barcode input
- Vibration feedback (`navigator.vibrate`)
- Real-time SignalR push (status updates while picker is on the floor)
- Smart auto re-allocation if same product+lot found on alternate pallet
- Per-line wizard ("show me ONE line at a time" instead of all-cards-stacked)
- Service worker offline caching (manifest gives PWA install prompt but no offline behaviour yet)
- Pack mobile + Receive mobile improvements
- PWA icons (manifest's `icons: []` is empty — Chrome warns but install still works)
- Mobile picker queue filters (currently shows all open tasks; future: filter to "assigned to me" once per-picker assignment lands)

**Notes**: No phantom-edits, no chunk hiccups except the `BaseController` → constructor-injection refactor caught at T3. The view's `pickForm()` Alpine state mirrors the desktop `_PickTaskLinesPanel` form state shape exactly — same field names + same `needsReason`/`isValid` logic — so future operators flipping between desktop and mobile have the same mental model.

### Day 10 — Phase 15A (Outbound list pages — Pick / Pack / Ship)

### Day 10 — Phase 15A (Outbound list pages — Pick / Pack / Ship)

**Branch**: `feat/post-mvp-list-pages` → merged to `main` · **Tag**: `v2.1.0-list-pages` · **Closes**: TD-036 (PickTasks list) + TD-037 (PackTasks list) + Shipments list (sub-item of TD-038)

First post-MVP phase. Three list pages, one per outbound execution surface, all mirroring the canonical SalesOrders Index template. The Outbound submenu is now complete — operator can browse Pick / Pack / Shipment queues without going through SO Detail's Generate redirect chain.

**No schema changes** — purely read-only DAL extensions + new controller actions + Razor views + sidebar entries. 4 commits across 4 chunks (T1 Pick, T2 Pack, T3 Ship, T4 tests) + 1 doc commit + tag.

**DAL pattern per surface** (3 records + 1 mapper + 2 repo methods):
- `XxxListRow` record — read-projection JOINing `outbound.SalesOrders` + `master.Customers` for SoNumber + customer label, plus per-task aggregates (LineCount for Pick/Pack via CTE; CartonCount for Ship via filtered-index-friendly CTE on `outbound.Cartons WHERE ShipmentId IS NOT NULL`)
- `XxxFilter` record — Page / PageSize / Search / Status / SortBy / SortDesc
- `XxxStatusCounts` record — chip aggregate (5/4/4 fields for Pick/Pack/Ship per their state-machine widths)
- `XxxSortMapper` — closed-set whitelist (SQL-injection defence; mirrors Phase 14A `SalesOrderSortMapper`)
- `IXxxRepository.GetPagedAsync` + `GetStatusCountsAsync` — paged read with JOINs + chip-count single SUM(CASE) aggregate respecting Search but ignoring Status (so inactive chips still display totals)

**Controller pattern per surface** (2 actions added to existing controllers):
- `GET /Xxx` — `Index()` returns View
- `GET /Xxx/Data` — JSON envelope `{ items, total, page, pageSize, totalPages, counts }` with status filter going through `XxxStatusMapper.FromWire` (case-insensitive) and `ToWire` on response. Sorted via `sortBy` query parameter (whitelist-validated).

**View pattern per surface** (1 Razor file):
- `Views/Xxx/Index.cshtml` — Alpine `xxxList()` state with debounced search + status chip strip + sortable table + Prev/Next pagination. Click row → `/Xxx/Detail/{id}`. `badgeClass` map matches `XxxStatusMapper` variant assignments. **No "New" button** for any of the 3 — pick / pack / ship tasks are generated from upstream entities, never created directly.

**Per-surface specifics**:
- **PickTasks** (T1): 5 chip states (Pending / In progress / Picked / Partial / Cancelled), 7-col table (Pick # / SO # / Customer / Status / Lines / Generated / By). Search matches PickNumber OR SoNumber.
- **PackTasks** (T2): 3 chip states (Pending / Packed / Cancelled — same minimalism as 14D), 7-col table. Search matches PackNumber OR SoNumber.
- **Shipments** (T3): 3 chip states (Pending / Shipped / Cancelled), **8-col table** (Shipment # / SO # / Customer / Status / **Carrier / Tracking** / Cartons / Generated). **Search matches THREE columns**: ShipmentNumber + SoNumber + TrackingNumber — operators commonly look up shipments by tracking when a customer calls; making it a first-class search target avoids "find the SO first" friction. Single-table (no per-line aggregate) but DOES carry a per-shipment carton count via CTE leveraging the Phase 14E `IX_Cartons_Shipment WHERE ShipmentId IS NOT NULL` filtered index.

**Sidebar**: Outbound submenu walks forward chunk-by-chunk:
- After T1: Sales Orders + Pick Tasks
- After T2: Sales Orders + Pick Tasks + Pack Tasks
- After T3: Sales Orders + Pick Tasks + Pack Tasks + Shipments (complete — every outbound surface reachable)

**Tests** (T4, +9 net): 3 tests per controller × 3 surfaces. Tight pattern:
- `Index_ReturnsView` — trivial guard
- `GetData_Happy_ReturnsItemsAndCounts` — stub repo with 1 item + state counts; assert envelope shape + counts shape per surface
- `GetData_StatusFilter_MappedToDb` — wire `'partiallypicked'`/`'packed'`/`'shipped'` (lowercase) → DB `'PartiallyPicked'`/`'Packed'`/`'Shipped'` via the relevant StatusMapper.FromWire; verifies the filter flows through the right `XxxFilter` shape.

Test posture: **820 passing** (was 811 / +9). 288 unit + 527 integration + 5 skipped.

**Out of scope** (still open from MVP TDs and untouched here):
- Mobile picker PWA (was 14E in original roadmap)
- Pack video (ADR-009 spec needed)
- `master.Carriers` FK lookup integration (carriers seeded as Inactive blocks dropdown)
- Multi-shipment per SO + multi-carton splitting (UNIQUE drops in future migrations)
- Carrier API / label printing / tracking auto-assignment
- Manifest workflow (Build → Seal → Handover with driver signature)
- Tracking events ingestion
- Post-Submit reversal ("return to stock") for any of pick/pack/ship
- Post-Submit edit on shipment metadata (operator may want to add tracking number after dispatch)
- ScanEach vs ScanAndQty per-product pack modes
- Per-list saved filters / column customisation / CSV export

**Notes on chunk-by-chunk hiccups**: None. Highest-reuse phase yet — T2 and T3 were near-mechanical translations of T1's pattern. Total time well under half-day estimate.

### Day 10 — Phase 14E (Ship Workflow — desktop dispatch form, MVP single-shipment)

**Branch**: `feat/outbound-ship` → merged to `main` · **Tag**: `v1.8.0-ship` · **Closes**: Outbound MVP chain end-to-end (SO → Allocate → Pick → Pack → Ship)

The dispatch half of the outbound pipeline. Pick (14C) decremented stock; Pack (14D) recorded the carton; this phase records the dispatch (carrier + tracking + cartons stamped + SO Packed → Shipped). **No Stock writes** — ship is post-stock; the qty already left inventory at pick submit. Ship is structurally the simplest of the three execution phases — single-table service, no per-line breakdown, no `ValidateRequestShape` (carrier + tracking are just optional strings).

**Schema** (3 migrations — smaller phase than 14C/14D since most conventions were already paved):
- `20260510_029` — DROP + ADD `CK_SalesOrders_Status` to widen the enum from 9 → 10: adds `Shipped` between `Packed` and `Cancelled`. Down() reverses to Phase 14D set. Same widening pattern as 14B's _018 / 14C's _021 / 14D's _025.
- `20260510_030` — CREATE `outbound.Shipments` header. **3-state machine** (`Pending → Shipped | Cancelled` — same minimalism as 14D Pack). Per-state audit trio (`GeneratedBy/At` always set; `ShippedBy/At` + `CancelledBy/At + CancelReason`) + `CK_Shipments_AuditMatchesStatus` invariant. **Free-text `CarrierName VARCHAR(50) NULL`** + **nullable `TrackingNumber VARCHAR(100)`** — the deferred-default carrier pattern (operator may not have either at ship time). The codebase has a full `master.Carriers` table + 4 seeded carriers (FLASH/KERRY/JT/THAIPOST) but ALL `Status='Inactive'`; the existing `GetActiveAsync` filters Production-only and would render an empty dropdown in dev. FK lookup integration is a TD for v2.x once admins promote carriers. `UX_Shipments_SalesOrder` UNIQUE enforces 1:1 SO → Shipment for MVP (multi-shipment splitting drops the UNIQUE in a future migration).
- `20260510_031` — ALTER `outbound.Cartons` ADD `ShipmentId` nullable FK + filtered index (`WHERE ShipmentId IS NOT NULL` — most cartons are pre-ship NULL; index stays small). `ShipmentService.SubmitAsync` stamps every carton belonging to the SO with the new ShipmentId inside its TX (resolved via `PackTask.SalesOrderId` join — single SQL UPDATE).

**Service** (`IShipmentService`, 3 lifecycle methods):
- `GenerateAsync(tenantId, salesOrderId, currentUserId)` — **lightest of the three; no TX needed** (single repo INSERT). Loads SO, validates state (`Packed` only). **Idempotent on existing Pending shipment** — returns its summary so the controller can redirect to the same Detail on accidental re-trigger (Phase 14C/14D precedent). Assigns `SHP-YYYYMMDD-NNNN`. **No SO state flip** — operator sees SO stays Packed while ship is in flight; flips to Shipped only on SubmitAsync. Ship-in-flight detected via existing-task guard, not SO state.
- `SubmitAsync(tenantId, request, currentUserId)` — TX-wrapped commit (3 writes inside one TransactionScope): (1) `shipmentRepo.SetShippedAsync(carrierName, trackingNumber, notes, ...)` flips Pending → Shipped + stamps dispatch metadata + audit (single UPDATE, COALESCE on Notes preserves any pre-Submit value if operator left blank); (2) `cartonRepo.StampShipmentForSalesOrderAsync(soId, shipmentId)` bulk UPDATE Cartons SET ShipmentId via JOIN through PackTask.SalesOrderId (single SQL); (3) `soRepo.SetStatusAsync(soId, "Packed", "Shipped")`. Service trims operator inputs to column widths (Carrier 50 chars, Tracking 100 chars). Empty strings normalised to null. **No Stock writes** — ship is post-stock.
- `CancelAsync(tenantId, shipmentId, reason, currentUserId)` — **even lighter than Pack's Cancel; no TX needed** (single repo write). `Pending → Cancelled` with required reason. **No SO state flip** — Generate didn't flip the SO, so Cancel doesn't either; SO stays Packed, ready for re-Generate. **No carton un-stamping** — Pending shipments haven't claimed any cartons (`StampShipmentForSalesOrderAsync` only fires on SubmitAsync's TX). Idempotent on already-Cancelled (returns false). Rejects `Shipped` (post-Submit terminal — return-to-stock is a future TD).

**UI** (4 surfaces touched / created):
- `/SalesOrders/Detail` — new `isShipped` flag + `canGenerateShipment` (Packed only). Quick Action "Generate shipment" added between "Generate pack" and "Cancel" (`ti-truck-delivery` icon). `canCancel` unchanged — Shipped already excluded from `{Draft, Open, Allocating, Allocated}` list.
- `/SalesOrders` Index — Alpine `statuses` array now lists 11 chips; counts envelope expanded with `shipped`; `badgeClass` map widened (Shipped=success — terminal happy state, distinct from Packed=info "ready for ship").
- `POST /SalesOrders/GenerateShipment/{id}` — calls `GenerateAsync`, redirects to `/Shipments/Detail/{newShipmentId}` on success with `ShipmentMessage` carrying ShipmentNumber + reminder to fill carrier + tracking. Error path bounces back to SO Detail with `SalesOrderError`.
- `_SalesOrderDecisionModals.cshtml` — new `#generate-ship-modal` block (info-blue, `ti-truck-delivery` icon, lead text explaining no-SO-state-change semantics + idempotency note).
- `/Shipments/Detail/{id}` — new surface using `_DetailLayout`. Stats: Cartons / Weight / Carrier / Status. SO link in Overview block, plus Carrier + Tracking + Notes when populated. Per-state audit trio in Properties. Custom Dispatch tab via `_ShipmentDispatchPanel`:
  - **Editable form when Pending**: 2-column form (Carrier free-text + Tracking number — both 100% optional with help text noting the deferred-default carrier pattern) + Notes textarea + submit button. Below: cartons section (placeholder pre-Submit, populated post-Submit with CartonNumber + Weight + Notes per row).
  - **Terminal (Shipped / Cancelled)**: no form, only the cartons table (everything stamped at submit time).
- `_ShipmentDecisionModals.cshtml` — new partial. Cancel modal with required 3-500 char reason textarea. Same shell as Phase 11A/12/13/14A/14C/14D modals, `wms-sh-` token namespace.
- Sidebar — Shipments entry deferred to the list-page chunk (Phase 14C/14D precedent; would 404 without an Index action).

**Status mappers** (1 widened, 1 created):
- `SalesOrderStatusMapper` widened to 10 states. `Shipped=success` (terminal happy state — distinct from `Packed=info` "ready for ship"). Header comment rolled back from "14F will widen with Shipping/Shipped/Closed" since Shipped landed in 14E.
- `ShipmentStatusMapper` (new). 3 states. `Pending=neutral`, `Shipped=success`, `Cancelled=neutral`.

**TempData generalization**: `_DetailLayout` banner block now coalesces EIGHT sets of TempData keys (added `ShipmentMessage` / `ShipmentError`). Pattern continues to scale linearly per phase.

**ViewModels + validation** (3 new):
- `SubmitShipmentViewModel` — model-binding shape for `POST /Shipments/Submit`. All fields optional (`CarrierName` / `TrackingNumber` / `Notes`). StringLength caps mirror DB column widths.
- `CancelShipmentViewModel` + `CancelShipmentValidator` — same shape as Phase 14D / 14C cancel VMs.

**DAL extensions**:
- New `IShipmentRepository` (6 methods: `CreateAsync` single INSERT no-TX, `GetByIdAsync` / `GetByNumberAsync` single-row reads, `GetActiveBySalesOrderAsync` pre-generation guard Pending only, `SetShippedAsync` (Pending → Shipped + stamps Carrier+Tracking+Notes — COALESCE on Notes preserves pre-Submit value), `SetCancelledAsync`, `CountForDatePrefixAsync`).
- `ICartonRepository` extensions: `StampShipmentForSalesOrderAsync` (bulk UPDATE via JOIN through PackTask.SalesOrderId — single SQL composes inside ambient TX); `GetByShipmentIdAsync` (read-side for Detail page).
- `PackTaskRepository.CartonColumns` SELECT now includes `ShipmentId` (forward-stable; PackTask Detail surfaces shipment linkage when populated).
- `SalesOrderStatusCounts` widened to 11 fields (+Shipped, between Packed and Cancelled — pipeline-chronological order). `GetStatusCountsAsync` SQL grew 1 new SUM(CASE).

**Tests**: +57 net test cases (~32 distinct methods). Smaller delta than Phase 14D's +68 (which was already smaller than 14C's +101) — Ship is the simplest of the three execution phases. 21 unit (ShipmentService — Generate/Submit/Cancel state-gating + happy paths + trim-and-truncate edge cases including `LongCarrierName_TruncatedTo50`); 36 integration (ShipmentStatusMapper Theory across 3 states; SalesOrderStatusMapper Theory backfill across the now-10-state set; ShipmentsController Detail / Submit / Cancel happy + error + AllOptionalFieldsBlank flow-through; SalesOrders.GenerateShipment happy + error). Test posture: **811 passing** (was 754). 288 unit + 518 integration + 5 skipped.

**Out of scope** (logged as TD-038):
- `/Shipments` Index list page + GetData JSON envelope + chip counts + sidebar entry (joins TD-036 /PickTasks + TD-037 /PackTasks list-page family).
- **`master.Carriers` FK lookup integration** — codebase has full Carrier infrastructure (4 seeded FLASH/KERRY/JT/THAIPOST + lookup repo + Production-status filter) but seeded as Inactive; would render empty dropdown in dev. Free-text MVP avoids the friction. Future: drop the free-text column, add CarrierId nullable FK + dropdown.
- **Multi-shipment per SO** — `UX_Shipments_SalesOrder` UNIQUE enforces 1:1 for MVP. Drop in a future migration to enable splitting (e.g. one SO ships in two batches).
- **Post-Submit reversal** ("return to stock" — Shipped tasks cannot be undone today; needs a separate Adjust+ workflow).
- **Carrier API integration / label printing / tracking number auto-assignment** — operator types both manually for MVP (deferred-default carrier pattern per ADR-009 sketch).
- **Manifest workflow** (Build → Seal → Handover with driver signature capture).
- **Tracking events ingestion** (`outbound.TrackingEvents` table from design doc — webhook receiver framework).

**Permission**: `OUTBOUND.ORDERS` covers shipment operations for MVP. Future phases may introduce a finer `OUTBOUND.SHIP` perm for separation-of-duties on dispatch vs SO admin.

**Notes on chunk-by-chunk hiccups**: None of significance. Pattern reuse from 14C/14D was ~80% as expected; the audit-first approach caught the carrier dropdown gotcha before T1.

### Day 10 — Phase 14D (Pack Task Workflow — desktop "Complete pack" form, MVP single-carton)

**Branch**: `feat/outbound-pack` → merged to `main` · **Tag**: `v1.7.0-pack` · **Foundation for**: Phase 14F (Ship consumes Packed SOs)

The packaging half of the outbound pipeline. Pick (14C) consumed reservations and decremented stock; this phase records what physically went into the carton. Atomic on submit: per-line PackedQuantity + a single Carton row + PackTask flips Pending → Packed + SO flips Picked|PartiallyPicked → Packed. **No Stock writes** — pack is post-stock; the qty already left inventory at pick submit. PackedQty < PickedQty surfaces as a per-line discrepancy (audit-only) but the SO still flips to Packed since the carton is sealed.

**Schema** (4 migrations):
- `20260510_025` — DROP + ADD `CK_SalesOrders_Status` to widen the enum from 8 → 9: adds `Packed` between `PartiallyPicked` and `Cancelled`. Down() reverses to Phase 14C set. SQL Server CHECK widening pattern (Phase 14B's _018 + 14C's _021 precedent).
- `20260510_026` — CREATE `outbound.PackTasks` header. **3-state machine** (`Pending → Packed | Cancelled` — simpler than Pick's 5-state because pack workflow is single-shot for MVP, no Save Progress / InProgress intermediate). Per-state audit trio (`GeneratedBy/At` always set; `PackedBy/At` + `CancelledBy/At + CancelReason`) + `CK_PackTasks_AuditMatchesStatus` invariant. `AssignedTo` nullable (pool mode for MVP). 3 indexes (per-status queue, per-SO, per-AssignedTo).
- `20260510_027` — CREATE `outbound.PackTaskLines`. `SalesOrderLineId` FK (NO ACTION). Snapshot Product/Owner/UoM only — pack doesn't track Lot/Pallet/Location since stock has already left. Quantity progression: snapshot `PickedQuantity > 0` (only positively-picked SO lines spawn pack lines — Skipped pick lines + zero-pick lines do NOT enter the carton) → operator enters `PackedQuantity` in `[0, PickedQuantity]`. Per-line `LineStatus` (`Pending|Packed|Skipped`) + `ShortPackReason` + `CK_PackTaskLines_StatusMatchesQty` + `CK_PackTaskLines_PackedNotOverPicked` invariants. CASCADE on header→lines. No `Version` on lines (matches PickTaskLines / TransferOrderLines / CycleCountLines).
- `20260510_028` — CREATE `outbound.Cartons`. Physical packaging per pack task. **MVP simplification: one carton per task** (`UX_Cartons_PackTask` UNIQUE enforces; multi-carton splitting drops the UNIQUE in a future migration + adds CartonContents many-to-many). `CartonNumber` `CTN-YYYYMMDD-NNNN` UNIQUE. `BoxTypeId` nullable FK to `master.BoxTypes`. `WeightKg` nullable (3-decimal precision matches BoxTypes.EmptyWeightKg — small parcels can shift carrier billing brackets). Created at SubmitAsync time inside the same TX as the task header flip; never edited (operator cancels + regenerates if metadata needs changing pre-Submit).

**Service** (`IPackTaskService`, 3 lifecycle methods):
- `GenerateAsync(tenantId, salesOrderId, currentUserId)` — **lightest of the three; no TX needed** (single repo write). Loads SO, validates state (`Picked` or `PartiallyPicked` only). **Idempotent on existing Pending pack task** — returns its summary so the controller can redirect to the same Detail on accidental re-trigger (Phase 14C precedent). Reject if no `PickedQuantity > 0` lines (nothing to pack). Per positively-picked SO line = one PackTaskLine snapshotting Product/Owner/UoM + the SO line's PickedQuantity (the read-only ceiling). Assigns `PACK-YYYYMMDD-NNNN`. **No SO state flip** — operator sees SO stays Picked|PartiallyPicked while pack is in flight; flips to Packed only on SubmitAsync. Pack-in-flight is detected via the existing-task guard, not SO state.
- `SubmitAsync(tenantId, request, currentUserId)` — heavyweight TX (the headline). Belt-and-suspenders `ValidateRequestShape` (private static): every task line in submission, no extras / dups, `LineStatus ∈ {'Packed','Skipped'}`, qty in `[0, PickedQuantity]`, `ShortPackReason` required when `packed < picked` OR `Skipped` (Skipped IS a short — the full Picked qty), Carton.WeightKg non-negative if supplied. Inside TX: (1) per task line `pickRepo.UpdateLinePackedAsync(qty + status + reason + notes)`; (2) `cartonRepo.CreateAsync(carton)` with `CartonNumber` stamped server-side; (3) `packRepo.SetPackedAsync(taskId)` (Pending → Packed); (4) `soRepo.SetStatusAsync` flip Picked → Packed via `||` chain (try Picked first, fall through to PartiallyPicked — Phase 14B SO Cancel precedent; whichever applies wins, the other is no-op). **No Stock writes** — pack is post-stock.
- `CancelAsync(tenantId, packTaskId, reason, currentUserId)` — **even lighter than Pick's Cancel; no TX needed** (single repo write). `Pending → Cancelled` with required reason. **No SO state flip** — Generate didn't flip the SO, so Cancel doesn't either. **No carton cleanup** — Pending tasks have no carton (Carton INSERT only fires on SubmitAsync's TX). **No line resets** — pre-Submit there's nothing to reset since per-line edits don't happen until SubmitAsync. Idempotent on already-Cancelled (returns false). Rejects `Packed` (post-Submit terminal — return-to-stock workflow is a future TD).

**UI** (5 surfaces touched / created):
- `/SalesOrders/Detail` — new `isPacked` flag + `canGeneratePack` (`Picked || PartiallyPicked`). Quick Action "Generate pack" added between "Generate pick" and "Cancel" (`ti-package` icon). `canCancel` unchanged — Picked / PartiallyPicked / Packed already excluded from the `{Draft, Open, Allocating, Allocated}` list.
- `/SalesOrders` Index — Alpine `statuses` array now lists 10 chips; counts envelope expanded with `packed`; `badgeClass` map widened (Packed=info — positive terminal, distinct from Picked=success which is the "pre-pack" healthy state).
- `POST /SalesOrders/GeneratePack/{id}` — calls `GenerateAsync`, redirects to `/PackTasks/Detail/{newPackId}` on success with `PackTaskMessage` carrying PackNumber + line count + total picked qty. Error path bounces back to SO Detail with `SalesOrderError`.
- `_SalesOrderDecisionModals.cshtml` — new `#generate-pack-modal` block (info-blue, `ti-package` icon, lead text explaining the no-SO-state-change semantics + idempotency note).
- `/PackTasks/Detail/{id}` — new surface using `_DetailLayout`. Stats: Lines / Picked / Packed (color-tinted per terminal outcome) / Status. SO link + carton metadata (Carton # / Box type / Weight / Carton notes) surfaced in Overview only when carton exists (post-Submit). Per-state audit trio in Properties. Custom Lines tab via `_PackTaskLinesPanel`:
  - **Editable form when Pending**: Alpine reactive table of lines + a 3-column **Carton metadata section** (Box type select / Weight input / Carton notes) **below** the lines table, all in one form, single submit button. Per-row packed-qty defaults to PickedQuantity (the common "everything goes in the carton" shortcut — operator just clicks Submit if everything went fine); Status select toggles Packed/Skipped (Skipped disables qty + zeroes); ShortPackReason flagged when packed-qty drops below picked OR status flips to Skipped. Live tally footer.
  - **Terminal (Packed / Cancelled)**: read-only table with packed vs picked color-coded per line (green=full, amber=short, red=skipped) and ShortPackReason surfaced.
- `_PackTaskDecisionModals.cshtml` — new partial. Cancel modal with required 3-500 char reason textarea. Same shell as Phase 11A/12/13/14A/14C modals, `wms-pk-` token namespace.
- Sidebar — Pack Tasks entry deferred to the list-page chunk (Phase 14C precedent; would 404 without an Index action).

**Status mappers** (1 widened, 1 created):
- `SalesOrderStatusMapper` widened to 9 states. `Packed=info` (positive terminal — ready for ship; distinct from Picked=success). 14F will widen further with Shipping / Shipped / Closed.
- `PackTaskStatusMapper` (new). 3 states. `Pending=neutral`, `Packed=success`, `Cancelled=neutral`.

**TempData generalization**: `_DetailLayout` banner block now coalesces SEVEN sets of TempData keys (added `PackTask*`). Pattern continues to scale linearly per phase.

**ViewModels + validation** (3 new):
- `SubmitPackTaskViewModel` + `PackedLineRow` — model-binding shape for `POST /PackTasks/Submit`. Carton fields (`BoxTypeId` nullable Guid, `WeightKg` nullable decimal with Range, `CartonNotes` string?) live on the same form because pack workflow is single-shot for MVP. Cross-field rules enforced server-side.
- `CancelPackTaskViewModel` + `CancelPackTaskValidator` — same shape as Phase 14C / 12 / 10B cancel VMs.

**DAL extensions**:
- New `IPackTaskRepository` (8 methods: `CreateAsync` ambient-TX-aware header+lines insert, `GetByIdAsync` / `GetByNumberAsync` **3-recordset QueryMultiple** (header + lines + nullable Carton — Pending tasks have no carton yet), `GetActiveBySalesOrderAsync` pre-generation guard (Pending only — no InProgress for MVP), `SetPackedAsync` / `SetCancelledAsync` atomic per-state UPDATEs idempotent via `WHERE Status='Pending'`, `UpdateLinePackedAsync`, `CountForDatePrefixAsync`). New `PackTaskDetail` aggregate.
- New `ICartonRepository` (small — `CreateAsync` + `CountForDatePrefixAsync` only; reads happen via PackTaskRepository's QueryMultiple).
- New `IBoxTypeRepository` (lookup-only, mirrors Phase 7 IUomRepository pattern). `GetActiveAsync` filtered by `IsActive=1`, sorted by Code. Populates the BoxTypeId `<select>` on the Pack Detail submit form.
- `SalesOrderStatusCounts` widened to 10 fields (+Packed, between PartiallyPicked and Cancelled — pipeline-chronological order). `GetStatusCountsAsync` SQL grew 1 new SUM(CASE).

**Tests**: +68 net test cases (~38 distinct methods). Smaller delta than Phase 14C's +101 because Pack has fewer state-machine paths (3-state task vs 5-state Pick) and no Stock writes — the service-layer test surface is naturally narrower. 28 unit (PackTaskService — Generate/Submit/Cancel state-gating + happy paths + ValidateRequestShape edge cases including NegativeWeight); 40 integration (PackTaskStatusMapper Theory across 3 states; SalesOrderStatusMapper Theory backfill across the now-9-state set; PackTasksController Detail / Submit / Cancel happy + error + EmptyGuidBoxType normalisation + state-driven flag wiring; SalesOrders.GeneratePack happy + error). Test posture: **754 passing** (was 686). 266 unit + 483 integration + 5 skipped.

**Out of scope** (logged as TD-037):
- `/PackTasks` Index list page + GetData JSON envelope + chip counts + sidebar entry — list page deferred (operator reaches pack tasks via GeneratePack redirect; Pick Tasks list also still deferred per Phase 14C TD-036).
- **Pack video** (per ADR-009 — needs a dedicated spec covering MediaRecorder integration, retention policy, PDPA audit log, per-station + per-channel policy).
- **Scale integration / weight verification** — operator can type weight manually for MVP; serial/USB scale device integration is a separate concern.
- **Box-suggestion algorithm** — the brief mentions "smallest fit + 20% buffer" but BoxType dimension data isn't seeded yet, and the algorithm itself is non-trivial.
- **ScanEach vs ScanAndQty modes** (per-product config from `master.ProductPackingConfigs`) — the panel today is a single Scan+Qty UX; barcode-driven per-item validation comes later.
- **Multi-carton splitting** — the `UX_Cartons_PackTask` UNIQUE enforces 1:1 for MVP. Future migration drops the UNIQUE + adds a `CartonContents` many-to-many for per-line per-carton breakdown.
- **Carrier integration / label printing / tracking number assignment** — Phase 14F scope (Ship).
- **Manifest workflow** (Build → Seal → Handover with driver signature) — Phase 14F scope.
- **Post-Submit reversal** ("return to stock" — Packed tasks cannot be undone today; needs a separate Adjust+ workflow).
- **Pack videos / PDPA audit log / 10-day retention default** — bundled with the pack-video TD.
- Pack task printable packsheet / label PDF.

**Permission**: `OUTBOUND.ORDERS` covers pack task operations for MVP. Future phases may introduce a finer `OUTBOUND.PACK` perm for separation-of-duties on pack vs SO admin.

**Notes on chunk-by-chunk hiccups**:
- T4 had a brief over-engineering moment — added an unnecessary `PickNumberOrPack()` extension method to disambiguate between PickTask.PickNumber and PackTask.PackNumber. Caught immediately on review and ripped out before commit. Lesson reinforced: **don't add abstractions for hypothetical future requirements** (per CLAUDE.md "Don't add features … beyond what the task requires").
- T8 sidebar entry: same trap as Phase 14C T8 — initially considered adding the sidebar link but `Url.Action("Index", "PackTasks")` would 404 since no Index action exists. Deferred to the list-page chunk (cleaner than shipping a dead link).
- Phantom-Edit risk dodged: T3 had to widen `SalesOrderStatusCounts` (record positional ctor) and update its 3 Mock test sites. Sequential edits used per the user's pre-flight "Phantom-Edit Mitigation" guidance — no parallel edits to the same file in one tool batch.

### Day 10 — Phase 14C (Pick Task Generation + Execution — desktop "Complete pick" form)

**Branch**: `feat/outbound-pick` → merged to `main` · **Tag**: `v1.6.0-pick-task` · **Foundation for**: Phase 14D (pack consumes Picked / PartiallyPicked SO lines), 14E (mobile picker PWA replaces the desktop submit form on a per-station basis)

The execution half of the outbound pipeline. Allocation (14B) reserved stock against SO lines via `OrderAllocations`; this phase consumes those reservations: a pick task snapshots the active allocations, the operator enters actual picked quantities (full / short / skipped), and submit atomically decrements `Stock.OnHand`, releases `Stock.QuantityAllocated`, bumps the SO line's `PickedQuantity`, and flips the allocation `Active → Picked`. The schema is sized so 14E's mobile picker plugs into the same `SubmitAsync` entry point — the desktop form is just one consumer surface.

**Schema** (4 migrations):
- `20260510_021` — DROP + ADD `CK_SalesOrders_Status` to widen the enum from 5 → 8: adds `Picking | Picked | PartiallyPicked` between `Allocated` and `Cancelled`. Down() reverses to Phase 14B set. SQL Server requires drop+add for CHECK widening (Phase 14B's _018 set the precedent).
- `20260510_022` — ALTER `outbound.SalesOrderLines` ADD `PickedQuantity DECIMAL(18,4) NOT NULL DEFAULT 0` + 2 CHECKs (`>= 0` and `<= OrderedQuantity`). Note: `PickedQty` is bounded by **Ordered**, not Allocated — short-pick is real (operator picks less than allocated; AllocatedQuantity decrements on submit to release the unfilled reservation), but you can never pick more than originally ordered. Denormalized aggregate of the SO line's pick-task line `PickedQty`s; bumped atomically inside `SubmitAsync` TX.
- `20260510_023` — ALTER `outbound.OrderAllocations` ADD `PickedAt` + `PickedBy` audit pair (FK to `security.Users`); widen `CK_OrderAllocations_Status` from 2 → 3 (`Active | Released | Picked`); DROP + ADD `CK_OrderAllocations_AuditMatchesStatus` with three branches now: `Active` (all audit nulls), `Released` (ReleasedAt populated, PickedAt null), `Picked` (PickedAt populated, ReleasedAt null). `Released` and `Picked` are distinct terminal states — Released = cancel-reversal (14B), Picked = consumed by a pick task (this phase).
- `20260510_024` — CREATE `outbound.PickTasks` + `outbound.PickTaskLines`. Bundled in one migration for table-pair coherence (Phase 13 TransferOrders pattern).
  - `PickTasks` 5-state machine (`Pending → InProgress → Picked | PartiallyPicked | Cancelled`) with per-state audit trio (`GeneratedBy/At` always set; `StartedBy/At`, `CompletedBy/At`, `CancelledBy/At + CancelReason`) + `CK_PickTasks_AuditMatchesStatus` invariant per the established Phase 11A/12/13 + 14B `OrderAllocations` pattern. `AssignedTo` nullable for MVP pool mode (per-picker assignment is a future workflow). 3 indexes (per-status queue, per-SO, per-AssignedTo).
  - `PickTaskLines` snapshot the OrderAllocation's Stock 6-tuple (LocationId / ProductId / OwnerId / UomId / LotId / PalletId) + ExpectedQuantity at generation time so display + reporting stay stable even if the underlying `inventory.Stock` row mutates (Phase 12 cycle-count line snapshot pattern). Per-line `LineStatus` (`Pending | Picked | Skipped`) + `ShortPickReason` + `CK_PickTaskLines_StatusMatchesQty` invariant. CASCADE on header→lines. No `Version` on lines (matches CycleCountLines + TransferOrderLines convention). 4 CHECKs total: status enum, expected-positive, picked-non-negative, picked-not-over-expected.

**Service** (`IPickTaskService`, 3 lifecycle methods, all TransactionScope-wrapped per Phase 11A/12/13/14B precedent — MSDTC trade-off accepted per `feedback_transactionscope_dapper.md`):
- `GenerateAsync(tenantId, salesOrderId, currentUserId)` — light TX. State validation (Allocated only; **idempotent on Picking** — returns the existing Active task so the controller can redirect to its Detail page rather than 500 on accidental re-trigger). Defensive double-generation guard via `GetActiveBySalesOrderAsync`. Per-allocation Stock lookup snapshots the 6-tuple onto each PickTaskLine. Assigns `PICK-YYYYMMDD-NNNN` via `CountForDatePrefixAsync`. Inside TX: `pickRepo.CreateAsync(header, lines)` → `soRepo.SetStatusAsync("Allocated", "Picking")`. **No Stock writes, no allocation flips** — those happen in `SubmitAsync` when the operator commits actual quantities.
- `SubmitAsync(tenantId, request, currentUserId)` — heavyweight TX (the headline). Belt-and-suspenders `ValidateRequestShape` (private static): every task line in submission, no extras / dups, `LineStatus ∈ {'Picked','Skipped'}`, qty in `[0, ExpectedQuantity]`, `ShortPickReason` required when `picked < expected` OR `Skipped` (Skipped IS a short — the full Expected). One `GetActiveEntitiesBySalesOrderIdAsync` read resolves `SalesOrderLineId` per allocation (PickTaskLine doesn't carry it — snapshot intentionally narrow). Per task line inside TX: (1) `pickRepo.UpdateLinePickedAsync(qty + status + reason + notes)`; (2) `stockRepo.UpsertOnHandAsync(StockKey, -pickedQty, ctx)` with `MovementType=Pick + ReferenceType='PickTaskLine' + ReferenceId=line.Id` — only when `picked > 0`. `CK_Stock_OnHand_NonNegative` throws on concurrent drain → TX rolls back; (3) `stockRepo.AdjustQuantityAllocatedAsync(stockId, -ExpectedQty)` — releases the **full reservation** (picked portion went out via OnHand; unfilled portion is now free for re-allocation); (4) `allocRepo.MarkPickedAsync(allocId)` — flip Active → Picked, returns false on race (concurrent cancel/pick), throw on false. Per affected SO line aggregated: `AdjustLinePickedQuantityAsync(+pickedSum)` + `AdjustLineAllocatedQuantityAsync(-expectedSum)`. Header flips: PickTask Pending → InProgress (if Pending) → `Picked | PartiallyPicked` (target = Picked when zero shorts + zero skips); SO Picking → `Picked | PartiallyPicked` (Picked when **every** SO line PickedQty >= OrderedQty — re-reads SO lines after step 5 to capture the post-pick aggregate). Concurrent state-change guarded on every state-flip return value.
- `CancelAsync(tenantId, pickTaskId, reason, currentUserId)` — light TX. `Pending | InProgress → Cancelled` with required reason. SO `Picking → Allocated`. **No Stock writes, no allocation flips, no line resets** — Generate didn't mutate Stock or OrderAllocations, so neither does Cancel; allocations stay Active and the SO returns to its pre-pick state ready for a re-Generate by another picker if needed. Any operator-entered quantities on lines stay frozen as audit history. Idempotent on already-Cancelled (returns false). Rejects `Picked / PartiallyPicked` (post-Submit terminals — reversing a posted pick needs a separate "return to stock" workflow, future phase).

**UI** (5 surfaces touched / created):
- `/SalesOrders/Detail` — 3 new state flags (`IsPicking / IsPicked / IsPartiallyPicked`) + `canGenerate` (`isAllocated || isPicking` — Picking returns existing task idempotently). `canCancel` narrowed to the 4 pre-pick states (`Draft | Open | Allocating | Allocated`) — once a pick task exists the operator must cancel the pick task first (revert SO Picking → Allocated) before cancelling the SO. New "Generate pick" Quick Action (ti-list-check icon) between Allocate and Cancel.
- `/SalesOrders` Index — Alpine `statuses` array now lists 9 chips; counts envelope expanded with `picking + picked + partiallypicked`; `badgeClass` map widened (Picking=warning, Picked=success, PartiallyPicked=warning).
- `POST /SalesOrders/Generate/{id}` — calls `GenerateAsync`, redirects to `/PickTasks/Detail/{newPickId}` on success with `PickTaskMessage` carrying PickNumber + line count + total expected qty. Error path bounces back to `/SalesOrders/Detail` with `SalesOrderError`.
- `_SalesOrderDecisionModals.cshtml` — new `#generate-modal` block (info-blue header + lead text explaining the snapshot semantics: "Stock + allocations NOT mutated until pick submit").
- `/PickTasks/Detail/{id}` — new surface using `_DetailLayout`. Stats: Lines / Expected / Picked (color-tinted: green when fully picked, amber on any short, neutral pre-submit) / Status. SO link in Overview (`/SalesOrders/Detail/{soId}` mono code). Per-state audit trio in Properties sidebar (Generated / Started / Completed / Cancelled). Custom Lines tab via `_PickTaskLinesPanel`:
  - **Editable form when Pending|InProgress**: Alpine reactive table. Per-row picked-qty input defaults to the full ExpectedQuantity (the common "full pick" shortcut — operator just clicks Submit if everything went fine); Status select toggles Picked/Skipped (Skipped disables the qty input + zeroes the value, server enforces null); ShortPickReason gets a "Required" placeholder + flagged input class when picked-qty drops below expected OR status flips to Skipped. Footer shows live "{n} full · {m} short · {k} skipped" tally. Submit button gated client-side via `isValid()` — every line that needs a reason must have ≥3 chars (server-side authoritative).
  - **Terminal (Picked / PartiallyPicked / Cancelled)**: read-only table with picked vs expected color-coded per line (green=full, amber=short, red=skipped) and ShortPickReason surfaced.
- `_PickTaskDecisionModals.cshtml` — new partial. Cancel modal with required 3-500 char reason textarea. CSS-only `:target` activation; same shell as Phase 11A/12/13/14A modals with `wms-pt-` token namespace. Submit lives **inline** in the Lines panel (needs the per-line inputs in scope; a separate modal would mean duplicating the line table).
- Sidebar — Outbound submenu still shows Sales Orders only; **PickTasks sidebar entry waits for the list-page chunk** (currently no `/PickTasks` action — operator reaches pick tasks via the Generate redirect from SO Detail). `outboundActive` widened to highlight Outbound when on a PickTasks route.

**Status mappers** (2 widened / created):
- `SalesOrderStatusMapper` widened to 8 states. Variants: `Picking=warning`, `Picked=success`, `PartiallyPicked=warning`. Phase 14D will further widen with Packed / Shipped / Closed.
- `PickTaskStatusMapper` (new). 5 states. `Pending=neutral`, `InProgress=warning` (in flight), `Picked=success`, `PartiallyPicked=warning` (short — needs follow-up), `Cancelled=neutral`.

**TempData generalization**: `_DetailLayout` banner block now coalesces SIX sets of TempData keys (`Cancel*`, `Adjustment*`, `CycleCount*`, `Transfer*`, `SalesOrder*`, `PickTask*`). Pattern continues to scale linearly per phase.

**ViewModels + validation** (3 new):
- `SubmitPickTaskViewModel` + `PickedLineRow` — model-binding shape for `POST /PickTasks/Submit`. DataAnnotations stay light because the cross-field rules (status enum, qty range, reason required for short/skip) are enforced server-side by `SubmitAsync`'s `ValidateRequestShape`.
- `CancelPickTaskViewModel` — same shape as Phase 10B `CancelReceivingViewModel` and Phase 12 `CancelCycleCountViewModel`.
- `CancelPickTaskValidator` — same shape as `CancelCycleCountValidator` (3-500 char reason, non-blank, non-empty Id).

**DAL extensions**:
- New `IPickTaskRepository` (8 methods: `CreateAsync` ambient-TX-aware header+lines insert, `GetByIdAsync` / `GetByNumberAsync` QueryMultiple, `GetActiveBySalesOrderAsync` pre-generation guard, `SetStartedAsync` / `SetCompletedAsync(targetStatus)` / `SetCancelledAsync(fromStatus, reason)` atomic per-state UPDATEs idempotent via `WHERE Status=@from`, `UpdateLinePickedAsync`, `CountForDatePrefixAsync`). New `PickTaskDetail` aggregate.
- `IOrderAllocationRepository` gained `MarkPickedAsync` (atomic Active → Picked flip with PickedAt/PickedBy audit). `EntityColumns` SELECT now carries PickedAt/PickedBy.
- `ISalesOrderRepository` gained `AdjustLinePickedQuantityAsync` (mirror of `AdjustLineAllocatedQuantityAsync`). `LineColumns` SELECT includes PickedQuantity. `GetLineRowsByIdAsync` SQL selects `sol.PickedQuantity`. `GetStatusCountsAsync` SQL grew 3 new SUM(CASE) cases.
- `SalesOrderListRow.cs`: `SalesOrderLineRow` record gained `PickedQuantity` (between AllocatedQuantity and UnitPrice). `SalesOrderStatusCounts` widened to 9 fields (3 new in pipeline-chronological order, before Cancelled).

**Tests**: +101 net test cases (~43 distinct methods — same scale as Phase 14B's +35). 31 unit (PickTaskService — Generate / Submit / Cancel state-gating + happy paths + ValidateRequestShape edge cases + concurrent-race coverage); 70 integration (PickTaskStatusMapper Theory across 5 states; SalesOrderStatusMapper Theory backfill across the now-8-state set; PickTasksController Detail / Submit / Cancel happy + error + state-driven flag wiring; SalesOrders.Generate happy + error). Test posture: **686 passing** (was 585). 238 unit + 448 integration + 5 skipped.

**Out of scope** (logged as TD-036):
- `/PickTasks` Index list page + GetData JSON envelope + chip counts + sidebar entry — list page deferred (operator reaches pick tasks via Generate redirect in MVP; navigation gap acceptable until pickers want a queue view).
- "Save Progress" intermediate save — operator enters all quantities + submits in one shot for MVP. The PickTask `Pending → InProgress` transition is meant for this future flow; today it's invisible (`SubmitAsync` advances Pending → InProgress → Completed in one round-trip pair).
- Post-Submit reversal flow — Picked / PartiallyPicked tasks cannot be undone; needs a separate "return to stock" workflow with Adjust+ movements. `CancelAsync` rejects these states explicitly with a forward-pointing error message.
- Per-pick-task assignment (`PickTasks.AssignedTo` column nullable for MVP — pool mode); per-picker assignment workflow + claim/release UX waits for a future phase.
- Mobile picker PWA (14E scope per `docs/03_Roadmap.md` — desktop submit form first).
- "View pick task" link from SO Detail when SO is in Picking / Picked / PartiallyPicked — pick number is recoverable from the SO header but no quick-link UI yet (operator currently has to go through Generate-which-bounces-to-existing-task on Picking).
- Pick task printable picksheet (Phase 9C GRN print pattern).
- Per-tenant pick strategy resolver (FIFO is implicit through allocation; no FEFO/zone strategies for MVP).
- Pick task observability (StockMovements feed shows the Pick movements but no per-task aggregate report — Activity tab on PickTask Detail is bare today).

**Permission**: `OUTBOUND.ORDERS` covers pick task operations for MVP. Future phases may introduce a finer `OUTBOUND.PICK` perm for separation-of-duties on pick vs SO admin.

**Notes on chunk-by-chunk hiccups**:
- T7 → T8 transitional state: Generate redirect target was "back to SO Detail" temporarily in T7 (the receiving page didn't exist yet); flipped to `/PickTasks/Detail/{newId}` in T8 once the route landed. Half-state between commits is OK per the chunk-by-chunk pattern — every commit compiles and 585 tests stayed green throughout.
- T8 sidebar: PickTasks submenu link was added then immediately backed out — `Url.Action("Index", "PickTasks")` would 404 because no Index action exists yet (deferred to the list-page chunk). Cleaner to defer the sidebar entry to that chunk than to ship a dead link.
- T5 `MarkPickedAsync` return-value check was added after a quick mental review caught the gap — the repo method's bool return signals concurrent-state-change races, but the original write didn't act on it. Throwing on false catches operator-vs-operator pick races (rare in practice; defensive against the data-corruption-not-loss side).

### Day 10 — Phase 14B (Sales Order Allocation — ADR-005 strategy primitive)

**Branch**: `feat/outbound-allocation` → merged to `main` · **Tag**: `v1.5.0-allocation` · **Foundation for**: Phase 14C (pick draws from active OrderAllocations)

First Stock-touching outbound surface. Allocation primitive sized so 14C's pick logic plugs in cleanly: it consumes Active `OrderAllocations` rows and never needs to re-resolve the strategy. Reversal-aware Cancel completes the round-trip — cancel-after-allocate releases reservations atomically.

**Schema** (3 migrations):
- `20260510_018` — DROP + ADD `CK_SalesOrders_Status` to widen the enum from 3 → 5: `Draft|Open|Allocating|Allocated|Cancelled`. Down() reverses to MVP set. SQL Server requires drop+add for CHECK widening.
- `20260510_019` — ALTER `outbound.SalesOrderLines` ADD `AllocatedQuantity DECIMAL(18,4) NOT NULL DEFAULT 0` + 2 CHECKs (`>= 0` and `<= OrderedQuantity`). Denormalized aggregate of the line's Active OrderAllocations rows; bumped/decremented atomically inside AllocateAsync / cancel-reversal TX.
- `20260510_020` — CREATE `outbound.OrderAllocations`. Per-line linkage to `inventory.Stock` rows. Status: `Active|Released`. Audit pairs (AllocatedBy/At + ReleasedBy/At) + ReleaseReason. `CK_OrderAllocations_AuditMatchesStatus` invariant (mirrors Phase 11A/12/13 pattern). 2 indexes (per-Line + per-Stock). Released allocations stay in the table as audit history — never hard-deleted.

**Strategy pattern** (ADR-005, new `WMS.BLL.Strategies.Allocation` folder):
- `IAllocationStrategy` — pure-function interface: `(SOLineId, OutstandingQty, Stock candidates) → (Picks, Shortfall)`. Strategies are stateless and deterministic. Records bundled in the same file (Context/Decision/Pick).
- `FifoAllocationStrategy` — `Name = "FIFO"`. Sorts candidates by `Stock.CreatedAt` ASC, takes `min(available, remaining)` per row.
- `AllocationStrategyResolver` — receives `IEnumerable<IAllocationStrategy>` from DI (auto-discovered), looks up by Name (case-insensitive), default = FIFO. Constructor enforces FIFO presence at startup. Adding FEFO/Tier later is one DI line + one impl class — no service-code change.

**Service** (`IAllocationService`):
- `AllocateAsync(tenantId, soId, strategyName?, userId, ct)` — TransactionScope-wrapped (Phase 11A/12/13 pattern, MSDTC trade-off). Loads SO → resolves strategy → per-line: `IStockRepository.GetAllocationCandidatesAsync` (filter by Warehouse + Product + Owner + UoM, where `OnHand-Allocated > 0`, FIFO-friendly default sort) → `strategy.Allocate(context)` → for each pick: insert `OrderAllocation` (Active) + `Stock.AdjustQuantityAllocatedAsync(+pickQty)` + `SalesOrderLine.AdjustLineAllocatedQuantityAsync(+sumPicks)`. Flips header to `Allocated` (zero shortfall) or `Allocating` (any shortfall). Idempotent on already-Allocated; rejects from Draft/Cancelled. Returns `AllocationResult(IsFullyAllocated, LineCount, FullyAllocatedLineCount, ShortfallByLineId)`.
- `ReleaseAllForSalesOrderAsync(tenantId, soId, reason, userId, ct)` — caller owns TX. 3 steps inside: (1) per-allocation: `Stock.AdjustQuantityAllocatedAsync(-allocatedQty)`; (2) per-line: `SalesOrderLine.AdjustLineAllocatedQuantityAsync(-sumOfFreedQty)`; (3) bulk-flip Active→Released with audit on every row via single UPDATE+IN-subquery. Returns count.

**Reversal-aware Cancel** (`SalesOrderService.CancelAsync`):
- Constructor gained `IAllocationService` dep.
- TransactionScope-wraps conditional release (only when `Status is "Allocating" or "Allocated"`) + 4-state source chain (`Draft|Open|Allocating|Allocated → Cancelled`). When SO had no allocations (Draft/Open), the release is skipped but the TX still wraps the status flip — uniformity over branching.

**UI** (3 surfaces touched):
- `/SalesOrders` Index — Alpine `statuses` array now lists 6 chips; badge map adds `allocating: s-warning` + `allocated: s-info`.
- `/SalesOrders/Detail/{id}`:
  - Stats tile shape: Lines / Quantity / **Allocated (x.xx / y.yy with color tint)** / Status. Dropped Amount tile.
  - 4 Quick Actions: Edit / Submit / **Allocate** / Cancel. Allocate enabled on `Open|Allocating` only (greyed on Allocated — idempotent no-op, visually "done").
  - 2 custom tabs: Lines (now shows `AllocatedQuantity` column with `% filled` color tint) + new **Allocations** panel (`_SalesOrderAllocationsPanel` — grouped-by-line read-only table: Line / Location / Lot / Pallet / Qty / Allocated time-relative / By).
- Decision modals — new `#allocate-modal` (FIFO strategy explainer + green confirm button). Cancel modal copy now conditional via `@if (ViewBag.IsAllocating || ViewBag.IsAllocated)` — explains allocation release vs no-op.

**Status mapper** widened to 5 states. Variants: `Draft=neutral`, `Open=success`, `Allocating=warning`, `Allocated=info`, `Cancelled=neutral`.

**SalesOrderStatusCounts** record gained `Allocating` + `Allocated` fields between `Open` and `Cancelled`. SQL projection extended.

**DAL extensions** (chunk 3):
- New `IOrderAllocationRepository` (7 methods: Create, CreateBatch, GetActiveByLineId, GetActiveBySalesOrderId, GetActiveEntitiesBySalesOrderId, ReleaseAsync, ReleaseAllForSalesOrderAsync). Reads JOIN through Stock + Lots + Pallets + Users for the display projection (`OrderAllocationRow`).
- `IStockRepository` gained `GetAllocationCandidatesAsync` (filter by warehouse + 3-tuple Product+Owner+UoM, `OnHand-Allocated > 0`, sort `CreatedAt` ASC) + `AdjustQuantityAllocatedAsync` (atomic delta, leans on existing `CK_Stock_Allocated_NotOverOnHand` for invariants).
- `ISalesOrderRepository` gained `AdjustLineAllocatedQuantityAsync`.
- `SalesOrderRepository.LineColumns` SELECT/`SalesOrderLineRow` record gained `AllocatedQuantity` between `OrderedQuantity` and `UnitPrice`.

**Tests**: +35 net (8 FifoStrategy + 5 Resolver + 10 AllocationService + 3 SalesOrderService cancel-reversal + 9 controller incl. 2 new mapper Theory cases). Test posture: **585 passing** (was 550). 207 unit + 378 integration + 5 skipped.

**Out of scope** (logged as TD-035): pick task generation + execution (14C scope), FEFO/Tier strategies (one impl + DI line each), per-tenant strategy configuration, allocation-aware line edit on Open SOs (line locks to "Replace lines" path until Phase 14C ships), short-pick failure reversal (will share `ReleaseAllForSalesOrderAsync`'s mechanics), reservation expiry timer, allocation history report (Released rows are queryable but no UI).

**Notes on chunk-by-chunk hiccups**:
- Chunk 3 hit a phantom-Edit issue where `IStockRepository.cs` interface edits never persisted despite the Edit tool reporting success — class-only edit compiled fine (extra methods on impl > interface is legal C#) but BLL surfaced the gap in chunk 5. Re-applied. Same pattern hit again on chunk 8 modals (parallel Edits to the same file in one tool-batch silently no-op for all but the first). Mitigation going forward: spot-check critical interface edits with a follow-up Read; sequential edits when modifying a file in multiple places per chunk.

**Permission**: `OUTBOUND.ORDERS` covers allocation operations for MVP. Future phases may introduce a finer `OUTBOUND.ALLOCATION` perm for separation-of-duties on Allocate vs SO admin.

### Day 10 — Phase 14A (Sales Order Admin CRUD — Outbound MVP foundation)

**Branch**: `feat/outbound-mvp` → merged to `main` · **Tag**: `v1.4.0-so-crud` · **Foundation for**: Phase 14B (allocation/pick) + 14C (pack) + 14D (ship)

First slice of Outbound. Parallel of Phase 9A's role for Inbound: pure admin CRUD on Sales Orders, with the line shape, validation, and Detail/Edit/Index surfaces sized so 14B's allocation logic plugs in cleanly.

**Schema** (3 migrations):
- `20260510_015` — `outbound` schema (CREATE SCHEMA IF NOT EXISTS pattern, mirrors counts schema migration 010).
- `20260510_016` — `outbound.SalesOrders` header. SoNumber unique; CustomerId/WarehouseId NOT NULL FKs; OrderDate (DATE, default today); RequestedShipDate? (DATE, nullable); Status enum trimmed to `Draft|Open|Cancelled` for MVP (allocation/pick/pack states arrive in 14B's ALTER); audit + Version. 3 indexes (Status+OrderDate DESC, Customer+OrderDate DESC, Warehouse+Status). 1 CHECK on Status.
- `20260510_017` — `outbound.SalesOrderLines`. CASCADE on SO delete; Product/Owner/UoM FKs; OrderedQuantity DECIMAL(18,4); UnitPrice nullable (free-text MVP); audit + Version. UQ(SO, LineNumber); IX(Product). 2 CHECKs (qty>0, UnitPrice IS NULL OR ≥0). **Owner per LINE per ADR-007** (a single SO can mix owner-keyed stock when the customer orders through a channel that consolidates suppliers).

**Service** (`ISalesOrderService`):
- `CreateAsync` — validates header + lines (per-line: ProductId/OwnerId/UomId non-empty, LineNumber positive + unique, OrderedQuantity > 0, UnitPrice ≥ 0 when not null). Assigns `SO-YYYYMMDD-NNNN` server-side via repo's `CountForDatePrefixAsync` (matches Adjustment / CycleCount / Transfer pattern; diverges from PO's caller-supplied PoNumber). Lands as `Draft`.
- `UpdateAsync` — header-only edit always allowed (non-Cancelled). `ReplaceLines=true` gated on `Status='Draft'` — once Open, lines lock pending allocation reversal in 14B. Cancelled SOs reject any update outright.
- `SubmitAsync` — `Draft → Open`. Idempotent on already-Open. Rejects zero-line SO (defensive against programmatic callers; UI wouldn't normally allow).
- `CancelAsync` — `Draft|Open → Cancelled`. **No reason field for MVP** — operator can edit Notes pre-cancel for context (CancelReason column deferred to 14B+ if allocation-driven cancellation needs it). Idempotent.
- **No TransactionScope wrapping** — MVP foundation has zero Stock-touching ops. 14B will wrap allocation-aware operations following Phase 11A/12/13 precedent.

**UI** (5 surfaces):
- `/SalesOrders` — Alpine list with chip counts (`All / Draft / Open / Cancelled`). 8-col table (SO# / Customer / Warehouse / OrderDate / ShipDate / Status / Lines / Qty).
- `/SalesOrders/Create` — multi-line Alpine grid. Header: Customer + Warehouse + OrderDate + RequestedShipDate + Notes. Lines: Product/Owner/UoM/Qty/UnitPrice/Notes/Remove. Workflow + reason guide sidebar.
- `/SalesOrders/Edit/{id}` — read-only header strip (SoNumber/Status/Customer/Warehouse) + editable fields. **Opt-in `Replace lines on save` checkbox** controls whether the lines section is sent to ReplaceLines. **`LinesLocked` banner** when `Status != 'Draft'`. Single Razor template handles three states (locked / editable-not-replacing / editable-replacing) via Alpine reactive `:disabled` + opacity hint.
- `/SalesOrders/Detail/{id}` — `_DetailLayout` with custom Lines tab (`_SalesOrderLinesPanel` — read-only table with `UnitPrice * OrderedQuantity` line totals). Quick Actions state-gated: Edit (non-Cancelled), Submit (Draft only), Cancel (non-Cancelled).
- Decision modals — `_SalesOrderDecisionModals` partial reuses Phase 11A/12/13 CSS-only `:target` pattern. 2 modals: Submit + Cancel (both no-payload).
- Sidebar — Outbound module now expands to a submenu with Sales Orders entry (superseding the auto-generated single-link fallback that pointed to '#'). 14B/C/D will add Waves/Picks/Packs/Shipments rows.

**TempData generalization**: `_DetailLayout` banner block now coalesces FIVE sets of TempData keys (`Cancel*`, `Adjustment*`, `CycleCount*`, `Transfer*`, `SalesOrder*`). Pattern continues to scale linearly.

**DAL extension**: `ICustomerRepository.GetActiveAsync()` (new) — returns `IReadOnlyList<LookupItem>` for the SO Create form's Customer dropdown. Mirrors Product/Owner/Uom shape; filters `Status='Active'`; sorted by Code.

**Tests**: +37 net (18 service unit + 19 controller integration including 3-case status mapper Theory). Test posture: **550 passing** (was 513). 181 unit + 369 integration + 5 skipped. Service-side tests cover Create validation (6 paths), Submit state-gating + zero-line guard, Cancel idempotency + chain attempt, Update ReplaceLines gating on non-Draft, happy-path SO-NNNN assignment. Controller covers all 9 endpoints + state-driven Quick Action gating across all 3 statuses + LinesLocked GET state + Cancel-redirect Edit-GET path.

**Out of scope** (logged as TD-034): per-line allocation/pick/ship qty columns, Status states beyond MVP set (Allocating/Allocated/Picking/Picked/Packed/Shipped/Closed), CancelReason audit column, B2B-vs-B2C distinguisher (SalesOrderDetails table from design doc), OrderSource/Channel FKs, customer-tier allocation priority, allocation strategy resolver, allocation reversal flow on Cancel-after-Open, Order Activity tab. Each independent and lands across 14B/C/D.

**Permission**: `OUTBOUND.ORDERS` already seeded by migration 042; baseline role grants by 044. No additional perm migration needed.

### Day 10 — Phase 13 (Inter-warehouse Transfer Workflow — ADR-012)

**Branch**: `feat/transfer-workflow` → merged to `main` · **Tag**: `v1.3.0-transfers` · **Implements**: ADR-012

Final inventory-management surface. Multi-line workflow that shifts owner-keyed stock between warehouses with audited per-state transitions.

**Schema** (2 migrations):
- `20260510_013` — `inventory.TransferOrders` header. **7-state machine** (collapsed from ADR's 9): `Draft → Submitted → Approved → InTransit → Received` (terminal happy); `Cancelled` from any pre-InTransit; `Lost` from InTransit. Per-state audit trio (`RequestedBy/At` always set; `SubmittedBy/At`, `ApprovedBy/At`, `DispatchedBy/At`, `ReceivedBy/At` populated as workflow advances; `CancelledBy/At + CancelReason`, `LostBy/At + LossReason` for failure terminals). `CK_TransferOrders_AuditMatchesStatus` enforces the per-status invariant (mirrors Phase 11A `Adjustments` + Phase 12 `CycleCounts`). `CK_TransferOrders_FromTo` ensures FromWarehouse≠ToWarehouse. 2 indexes (per-status queue + per-warehouse pair).
- `20260510_014` — `inventory.TransferOrderLines`. Owner+Lot preserved per ADR-007 (3PL/VMI invariant). Quantity progression: `QtyRequested` (operator intent) → `QtyDispatched` (actual pick) → `QtyReceived` (dest count). **`QtyLossInTransit` is a PERSISTED computed column** `(ISNULL(QtyDispatched,0) - ISNULL(QtyReceived,0))` — no service code writes it. Per-line Status: `Pending|Dispatched|Received|Variance`. 6 CHECK constraints incl. `CK_TransferOrderLines_DispatchedNotOverRequested`, `CK_TransferOrderLines_ReceivedNotOverDispatched`, `CK_TransferOrderLines_StatusMatchesQty` invariant. CASCADE delete on header→lines.

**Service** (`ITransferOrderService`):
- `CreateAsync` — validates From≠To warehouse + reason in closed set + non-empty lines + per-line From≠To location + qty>0 + LineNumber uniqueness. Assigns `TRN-YYYYMMDD-NNNN`. Lands as `Draft` with all lines `Pending`.
- `SubmitAsync` — `Draft → Submitted`. Idempotent on already-Submitted.
- `ApproveAsync` — `Submitted → Approved`. **Separation of duties** (approver≠requester).
- `DispatchAsync` — `Approved → InTransit`. **TransactionScope-wrapped** (Phase 11A/12 pattern; MSDTC trade-off accepted per `feedback_transactionscope_dapper.md`). Per dispatched line: `IStockRepository.UpsertOnHandAsync(srcKey, -QtyDispatched, ctx)` with `MovementType=Transfer + ReferenceType='TransferOrderLine' + ReferenceId=line.Id`. `CK_Stock_OnHand_NonNegative` throws if source insufficient → TX rolls back. Lines not in payload stay `Pending` (partial dispatch allowed). Header flips on any line dispatched.
- `ReceiveAsync` — `InTransit → Received`. TransactionScope-wrapped. Per line: `UpsertOnHandAsync(dstKey, +QtyReceived, ctx)` (skipped when QtyReceived=0 — full loss case). Line status: `Received` when QtyReceived==QtyDispatched; `Variance` when shorter. Header flips when ALL lines received.
- `CancelAsync` — pre-InTransit only (`Draft|Submitted|Approved → Cancelled`). Required reason. Tries each from-state via `||` chain (atomic UPDATE WHERE Status=@from; whichever applies wins).
- `MarkLostAsync` — `InTransit → Lost`. **No Stock writes** — loss captured naturally by `QtyLossInTransit` on lines whose `QtyReceived` stays NULL forever.

**UI** (5 surfaces):
- `/Transfers` — Alpine list with chip counts (`All / Draft / Submitted / Approved / In transit / Received / Cancelled / Lost`). 8-col table with side-by-side `Req · Dis · Rec` qty cell (color-coded — blue/green/red).
- `/Transfers/Create` — single-page form. Two cascading Warehouse → Location dropdowns (one for From, one for To — both call `GET /Transfers/Locations/{whId}`). **Multi-line editable Alpine grid** (`x-for` with indexed `Lines[N].FieldName` for ASP.NET model binding). Closed-list reason dropdown.
- `/Transfers/Detail/{id}` — `_DetailLayout` with custom Lines tab. **Inline Dispatch form when Status=Approved** (per-line picked qty, defaulted to QtyRequested). **Inline Receive form when Status=InTransit** (per-line received qty, defaulted to QtyDispatched). Read-only variance table otherwise (loss column color-coded red). Quick Actions state-gated for all 6 transitions.
- Decision modals — `_TransferDecisionModals` partial reuses Phase 10B/11A/12 CSS-only `:target` pattern. 4 modals: Submit + Approve are no-payload; Cancel + MarkLost require reasons (3-500 chars).
- Sidebar — Inventory module submenu now has Adjustments + **Transfers**. `INVENTORY.TRANSFERS` permission already seeded by migration 042.

**TempData generalization**: `_DetailLayout` banner block now coalesces FOUR sets of TempData keys (`Cancel*`, `Adjustment*`, `CycleCount*`, `Transfer*`) into a single render — pattern continues to scale linearly.

**Tests**: +53 net (27 service unit + 26 controller integration including 7 status-mapper Theory cases). Test posture: **513 passing** (was 460). 163 unit + 350 integration + 5 skipped. Unit tests cover state-transition gating (every from-state per action), separation-of-duties, dispatched/received qty caps + variance status flip, zero-receive (no Stock write), TX-wrapped happy paths writing the right deltas + StockMovementContext shape. Controller tests cover all 8 endpoints + state-driven Quick Action gating across all 7 statuses.

**Out of scope** (logged as TD-033): per-line CarrierId / TrackingNumber / Priority / EstimatedTransitDays, mobile PWA receiving workflow, integration with Outbound PickTask (PickTaskId on lines), auto-Adjustment on variance closure, "any source location" auto-resolution (NULL FromLocationId per ADR), `inventory.TransferStatusHistory` per-transition log table, mid-transit re-dispatch flow, dual-writer in-transit accounting (ADR Option A).

### Day 10 — Phase 12 (Cycle Count Workflow — counts.* domain)

**Branch**: `feat/cycle-counts` → merged to `main` · **Tag**: `v1.2.0-cycle-counts` · **New domain**: `counts` schema

Periodic stock reconciliation — the multi-line complement to Phase 11A's single-line manual Adjustment. Distinct table family per ADR-013 ("Don't use Adjustment for Cycle Count results").

**Schema** (3 migrations):
- `20260510_010` — `counts` schema (CREATE SCHEMA IF NOT EXISTS).
- `20260510_011` — `counts.CycleCounts` header. 4-state machine (`Counting | Review | Applied | Cancelled`); `LocationFilter` nullable (null = whole-warehouse scope); per-state audit trio (`StartedBy/At` always set; `CountedBy/At`, `ReviewedBy/At` + `AppliedAt`, `CancelledBy/At` + `CancelReason`); `CK_CycleCounts_AuditMatchesStatus` invariant per status. Indexes: per-warehouse + per-status queue.
- `20260510_012` — `counts.CycleCountLines` snapshot. Each line carries StockId + 6-tuple denormalized + ExpectedQuantity (snapshot) + nullable CountedQuantity + LineStatus (`Pending | Counted | Skipped`). 4 CHECK constraints: line-status enum, expected-qty non-negative, counted-qty non-negative, status-requires-quantity invariant. CASCADE delete on header→lines.

**Service** (`ICycleCountService`):
- `CreateAsync` — calls new `IStockRepository.GetPositiveOnHandByWarehouseAsync(warehouseId, locationFilter?)` to snapshot positive-OnHand stock at scope. Empty snapshot throws (no point counting nothing). Assigns `CYC-YYYYMMDD-NNNN`. Lines persist with `CountedQuantity=NULL` + `LineStatus='Pending'`.
- `SaveCountedQuantitiesAsync` — bulk per-line update of `CountedQuantity` + `LineStatus` + `Notes`. Validates per-update: status-quantity invariant (Pending forbids qty; Counted requires qty), enum membership, non-negative qty. Allowed only when session is in `Counting` state. Atomic per call — all updates land or none.
- `SubmitForReviewAsync` — `Counting → Review`. Records `CountedBy/At` on header. Idempotent.
- `ApproveAndApplyAsync` — TransactionScope-wrapped (Phase 10B/11A pattern). Per-line apply policy: Counted lines with non-zero variance → `IStockRepository.UpsertOnHandAsync(key, variance, ctx)` with `MovementType=Cycle` + `ReferenceType='CycleCountLine'` + `ReferenceId=line.Id` + Notes carrying count number; Counted lines with zero variance → no Stock write (verified-as-correct); Pending/Skipped lines → ignored. Repo `SetAppliedAsync` flips status. CK_Stock_OnHand_NonNegative throws if a counted-down delta would underflow — TX rolls back. Separation of duties: `counter ≠ approver`.
- `CancelAsync` — `Counting OR Review → Cancelled` with required reason. Cannot cancel Applied. Idempotent.

**UI** (4 surfaces):
- `/CycleCounts` — Alpine list with chip counts (`All / Counting / Review / Applied / Cancelled`). 8-col table includes per-session line counts + counted progress + variance count for at-a-glance review.
- `/CycleCounts/Create` — single-page form. Cascading Warehouse → Location filter (AJAX `GET /CycleCounts/Locations/{whId}`); optional Notes. Submit creates the snapshot and redirects to Detail.
- `/CycleCounts/Detail/{id}` — `_DetailLayout` with custom Lines tab. **Inline editable count form when Status=Counting** (Alpine reactive table with auto-status-flip on quantity entry — clearing the field reverts to Pending; entering a number flips to Counted); **read-only variance table when Status≠Counting** (variance column color-coded: green=zero, blue=positive, red=negative; status badge per line). Quick Actions state-gated: Submit (Counting only), Apply (Review only + counter≠user), Cancel (any non-terminal).
- Decision modals — `_CycleCountDecisionModals` partial reuses Phase 10B/11A CSS-only `:target` pattern. Submit + Apply are no-payload confirms; Cancel requires reason.
- Sidebar — Counts module (existing `ti-checklist` icon) now expands to Cycle Counts submenu. `COUNTS.CYCLE_COUNTS` permission already seeded by migration 042.

**TempData generalization**: `_DetailLayout` banner block now coalesces three sets of TempData keys (`Cancel*`, `Adjustment*`, `CycleCount*`) into a single render. Future "POST then redirect to Detail" surfaces just need to write to either set.

**Stock repo addition**: new `IStockRepository.GetPositiveOnHandByWarehouseAsync(warehouseId, locationFilter?, ct)` — JOIN through Locations to filter by warehouse, narrow to positive OnHand only, sorted by Location.Code + ProductId for stable session ordering.

**Tests**: +37 net (17 service unit + 20 controller integration including 4 status-mapper Theory cases). Service-side tests cover snapshot creation (empty/happy), per-line save validation (4 paths), state transitions, separation-of-duties on Apply, and the per-line variance behavior (write only when Counted + non-zero variance; verified-as-correct when zero variance). Controller covers all 6 endpoints + state-driven Quick Action gating. Test posture: **460 passing** (was 423). 136 unit + 324 integration + 5 skipped.

**Out of scope** (logged as TD-032): mobile PWA scan-driven counting, multi-counter coordination, per-line approve/reject, print count sheet, scheduled / recurring counts, variance-threshold gating. Each independent.

### Day 10 — Phase 11A (Stock Adjustment Workflow — ADR-013)

**Branch**: `feat/adjustment-workflow` → merged to `main` · **Tag**: `v1.1.0-adjustments` · **Implements**: ADR-013

First net-new operational feature post-MVP. Daily-ops gap closer (cycle-count discrepancies, breakage write-offs, found stock, return-to-supplier).

**Schema** (`inventory.Adjustments`, migration `20260510_009`):
- Single-line per ADR (cycle counts use future `counts.CountAdjustments` table).
- Flat 3-state machine: Pending → (Applied | Rejected). Apply atomic with approval — no intermediate Approved state.
- 6-tuple stock target denormalised (LocationId / ProductId / LotId / PalletId / OwnerId / UomId + WarehouseId for filter). `StockId` nullable: NULL on Pending; populated to resolved row on Apply.
- Audit trio per terminal state: `RequestedBy/At` (always set), `ApprovedBy/At` + `AppliedAt` (Applied), `RejectedBy/At` + `RejectionReason` (Rejected). `CK_Adjustments_AuditMatchesStatus` enforces the per-status invariant.
- 4 CHECK constraints: status enum (`Pending|Applied|Rejected`), reason enum (`Damaged|Expired|Lost|Found|ReturnedToSupplier|Sample|Other`), QuantityDelta non-zero, audit-status invariant.
- 3 indexes: per-status pending queue, per-warehouse list, per-stock partial (filtered to `WHERE StockId IS NOT NULL`).

**Service** (`IAdjustmentService`):
- `CreateAsync` — validates 6-tuple + reason + non-zero delta + non-empty user; assigns `ADJ-YYYYMMDD-NNNN` via repo's `CountForDatePrefixAsync`. Lands as Pending with `StockId=null`.
- `ApproveAsync` — TransactionScope-wrapped (same pattern as Phase 10B cancel; multi-connection → MSDTC promotion accepted). Steps: (1) `IStockRepository.UpsertOnHandAsync(key, delta, ctx)` with `MovementType=Adjust` + `ReferenceType='Adjustment'` + `ReferenceId=adjustment.Id` + Notes carrying `"{Reason}: {AdjustmentNumber}"`; (2) `SetAppliedAsync(id, stockId, approverId)` flips status + populates audit trio + stamps StockId. Idempotent on already-Applied (returns false). Throws on Rejected, on requester==approver (separation of duties), on InvalidOperationException from CK_Stock_OnHand_NonNegative (operator already consumed stock).
- `RejectAsync` — atomic status flip with rejection reason. Same idempotency + self-rejection rule as Approve.
- WHEN NOT MATCHED on UpsertOnHand creates a new Stock row when no 6-tuple match exists — fine for Found scenarios (positive delta); negative delta on a non-existent row fails CK_Stock_OnHand_NonNegative naturally.

**UI**:
- `/Adjustments` — Alpine list with chip counts (`All N · Pending N · Applied N · Rejected N`) reusing Phase 10A pattern. 8-col table with signed-delta color (green +, red −) and per-row UoM.
- `/Adjustments/Create` — single-page form with 2-card grid layout (Stock target + Adjustment + Reason guide sidebar). Cascading Warehouse → Location dropdown (AJAX `GET /Adjustments/Locations/{whId}` populates after warehouse pick). Closed-list reason dropdown. AllowCreateNew checkbox for Found-style scenarios. "Notes required when Reason='Other'" enforced server-side via FluentValidation `When` rule.
- `/Adjustments/Detail/{id}` — `_DetailLayout` with status-driven Quick Actions. Approve & Reject buttons enabled only when (a) status=Pending AND (b) currentUser != requester (separation of duties). Self-approval banner explains the gating to the requester.
- Approve / Reject modals — CSS-only `:target` pattern reused from Phase 10B. Approve: simple confirm. Reject: required reason textarea (3-500 chars).
- Sidebar — Inventory module now expands to Adjustments submenu (matches Inbound + Master pattern). `INVENTORY.ADJUSTMENTS` permission already seeded by migration 042; the sidebar appears for users with any INVENTORY.* perm.
- TempData banner generalised in `_DetailLayout` — accepts both `CancelMessage/Error` (Phase 10B) and `AdjustmentMessage/Error` (Phase 11A) without duplication.

**Movement Log integration**:
- Apply writes `inventory.StockMovements` row inside the same TX as the Stock UPDATE (per ADR-014). `ReferenceType='Adjustment'`, `ReferenceId=adjustment.Id`, `Notes` carrying reason + adjustment number. Existing `MovementActivityMapper.Map` renders these via the `Adjust+/Adjust-` variants on Stock / Product / Warehouse Activity feeds — no mapper change needed.

**Tests**: +29 net (13 service unit + 16 controller integration including 3 status-mapper Theory cases). Test posture: **423 passing** (was 394). 119 unit + 304 integration + 5 skipped.

**Out of scope** (logged as TD-031): threshold-based auto-approval, role-based approval bypass, multi-line adjustments, Stock-detail-page entry point, billing hooks. Each is independent and can land separately.

### Day 10 — Phase 10B (Inbound Hardening — TransactionScope + GR Cancellation)

**Branch**: `feat/inbound-hardening` → merged to `main` · **Tag**: `v1.0.2-inbound-hardened` · **Closes**: TD-022 + TD-023

Two paired hardening items: atomic orchestration + reversal flow. Bundled because the Cancellation flow re-uses the same TransactionScope pattern from TD-022.

**TD-022 — TransactionScope on PostReceivingAsync**:
- Wrapped the 4-step orchestration (Create header+lines → per-line stock upsert → Lot/Pallet ref stamp → PO line bump → PO status auto-transitions) in `TransactionScope(Required, ReadCommitted, AsyncFlowOption.Enabled)`.
- Trade-off accepted: each repo gets a fresh `SqlConnection` from its factory, so the orchestration spans multiple connections within one scope. Microsoft.Data.SqlClient's PSPE promotes the LTM transaction to MSDTC on the second connection enlist. Works on Windows deployment (per architecture). Cleaner alternative — single shared connection threaded through every repo + sub-service — was de-scoped (~1-2 days extra work for marginal gain at current volumes).
- Repos `PurchaseOrderRepository.{CreateAsync, ReplaceLinesAsync}` + `ReceivingHeaderRepository.CreateAsync` had internal `_connection.BeginTransaction()` calls that fight an ambient TransactionScope ("SqlConnection does not support parallel transactions"). Fixed via `var hasAmbient = Transaction.Current is not null; using IDbTransaction? tx = hasAmbient ? null : _connection.BeginTransaction();` — preserves standalone behaviour, defers to ambient when wrapped.
- Draft path also wrapped (Step 1 only runs but failure rolls back cleanly).

**TD-023 — GR Cancellation flow**:
- Migration `20260510_008_AddReceivingHeadersCancellationAudit` — adds `CancelledBy` (FK → security.Users), `CancelledAt`, `CancelReason` (NVARCHAR(500)) to `inbound.ReceivingHeaders`. All nullable; populated atomically inside the cancel orchestration.
- New `IReceivingHeaderService.CancelReceivingAsync(tenantId, headerId, reason, userId)`:
  1. Validates state (Posted only; Draft → throw "discard instead"; Cancelled → return false idempotent).
  2. TransactionScope wraps everything below.
  3. Per line: `IStockRepository.UpsertOnHandAsync(key, -line.ReceivedQuantity, ctx)` with `MovementType=Adjust` + `ReferenceType="ReceivingLineCancellation"` + `ReferenceId=line.Id` + `Notes="Cancelled receipt {GR-N}: {reason}"`. `CK_Stock_OnHand_NonNegative` throws if the operator already consumed the received stock — TX rolls back, controller surfaces "stock has been consumed".
  4. Per linked PO line: `IncrementLineReceivedQuantityAsync(poLineId, -qty, …)`.
  5. `SetCancellationAsync(headerId, reason, userId)` flips `Status='Cancelled'` + audit trio in one atomic UPDATE (idempotent via `WHERE Status='Posted'`).
  6. Per linked PO line: `RevertLineStatusAsync(poLineId, userId)` — server-side CASE: Received==0→'Open'; Received<Expected→'PartiallyReceived'; else→'Closed'. Cancelled lines untouched.
  7. PO header revert via new `IPurchaseOrderService.RevertStatusAfterCancelAsync(tenantId, poId)`: AnyLineHasReceipts → 'Receiving'; otherwise → 'Open'. Closed POs walk back; Cancelled POs untouched.

**Repository additions**:
- `IReceivingHeaderRepository.SetCancellationAsync(id, reason, userId)` — atomic Status flip + audit-trio populate.
- `IPurchaseOrderRepository.AnyLineHasReceiptsAsync(poId)` — predicate for header revert.
- `IPurchaseOrderRepository.RevertLineStatusAsync(poLineId, userId)` — server-side CASE-driven status revert; only updates when computed target differs (preserves Version stability).
- `StockMovementRepository.GetByReceivingHeaderAsync` SQL filter widened: `m.ReferenceType IN ('ReceivingLine', 'ReceivingLineCancellation')` so cancellation movements appear in the Activity tab.

**UI surface**:
- `Cancel receipt` QuickAction enabled only when `Status=='Posted'`; href `#cancel-modal` opens the modal via CSS `:target` selector (no JS framework — stateless, dismissed via close button's `href="#"`).
- New `_CancelReceiptModal.cshtml` partial: red header, lead text, required reason textarea (3-500 chars HTML5 validation), "Keep receipt" cancel + red "Confirm cancellation" submit. Inline styles scoped by class names; not adding to `wms-detail.css` for a one-off modal.
- Modal renders only when `ViewBag.IsPosted=true` AND `ViewBag.HeaderId` set (only on Receiving Detail).
- Cancelled receipts surface `Cancel reason` in Overview + `Cancelled` (relative time) in Properties sidebar.
- TempData banners (`CancelMessage` success / `CancelError` red) render at top of `_DetailLayout.wmsd-page` — generic enough that any future "POST then redirect to Detail" surface can reuse the same TempData keys.

**Validation**:
- `CancelReceivingViewModel` (record) — `Reason` required, 3-500 chars (DataAnnotations for client-side jQuery + FluentValidation for server-side).
- `CancelReceivingValidator` — explicit `Must(r => !string.IsNullOrWhiteSpace(r))` so whitespace-only doesn't slip past `NotEmpty()`.

**Controller endpoint**:
- `POST /Receiving/Cancel/{id}` with `[ValidateAntiForgeryToken]`. Route `id` overrides VM `Id` (route is authoritative, guards against tampering). Validation failure → TempData error + redirect to Detail. Service throws → TempData error + redirect (TX already rolled back). Success → TempData success + redirect to Detail; redirect URL resolved from `ReceivingNumber` (the Detail action's URL key).

**Tests**: +11 net (5 unit on `CancelReceivingAsync` covering blank reason / already-cancelled idempotency / Draft rejection / happy-path verification of all 5 reverse steps / blind-receipt skip; 6 controller covering happy redirect / validation-fail / already-cancelled notice / InvalidOperationException surface / Detail audit-field surface / Cancel-quick-action enable-state). Test posture: **399 passing** (was 388). 106 unit + 288 integration + 5 skipped.

**Out of scope** (still open):
- TD-026 PO Edit per-line lock (full-PO lock still in place).
- TD-027 GR Edit-Draft-Promote (Draft cannot be edited then posted; only discard-and-recreate works for Drafts).
- "Receive against" QuickAction on PO Detail still inert.
- Cancellation `CancelledBy` Guid not resolved to user name on Detail (Properties only shows the time). Could add `IUserRepository.GetById` lookup; minor.

### Day 10 — Phase 10A (PO Detail Completeness)

**Branch**: `feat/po-detail-completeness` → merged to `main` · **Tag**: `v1.0.1-po-detail-complete` · **Closes**: TD-028 + TD-029 + TD-030

Three traceability gaps caught by user smoke after the v1.0.0-inbound-mvp ship. All three tightly coupled — same surfaces, shared layout work — so bundled as a single phase rather than three loose fixes.

**Layout extension** (`_DetailLayout.cshtml`):
- New `CustomTabs: List<DetailCustomTab>` slot on `DetailPageViewModel`. Empty list keeps the legacy 4-tab Master Data surface unchanged (zero regression on Products/Customers/Warehouses Detail). Tabs render between Overview and Documents in declaration order. Each `DetailCustomTab(Key, Label, IconClass, PartialName, Count?)` drives one button + one panel; partial receives the same VM with entity-specific data via ViewBag (existing convention — `ViewBag.Lines` already used by PO Detail).
- Header `Edit` button now nullable — `editUrl` switch resolves null for entities without an Edit route (Receiving today). Previously rendered `<a href="#">` which was misleading.
- Added `PurchaseOrder` to the editUrl switch → `/PurchaseOrders/Edit/{id}`.

**TD-029 Lines tab**:
- New `PurchaseOrderLineRow` DTO (`WMS.DAL.Repositories.Inbound`) — Id, LineNumber, ProductId, ProductCode, ProductName, UomId, UomCode, ExpectedQuantity, ReceivedQuantity, Status. Phase 6D pattern (matches `StockMovementListRow`).
- New `IPurchaseOrderRepository.GetLineRowsByIdAsync(poId, ct)` — INNER JOINs `master.Products` + `master.UnitsOfMeasure`. Sorted by LineNumber.
- New `_PoLinesPanel.cshtml` — 7-col table (#, Product code/name, UoM, Expected, Received, Fill%, Status). Per-line mini progress bar color-coded green (≥100%) / amber (partial) / gray (zero). Status badge per `s-info|warning|success|neutral` variant. Empty state: "No lines added".

**TD-030 Receipts tab**:
- New `PoReceiptRow` DTO — Id, ReceivingNumber, ReceivedAt, Status, LineCount, TotalReceivedQty. Distinct from `ReceivingActivityRow` (which carries PerformedByName for the chronological feed) — Receipts table needs the qty column.
- New `IReceivingHeaderRepository.GetReceiptsByPoIdAsync(poId, ct)` — CTE for line aggregate, ORDER BY ReceivedAt DESC. Leverages existing `IX_ReceivingHeaders_PurchaseOrder` index.
- New `_PoReceiptsPanel.cshtml` — 5-col table (Receiving #, Received at + relative, Status, Lines, Total qty). Click-through to `/Receiving/Detail/{ReceivingNumber}`. Empty state: "No receipts posted yet for this PO".
- PO Detail's Activity tab still feeds from `GetActivityByPoAsync` (chronological, with PerformedByName) — the two will diverge once status-change events flow into Activity. Acceptable redundancy today.

**TD-028 Filter chip counts**:
- New `PurchaseOrderStatusCounts(All, Open, Receiving, Closed, Cancelled)` + `ReceivingStatusCounts(All, Draft, Posted, Cancelled)` records.
- New `GetStatusCountsAsync` on both repos — single `SUM(CASE WHEN Status=...)` aggregate sharing the rows query's WHERE filter EXCEPT for `@Status` (so the inactive chips still display per-status totals).
- Both controllers' `/Data` actions append `counts` object to JSON envelope.
- Index views render `<span class="wmsp-chip-count">` next to chip labels using existing Phase 8.5 picker styling. `wms-picker.css` already loaded on `_OfficeLayout` per Phase 9C hotfix `0356e2c`.
- Note: `[All]` SQL alias quoted (reserved keyword); records bind ctor positionally so column order matters.

**Tests**: +7 net (5 PurchaseOrdersAdminTests on Detail tabs + counts; 2 ReceivingControllerTests on counts). `BuildAdmin` updated to default-stub `GetStatusCountsAsync` / `GetLineRowsByIdAsync` / `GetReceiptsByPoIdAsync` / `GetActivityByPoAsync` so existing Create/Edit/Archive tests stay compiling. Test posture: **388 passing** (was 381). 101 unit + 282 integration + 5 skipped.

**Out of scope** (still open):
- TD-022 TransactionScope wrapper on receiving orchestration.
- TD-023 GR Cancellation flow (PO Detail "Cancel PO" QuickAction still inert).
- TD-027 GR Edit-Draft-Promote (Receiving Detail "Edit draft" still inert).
- "Receive against" QuickAction on PO Detail still disabled — would deep-link to `/GoodsReceipt/Create?poId={id}`. ~10 min when wanted.
- Documents tab on PO Detail wired but no upload UI (same state as Receiving Detail).

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

### Day 9 — Hotfix bundle (post-Phase-9C, pre-v1.0.0)

Three hotfixes after the initial Phase 9C tag, all rooted in two failure classes that escaped the Moq-based controller test suite. `v1.0.0-inbound-mvp` retagged to follow each fix; final tag landed at `0356e2c`.

| Commit | Bug class | Surface |
|--------|-----------|---------|
| `d8a0e52` | Dapper records — type mismatch | `PurchaseOrderListRow.ExpectedDate` declared `DateOnly?` to mirror entity; Dapper records need exact ctor-type match against SQL DATE → `DateTime?`. Reverted on the record |
| `ac2a62e` | Dapper records — column-name mismatch | `LocationRepository.GetActiveByWarehouseAsync` SELECT'd `Name` from `master.Locations`; that table has `Code` + `Description` only. Fixed via `COALESCE(Description, Code) AS Name` |
| `0356e2c` | CSS module load gap | `wms-picker.css` (defines `.wmsp-chip`) was loaded by `_AuthLayout` only; Phase 9A `/PurchaseOrders` + Phase 9C `/Receiving` use the chip class on `_OfficeLayout` pages → unstyled. Added the `<link>` to `_OfficeLayout` |

Test gap: all three survive controller tests (Moq stubs the repo) and build green. Only manual / programmatic-smoke catches them. TD-006 family — write/SQL-path tests need a real fixture; CSS module load smoke needs HTML inspection.

Memory entry written: `feedback_dapper_record_binding.md` documents the records-vs-classes binding strictness pattern. Decision rule: classes when columns evolve or types convert; records when projection is stable + you commit to verifying column names/types every time.

TD logged: TD-028 (filter chip counts on list pages — cosmetic; picker has counts because server-rendered, list pages don't because Alpine + paged JSON).

### Day 9 — Phase 9C (GR List + Detail + GRN Print) · **v1.0.0-inbound-mvp milestone**

**Branch**: `feat/phase9c-receiving-list` → merged to `main` · **Tags**: `v0.9.2-receiving-list` (merge `7373a66`) + `v0.9.2-receiving-list-fix` (`ac2a62e`) + **`v1.0.0-inbound-mvp` (final at `0356e2c`)** — see Hotfix bundle entry above for the three fixes after the initial Phase 9C ship · **Closes**: rounds out Phase 9 inbound module — Phase 9 done end-to-end on Day 9.

Three new surfaces under `/Receiving` finishing the inbound MVP:

**Data layer**:
- `ReceivingListRow` + `ReceivingFilter` + `ReceivingSortMapper` (Phase 9A trio mirror).
- `IReceivingHeaderRepository.GetPagedAsync` — LEFT JOINs PurchaseOrders + Owners (both nullable for blind receipts) + Warehouses + per-header line aggregate. Search matches ReceivingNumber OR PoNumber.
- `IStockMovementRepository.GetByReceivingHeaderAsync` — single SQL query joining `StockMovements ↔ ReceivingLines` via `(ReferenceType='ReceivingLine', ReferenceId=line.Id)` for the Detail Activity tab feed. Returns `StockMovementListRow` (resolved PerformedByName + From/To codes).
- `ReceivingStatusMapper` — wire ↔ DB (draft|posted|cancelled).

**Surfaces**:
- `/Receiving` — Alpine list. Filter chips (All/Draft/Posted/Cancelled), search by number-or-PO, sortable columns, pagination. JSON data endpoint at `/Receiving/Data`.
- `/Receiving/Detail/{number}` — shared `_DetailLayout`. 4 stat tiles (Lines / Received qty / PO link / Status); Activity tab pulls per-receipt movements via the new repo method. Quick Actions: Print GRN (active), View PO (gated on `HasPo`), Edit draft (disabled — TD-027), Cancel receipt (disabled — TD-023).
- `/Receiving/Print/{number}` — standalone GRN page. `Layout = null`; self-contained `@media print` stylesheet. Letter-style header, 4-block info dl, Lines table, total qty footer, Received-by/Verified-by signatures section. Browser-print via `window.print()` button — no PDF library dependency.

**Sidebar**: Inbound submenu now has 4 entries — Purchase Orders / Goods Receipt (new) / **Receipts** (this list) / Receive (mobile). Inbound stays active across all four contexts.

**Tests**: 11 `ReceivingControllerTests` (Index/GetData/Detail/Print happy + 404 + filter mapping + blind-receipt quick-action gating + status mapper Theory). Test posture: **381 passing** (was 370). 101 unit + 275 integration + 5 skipped.

**v1.0.0-inbound-mvp milestone summary**:
- ✅ Phase 1 — Auth (3-step login, BCrypt, multi-tenant)
- ✅ Phase 6B — Master Data (Products / Customers / Warehouses)
- ✅ Phase 7 — Master admin CRUD
- ✅ Phase 8 + 8.5 — UI polish + picker + hover
- ✅ Phase 9A — PO admin CRUD
- ✅ Phase 9B — Desktop Goods Receipt
- ✅ Phase 9C — GR list/detail/print
- ✅ Movement Log + status auto-transitions
- ✅ Phase 5 storage (documents)

**Pending** (post-MVP):
- Outbound (sales orders, picking, packing) — Phase 11+
- Mobile PWA polish (TD-007/008) — Phase 10
- Reports / dashboards
- Adjustment + cycle count flows — Phase 10/11
- ASN layer (TD-024) — Phase 11+
- Vendor master formal (TD-025) — Phase 12+
- TransactionScope wrapper (TD-022) — Phase 10
- GR cancellation (TD-023) + Edit-Draft-Promote (TD-027) — Phase 10

= **MVP B2B WMS ready** for Phase 10+.

### Day 9 — Phase 9B (Desktop Goods Receipt)

**Branch**: `feat/phase9b-goods-receipt` → merged to `main` · **Tag**: `v0.9.1-goods-receipt` · **Closes**: user feedback "ยังไม่เห็นหน้ารับสินค้าเลย" — THE Phase 9 headline.

Desktop multi-line GR form built on Phase 9A's PO infrastructure.

**Service-layer extension** (`ReceivingHeaderService.PostReceivingAsync`):
- `PostReceivingRequest` gains `IsDraft` flag (default false). When `true`, orchestration creates header + lines (Status='Draft') and stops — no stock writes, no Movement Log, no PO bumps. Audit trail of intent only.
- After non-Draft (Posted) orchestration completes, the service now calls `IPurchaseOrderService.MarkReceivingAsync` + `MarkClosedAsync` on the linked PO. Idempotent. Closes a silent gap in the legacy mobile receive flow too — POs no longer sit at 'Open' indefinitely after full-quantity receipt.

**New surface**: `/GoodsReceipt/Create` — V1 Section tabs (4):
- **Section 1 Header**: PO selector (with "Blind receipt" option) + Receiving number (auto-generated `GR-YYYYMMDD-NNNN`, override-able) + Received-at + Warehouse + Vendor (read-only display from PO) + Notes.
- **Section 2 Lines**: editable Alpine grid (Phase 9A pattern). When PO selected, AJAX to `/GoodsReceipt/PoLines/{poId}` pre-fills lines with `outstandingQuantity` (Expected − Received). Per-line columns: LineNumber, Product, UoM, Owner, Location, Expected, **Received** (over/under-flagged), Lot #, Pallet #, Remove. Visual flags: green tint on match, amber on under, red on over, no flag for blind.
- **Section 3 Documents**: placeholder — uploads happen on the Detail page (Phase 9C scope).
- **Section 4 Activity**: placeholder — populates after the receipt has events.

**Two submit modes**:
- "Save Draft" → `IsDraft=true`, Status='Draft', no stock movement.
- "Post receipt" → `IsDraft=false`, full orchestration (existing PostReceivingAsync behaviour + new PO status auto-transitions).

**Data layer**:
- New `ILocationRepository.GetActiveByWarehouseAsync` for the line Location dropdown. Filters `WarehouseId = @id AND IsActive = 1 AND Status = 'Active'`. Phase 7-style lookup-repo pattern.

**Sidebar** (`SidebarMenu/Default.cshtml`): Inbound submenu gains "Goods Receipt (new)" → `/GoodsReceipt/Create`. Existing PO + Receive (mobile) entries stay.

**JSON endpoint**: `GET /GoodsReceipt/PoLines/{poId}` returns `{ poId, poNumber, ownerId, warehouseId, lines: [...] }` filtered to Open + PartiallyReceived lines only. Closed/Cancelled excluded — they don't accept more receipts.

**Tests**: 7 `GoodsReceiptControllerTests` covering Create GET starter + PO-prefill open/partial filter, Create POST Draft vs Post mode, FV-fail path, PoLines endpoint + 404. `ReceivingHeaderServiceTests` updated to inject mock `IPurchaseOrderService`.

Test posture: **370 passing** (was 363). 101 unit + 264 integration + 5 skipped.

Out of scope (logged at end-of-9A in TD-022/023):
- TransactionScope wrapper still pending (TD-022).
- Cancellation/reversal flow (TD-023).
- Edit Draft + Promote-to-Posted flow (Phase 9C scope; the PostReceivingAsync IsDraft-handling stops at create today, no edit-and-post round-trip yet).

Foundation for: Phase 9C list/detail/print of receipts.

### Day 9 — Phase 9A (Purchase Order Admin CRUD)

**Branch**: `feat/phase9a-po-crud` → merged to `main` · **Tag**: `v0.9.0-po-crud` · **Closes**: prereq for "ยังไม่เห็นหน้ารับสินค้าเลย" (Phase 9B GR is the headline; 9A is the foundation)

Inbound module foundation phase. Phase 9 audit confirmed:
- Schema ready (Phase 6A added Movement Log integration to receiving).
- `inbound.PurchaseOrders` + `Lines` exist; `IPurchaseOrderRepository` had Create + Get only — no Paged / Update / status surface.
- No PO UI at all — couldn't create/browse POs in the app.
- Vendor = `master.Owners` with `OwnerType='Supplier'|'VMI'` (no formal Vendor master; deferred — TD-025).

Locked decisions (per the Phase 9A brief Q1–Q3):
- **Q1**: 3-section Create form (Header / Lines / Review).
- **Q2**: Full-PO lock on Edit when any line has `ReceivedQuantity > 0` (simplest safe path; per-line lock deferred to TD-026).
- **Q3**: User-entered `PoNumber` with async uniqueness check (auto-gen deferred).

Data-layer additions:
- New `IOwnerRepository.GetActiveSuppliersAsync()` — Vendor dropdown source, filtered to `OwnerType IN ('Supplier','VMI') AND IsActive=1`. Standard Phase 7 lookup-repo pattern (Carrier/Uom/Category trio).
- New `IProductRepository.GetActiveAsync()` — flat lookup for PO line grid; Status='Active' filter. `IX_Products_Status` covers WHERE+ORDER.
- New `IReceivingHeaderRepository.GetActivityByPoAsync()` — Activity tab feed for PO Detail. Same `ReceivingActivityRow` shape as Phase 6E warehouse method.
- `PurchaseOrderListRow` DTO + `PurchaseOrderFilter` record + `PurchaseOrderSortMapper` whitelist (mirrors Phase 6B trio).
- `IPurchaseOrderRepository` extensions:
  - `GetPagedAsync` — JOINs Owners + Warehouses + per-PO line aggregate (LineCount + TotalExpectedQty + TotalReceivedQty) via CTE.
  - `UpdateHeaderAsync` — `ExpectedDate` + `Notes` only; PoNumber/OwnerId/WarehouseId frozen.
  - `ReplaceLinesAsync` — DELETE + multi-INSERT atomic; called only when receipts==0.
  - `SetStatusAsync(from, to)` — atomic UPDATE WHERE Status=@from; idempotent.
  - `CountReceivedLinesAsync` + `AllLinesFullyReceivedAsync` + `CancelOpenLinesAsync` — supporting status-transition predicates and bulk-cancel.

Service-layer additions (`IPurchaseOrderService`):
- `UpdateAsync` — header + optional `ReplaceLines`. Service-side guard: rejects with `InvalidOperationException` when ReplaceLines=true AND `CountReceivedLinesAsync > 0`.
- `ArchiveAsync` — Open|Receiving → Cancelled; cascades Cancelled to open + partially-received lines.
- `MarkReceivingAsync` / `MarkClosedAsync` — idempotent atomic transitions called by 9B's GR flow once it ships.

UI surface:
- `/PurchaseOrders` list — Alpine-driven (search + status chips + table + pagination).
- `/PurchaseOrders/Create` — 3-section V1 stepper. **New pattern**: editable Alpine line grid (Section 2). `x-for` over `lines` reactive array; `x-model` per cell; indexed `Lines[N].FieldName` so ASP.NET Core model-binder rebuilds `List<Line>`. Add row / Remove row buttons.
- `/PurchaseOrders/Edit/{id}` — single-page Edit. Lines table renders read-only when receipts exist (with explanatory banner); full editable grid when zero receipts.
- `/PurchaseOrders/Detail/{id}` — shared `_DetailLayout`. 4 stat tiles (Lines / Expected qty / Received qty / Fill%); Activity tab pulls receipts.
- Sidebar Inbound module now expandable submenu (matches Master pattern): Purchase Orders + Receive (mobile).

Tests: 14 `PurchaseOrdersAdminTests` + status mapper Theory (4 cases) = +14 net. Test posture: **363 passing** (was 349). 101 unit + 257 integration + 5 skipped.

Out of scope (logged): TD-022 (TransactionScope wrapper), TD-023 (GR cancellation), TD-024 (ASN), TD-025 (formal vendor master), TD-026 (per-line lock).

Foundation for: Phase 9B desktop Goods Receipt form.

### Day 8 — Phase 8.5 (Auth Picker + Hover Cleanup)

**Branch**: `feat/auth-picker-hover` → merged to `main` · **Tag**: `v0.8.2-picker-hover` · **Closes**: user feedback "Warehouse picker เลือกยาก" + "ทุกหน้ามี hover ที่ปุ่ม/menu ไม่เอา underline"

Continuation phase from Phase 8 (which only covered Master Data forms + Detail pages). Two surfaces: the Step 3 warehouse picker (full redesign) and global hover/underline cleanup across the app.

**Picker** (`Auth/SelectWarehouse`):
- Old: scrolling list of `<button>` cards with just Code + Name. 24 demo warehouses overflowed.
- New: top purple-gradient header strip with WMS logo + "{Tenant} · Step 3 of 3" + Back button → /Auth/SelectTenant. Body: heading + search input (auto-focused, ti-search, ⌘K kbd hint) + region chips ("All [count]" / "Bangkok 6" / "North 4" / "Northeast 2" / "Central 1" / "East 4" / "South 4" / "Other 3") + region-grouped sections with ti-map-pin headers. Each warehouse row: building icon + Code (mono) + Name + city · type meta + Type badge + chevron. Hover: purple-50 fill + soft shadow. Submit posts SelectedWarehouseId, server re-validates against `GetActiveAsync` allow-list before re-issuing the cookie.
- Filter is client-side (Alpine `x-show` per row + per group), zero server roundtrips. Search matches case-insensitive against Code+Name+City baked into a `data-haystack`-style attribute at render time.
- Region grouping driven by `WarehouseRegionResolver` (new) — static city → Thai-region map. Address parsing: leading City segment before the comma. Unknown cities fall to "Other".

**Hover cleanup** (global, across ALL pages):
- Two existing `text-decoration: underline` offenders fixed in place: `.wms-login-field-link:hover` (color shift to purple-700 instead) and `.wmsd-dl dd a:hover` (same — Phase 8 missed it).
- New Section 16 in `wms-custom.css` adds:
  - Global `a` reset: `text-decoration: none; color: inherit; transition: 150ms ease`. `a:hover/focus/active` re-asserts `none`.
  - Belt-and-suspenders `!important` override on a focused list of nav-shaped surfaces (sidebar, topbar, breadcrumbs, tabs, quick-actions, dropdown items) — UA-default underlines can never re-appear.
  - Sidebar nav refinement: rounded items, softer active state (rgba 0.18 + inset 0.5px white border).
  - Standardised hover patterns on filter chips, Detail tabs (.wmsd-tab), Quick Action items (.wmsd-action — purple-50 fill, purple-700 text), and `.wms-btn-purple` (purple-700 + box-shadow on hover, purple-800 + translateY(1px) on active).
  - 150ms transition on background/border/color/shadow across all interactive elements.

**Data layer**: new `WarehousePickerItem` record in `WMS.Common.Auth` (Id, Code, Name, Address, Type) + new repo method `IWarehouseRepository.GetPickerItemsAsync`. The lighter `WarehouseInfo` (Id, Code, Name) stays for smart-skip / claim resolution paths.

**Layout**: `_AuthLayout.cshtml` now loads the new `wms-picker.css` and Alpine 3.14.1 (defer). Cost is tiny on Login + Tenant select pages but keeps the auth shell uniform.

**Out of scope (logged as TDs)**:
- TD-018 — picker empty-state DOM-query brittleness. Current x-show reads `document.querySelectorAll('.wmsp-row[style*="display: none"]')` — depends on Alpine's exact inline-style mutation. ~30 min refactor to track visibleCount reactively.
- TD-019 — Recent warehouses section deferred. No persistence layer for warehouse-pick history; cookie or IUserPreferences table is the lightest path.
- Size / staff metadata not in schema (Phase 7+ admin CRUD wouldn't touch these either; would need a schema migration). Brief mockup showed these but they were shipped as nice-to-have.
- Maintenance state badge unused — schema is bool IsActive only (TD-009). Picker's `GetActiveAsync` filter means all rows are Active anyway.

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
- ADR-009: Pack video — browser MediaRecorder (WebM/VP9), separate-POST upload, mic muted by default, 10-day Hangfire-driven retention, OUTBOUND.ORDERS perm, Safari/PDPA-log/per-station-policy deferred
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

**Last updated**: 2026-05-11 (Day 11 — Phase 23 Reports Foundation; v2.9.0-reports · 📊 first v3.0.0 chapter phase — Inventory dashboard + Order analytics + Operational KPIs + Excel export)
**Version**: 1.37
