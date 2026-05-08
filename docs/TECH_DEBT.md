# Tech Debt Tracker

Track technical debt items - things we've intentionally deferred or
shortcuts taken that need cleanup later. Updated as discovered,
closed when fixed.

## Conventions

- **ID format**: TD-NNN sequential (TD-001, TD-002, etc.)
- **Reference in code**: Use comment `// TODO(TD-XXX): description` near affected code
- **Add immediately**: Don't wait — log when discovered
- **Close with commit**: Move to Closed section with commit hash
- **Review**: Quarterly or before each release

## Status Legend

- 🔄 **Open** - Active debt, scheduled for fix
- ⚠️ **Watching** - Acceptable for now, monitor for triggers
- ✅ **Closed** - Resolved
- ❌ **Wontfix** - Acknowledged but won't be fixed (with reasoning)

---

## Open Items

| ID | Title | Priority | Discovered | Plan | Notes |
|----|-------|----------|------------|------|-------|
| TD-004 | Putaway StockMovements rows carry ReferenceId = NULL | Medium | 2026-05-08 | Closes when ADR-004 (hybrid putaway template + scoring) lands and introduces a putaway header table; backfill via UPDATE matching ReferenceType='Putaway' AND PerformedAt range is then trivial | Per ADR-014. Pinned by PutawayServiceTests.PutawayStockAsync_PassesNullReferenceId_TD004 |
| TD-005 | ADR-004 (Hybrid Putaway: Template + Scoring) missing as a doc | Low | 2026-05-08 | Draft alongside the suggestion-engine implementation. Referenced by name in `docs/01_WMS_Master_Design.md` and `IPutawayService` comments but no file in `docs/decisions/` | Discovered during ADR-014 audit |
| TD-006 | StockMovements write-path integration tests need a real SQL Server fixture | Low | 2026-05-08 | Stand up testcontainers / LocalDB fixture when ADR-013 / ADR-012 implementations land — they'll need it too. Then remove the `Skip` attributes on `StockMovementLogTests` (5 cases already authored, intent-complete) | Currently relying on manual smoke + Moq-level context-binding tests in unit suite |
| TD-007 | `/Receive` page uses raw layout (no `_OfficeLayout`) | Medium | 2026-05-08 | Migrate to `_OfficeLayout` + Master Data form pattern (sidebar + topbar + breadcrumb + wms-custom.css). Address in a dedicated UI-cleanup phase, or in Phase 6B if time allows | Built in Day 3-4 alongside `ReceivingService`. Functional but visually inconsistent with the Phase 1+ design system |
| TD-008 | `/Putaway` page uses raw layout (no `_OfficeLayout`) | Medium | 2026-05-08 | Same migration as TD-007 — `_OfficeLayout` + Master Data form pattern | Built in Day 3-4 alongside `PutawayService`. Same issues + refactor approach as TD-007 |
| TD-009 | `master.Warehouses` has no `Status` string — UI lost the "maintenance" intermediate state | Low | 2026-05-08 | If business needs "maintenance" back: add `Status` NVARCHAR(20) NOT NULL DEFAULT 'Active' + CHECK ('Active'/'Maintenance'/'Inactive'); migrate `IsActive=1`→`Status='Active'`, `IsActive=0`→`Status='Inactive'`; drop `IsActive`. Frontend filter chip + statusBadge already accept 'maintenance' as a no-op so the migration is purely additive | Phase 6B intentionally collapsed mock's 3 states (`active`/`maintenance`/`inactive`) onto the existing `IsActive` bool. WarehouseStatusMapper.FromWire("maintenance") returns null (drops the filter); pinned by `WarehousesControllerTests.GetData_UnknownStatus_DropsFilter_TD009` |
| TD-011 | Customer.TotalOrders / LastOrderDate / Last-order-relative stubbed `null` / `"—"` | Low | 2026-05-08 | Wire when an orders schema lands (Phase 7+). Same pattern as TD-010 — repo first, controller swap is small. Detail stat tiles (Total orders, YTD revenue, Last order, Avg ticket) all stubbed `"—"` and pinned by `CustomersControllerTests.Detail_StatTiles_AllStubbed_TD011` | No outbound order schema exists today; mock had fake values like `TotalOrders × 4500` for "YTD revenue" — preserved as stub until real data flows |
| TD-012 | Product price column missing — pricing is owner-scoped on `master.ProductOwners.SettlementPrice` | Low | 2026-05-09 | When admin CRUD lands (Phase 7+), resolve price via the customer/owner context active on the request. List view `fmtPrice(null)` already renders `"—"` so the column degrades cleanly until then. Detail page Price tile dropped entirely | Discovered during T8. Mock used a fake random per-product price; schema design has no such column because pricing legitimately differs per Owner-Product mapping (3PL/VMI vs Self) |
| TD-013 | jQuery missing on `_AuthLayout` — `jquery.validate` scripts load without jQuery itself | Low | 2026-05-09 | Add a jQuery `<script>` tag before the validation library references in `Views/Shared/_AuthLayout.cshtml`. ~5–10 min in a dedicated UI-cleanup phase | Pre-existing from Day 3 auth implementation, surfaced during Phase 6B smoke test on 2026-05-09. Cosmetic only — console errors on `/Auth/Login` (and likely the tenant/warehouse selection pages that share the layout); form submit still works because server-side validation handles it |
| TD-014 | Activity tab on Customer + Warehouse Detail still on hardcoded mock entries | Low | 2026-05-09 | Customer activity needs an outbound-orders + CRM-notes stream (Phase 7+ orders schema dependency). Warehouse activity needs a unified feed across receiving headers + cycle counts + putaway operations — no single repo today. Both are bigger than TD-010 was; Phase 6C scoped Products only on purpose | Phase 6C scoped to Products only because `IStockMovementRepository.GetByProductAsync` was the lone real-data source available. Customer/Warehouse panels still show hardcoded entries that vaguely match the entity's lifecycle |
| TD-016 | Putaway operations render as 2 separate rows on the Activity tab — no pairing into a single logical entry | Low | 2026-05-09 | Naturally closes alongside TD-004: once ADR-004 introduces a putaway header table, the source + destination movements share a `ReferenceId`, and the renderer can group by `(ReferenceType, ReferenceId)` to fold paired rows into a single "moved {qty} from {from} to {to}" entry. Until then, splitting the location clause by `QuantityDelta` sign keeps each row grammatical (source→"from STAGE-01", dest→"into BIN-A1") | Phase 6D mapper choice. Both rows DO have FromLocationId + ToLocationId populated (per Phase 6A's putaway writer), so a future grouping pass has the data it needs without a SQL change |

---

## Closed Items

| ID | Title | Priority | Discovered | Resolved | Commit | Notes |
|----|-------|----------|------------|----------|--------|-------|
| TD-001 | Audit field standardization | Medium | 2026-05-04 | 2026-05-04 | cf01fc3, 0c65fe3 | 10 tables (3 Master + 7 Tenant), 24 columns added, idempotent + roundtrip-tested |
| TD-002 | FK_Customers_Carrier orphan | Medium | 2026-05-04 | 2026-05-04 | d562cd3 | FK added by migration 027 with ON DELETE SET NULL after Carriers (021) landed |
| TD-003 | ProductCategories.Path / Name type mismatch | Low | 2026-05-05 | 2026-05-05 | e08dc40 | Migration 032 widens Path from VARCHAR(500) to NVARCHAR(500); IX_Categories_Path dropped + recreated around the ALTER |
| TD-010 | Activity tab on `/Products/Detail/{sku}` still showed hardcoded mock entries | Medium | 2026-05-08 | 2026-05-09 | c26998d, 5fd4d37 | Phase 6C: `MovementActivityMapper` (T1, c26998d) maps `StockMovement → ActivityItem`; `ProductsController.Detail` reads via `IStockMovementRepository.GetByProductAsync(productId, limit: 20)` (T2, 5fd4d37). Empty timelines for fresh products + DEMO-001 are the expected default — `_ActivityPanel` renders "No activity yet." in that case |
| TD-015 | `MovementActivityMapper` didn't resolve actor names or location codes | Low | 2026-05-09 | 2026-05-09 | bb2a114 | Phase 6D: SQL-JOIN strategy (chose over batch-lookup to match `ProductRepository`/`WarehouseRepository` JOIN-with-DTO pattern). New `StockMovementListRow` carries resolved `PerformedByName` + From/To location codes; `IStockMovementRepository.GetByProductAsync` LEFT JOINs `security.Users` (`COALESCE(FullName, Email, 'System')`) + `master.Locations × 2`. Mapper title now reads `"<b>Maya Rodriguez</b> received 5 units at WH-MAIN"`; actor + location HTML-encoded against XSS. Spawned TD-016 (putaway-pair grouping) |

---

## Won't Fix

(empty)

---

## Process

### When you discover debt:
1. Add row to "Open Items" with next TD-NNN
2. Add comment in code: `// TODO(TD-XXX): summary`
3. Commit: `docs(tech-debt): add TD-XXX <title>`

### When you fix debt:
1. Move row from Open → Closed
2. Add commit hash
3. Update related code comments
4. Commit: `docs(tech-debt): close TD-XXX <title>`

### When deferring permanently:
1. Move to "Won't Fix"
2. Add reasoning in Notes
3. Commit: `docs(tech-debt): wontfix TD-XXX <title>`
