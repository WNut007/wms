# ADR-016: DB-per-Tenant Confirmed for v3.0.0 Launch

**Status**: Accepted
**Date**: 2026-05-14
**Decision makers**: Project owner
**Supersedes**: re-affirms ADR-001 (Multi-tenant DB per tenant); pairs with ADR-019

---

## Context

Heading into Phase 26 (Deployment Foundation), the team revisited the
multi-tenant strategy. ADR-001 had locked DB-per-tenant in Phase 1; by
Phase 26 we had 30+ migrations, 20+ schema tables, and ~1,000 tests
all written against that assumption. The question was whether to:

1. **Stay with DB-per-tenant** for v3.0.0 launch (status quo)
2. **Refactor to hybrid** (shared DB + TenantId column for SMB; separate DB for enterprise) before launch
3. **Refactor to single-DB-multi-tenant** (shared DB + TenantId everywhere)

Drivers for revisiting:
- Hybrid would enable a future SMB tier with much lower per-tenant cost
- A 2-3 week refactor pre-launch would be cheaper than retrofitting later
- Customer interviews suggested SMB market might want a low-cost entry point

Drivers against revisiting:
- ADR-015 commits to Land + Expand — SMB is post-launch, not pre-launch
- Pre-revenue refactor delays the first deal
- Enterprise customers expect isolation; selling them shared-DB is a procurement nightmare
- Existing tests assume connection-per-tenant; rewriting tests = additional weeks
- Hybrid actually requires BOTH patterns in code (worst-of-both) until the SMB tier launches

---

## Decision

**DB-per-tenant remains the architecture for v3.0.0.** ADR-001 holds.

Hybrid DB strategy is deferred to v3.1+ (TD-065 / ADR-019).

---

## Rationale

- **Sales-led launch targets enterprise** (ADR-015). Enterprise customers
  explicitly want hard isolation. DB-per-tenant is the gold standard.
- **No SMB customer to serve yet**. Refactoring for a hypothetical
  future tier is premature optimisation. ADR-019 captures the exact
  trigger that would justify the refactor.
- **Code + tests are aligned**. ~1,000 tests + 30 migrations + DAL
  factory pattern all assume DB-per-tenant. Refactoring breaks them
  all simultaneously.
- **Operational complexity is acceptable today**. 1-10 tenants = 1-10
  DBs. SQL Server handles this trivially. Hangfire in Master DB,
  audit log in Master DB — already cross-tenant by design (Phase 17 /
  Phase 24 / Phase 27).
- **Cost difference is small at our scale**. SQL Server per-instance
  pricing dominates; per-DB cost is marginal until you have 100s of
  tenants. We're not there.

---

## Consequences

### Positive
- Zero refactor work pre-launch — Phase 26 + 27 + 28 + 29 ship on time
- Enterprise procurement story is clean: "each customer's data lives in
  its own SQL Server database with its own access controls"
- Tenant suspension / hard-delete / data export operate on whole-DB
  units (TD-079 / TD-085 / TD-086) — easier conceptually than
  filtering by TenantId column
- Failure isolation: a corrupted tenant DB doesn't affect other tenants
- Backup / restore per-tenant is one-line (`RESTORE DATABASE ...`)

### Negative
- Per-tenant cost is higher (acceptable at v3.0.0 scale)
- N tenants × ~30 migrations to run on each deploy (mitigated by Phase 26's `tenants` fan-out coordinator)
- SMB-tier opportunity cost — can't serve customers below ~$15K ACV today

### Operational implications

- **Phase 26's `up tenants`** fan-out coordinator is the production migration tool
- **Phase 27's TenantProvisioningService** does `CREATE DATABASE` per tenant + runs migrations inline
- **Per-tenant backup** is the responsibility of the SQL Server backup schedule (runbook.md Section 6)
- **Master DB carries cross-tenant data**: `master.Tenants`, `master.UserTenantMap`, `master.SuperAdmins`, `master.SystemAuditLog`, Hangfire schema
- **Connection caching** in `ITenantConnectionFactory` (5-min IMemoryCache TTL) keeps the per-request resolution cheap

---

## When to revisit

This decision should be re-opened when:

1. **MAU > 1,000 across 100+ tenants** — SQL Server per-DB overhead becomes operationally visible
2. **First SMB customer signed** — sales pressure to serve sub-$15K ACV
3. **DBA pushback** — SQL Server team flags that we're hitting per-instance limits
4. **Migration time > 30min for `up tenants`** — fan-out duration creates unacceptable maintenance windows
5. **Backup storage cost dominates infrastructure** — small-tenant per-DB backups become 80%+ of storage

Until then, ADR-001 / ADR-016 hold. ADR-019 documents the trigger and target architecture for the eventual change.

---

## Related ADRs

- [ADR-015 — Land + Expand GTM Strategy](./ADR-015_Land_Expand_GTM_Strategy.md)
- [ADR-019 — Hybrid DB Strategy Deferred to v3.1+](./ADR-019_Hybrid_DB_Strategy_Deferred.md)
- ADR-001 — Multi-tenant DB per Tenant (informal in CLAUDE.md)
