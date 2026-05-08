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

---

## Closed Items

| ID | Title | Priority | Discovered | Resolved | Commit | Notes |
|----|-------|----------|------------|----------|--------|-------|
| TD-001 | Audit field standardization | Medium | 2026-05-04 | 2026-05-04 | cf01fc3, 0c65fe3 | 10 tables (3 Master + 7 Tenant), 24 columns added, idempotent + roundtrip-tested |
| TD-002 | FK_Customers_Carrier orphan | Medium | 2026-05-04 | 2026-05-04 | d562cd3 | FK added by migration 027 with ON DELETE SET NULL after Carriers (021) landed |
| TD-003 | ProductCategories.Path / Name type mismatch | Low | 2026-05-05 | 2026-05-05 | e08dc40 | Migration 032 widens Path from VARCHAR(500) to NVARCHAR(500); IX_Categories_Path dropped + recreated around the ALTER |

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
