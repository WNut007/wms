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

**Active Sprint**: Day 10 · Phase 10A + 10B + 11A + 12 + 13 + 14A shipped → tags `v1.0.1-po-detail-complete` + `v1.0.2-inbound-hardened` + `v1.1.0-adjustments` + `v1.2.0-cycle-counts` + `v1.3.0-transfers` + `v1.4.0-so-crud`
**Current Focus**: Phase 14B — Sales Order allocation + Pick task generation (parallel to Phase 9B's GR work for Inbound). Foundation now in place; allocation strategy resolver + line-status fields the next layer up.
**Blockers**: none — migrations 20260510_008 through _017 already applied to dev tenant

Update this section weekly during standups.

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

**Last updated**: 2026-05-10 (Day 10 — Phase 14A Sales Order Admin CRUD; v1.4.0-so-crud)
**Version**: 1.23
