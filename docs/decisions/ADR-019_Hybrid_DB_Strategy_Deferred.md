# ADR-019: Hybrid DB Strategy Deferred to v3.1+

**Status**: Accepted (deferral)
**Date**: 2026-05-14
**Decision makers**: Project owner
**Pairs with**: ADR-016 (DB-per-Tenant confirmed for v3.0.0)

---

## Context

Phase 26 (Deployment Foundation) revisited the multi-tenant strategy
as production deployment loomed. Three viable patterns:

1. **DB-per-tenant** (status quo, ADR-001 / ADR-016) — each customer's
   data in a separate SQL Server database
2. **Single-DB-multi-tenant** — all customers share one DB with
   TenantId column on every operational table
3. **Hybrid** — enterprise customers on dedicated DBs; SMB customers
   share a single multi-tenant DB

The Land + Expand strategy (ADR-015) commits to enterprise-first launch,
so v3.0.0 doesn't immediately serve the SMB market. But the future SMB
tier matters strategically:

- **Lower per-tenant cost** — sharing infrastructure across many small
  customers makes sub-$15K ACV economics work
- **Faster provisioning** — INSERT row in shared DB vs CREATE DATABASE +
  ~30 migrations
- **Easier cross-tenant analytics** — single query against shared tables
- **Larger addressable market** — SMB has 10x the customer count of
  enterprise

The question was whether to refactor pre-v3.0.0 or defer.

---

## Decision

**Defer the Hybrid DB Strategy to v3.1+.**

Tracked as TD-065. Triggered by the conditions in ADR-016's "When to
revisit" section, or by external pressure (first SMB customer signed,
SaaS funnel needs lower-cost tier, etc.).

---

## Rationale

### Current commitments preclude pre-launch refactor

- ~1,000 tests assume DB-per-tenant (factory pattern, connection
  resolution, audit log isolation, etc.)
- 30+ migrations are tagged Tenant — refactoring to add `TenantId`
  on every table requires rewriting most of them
- Phase 26 deployment + Phase 27 SuperAdmin both committed to DB-per-
  tenant operationally (`tenants` fan-out coordinator, CREATE DATABASE
  in provisioning)
- 2-3 weeks of refactor work pre-launch delays the first deal — bad
  trade given Land + Expand cash-funded strategy

### No customer asking for hybrid today

The customer set is exactly the audience for DB-per-tenant: enterprise
customers who pay for isolation, value per-customer backup/restore,
need compliance-grade tenant separation. They WANT the dedicated DB
model.

Refactoring for a hypothetical future SMB market is the textbook
"premature optimisation" case. Wait until a real customer raises the
need.

### Hybrid is actually more complex than either pure model

A hybrid system requires BOTH patterns in code:
- Connection factory must resolve EITHER a per-tenant DB OR a
  TenantId filter onto the shared DB
- Every query must know which mode the tenant is in
- Migrations must apply to BOTH the shared DB (with TenantId) AND
  per-tenant DBs (without)
- Audit log writes must dispatch correctly per mode
- Backup / restore semantics differ per mode (one-DB restore for
  enterprise, row-set export for SMB)

This is "worst-of-both" until the SMB tier actually launches. Building
it speculatively means maintaining the complexity without the
corresponding revenue.

---

## Target architecture (when triggered)

When the hybrid refactor is justified, target architecture:

### Shared SMB DB

- New database: `WMS_Shared` (or `WMS_SMB`)
- All Tenant-tagged tables get `TenantId UNIQUEIDENTIFIER NOT NULL` column with FK to `master.Tenants(Id)`
- Every index gets `TenantId` as leading column (or composite with current key)
- Every query gets `WHERE TenantId = @currentTenantId` injected (via Dapper interceptor or repo base class)
- `master.Tenants` gains `TenantMode VARCHAR(20) NOT NULL` = `'Dedicated' | 'Shared'`
- New SMB tenants → `Shared` mode → INSERT into shared DB instead of CREATE DATABASE
- Existing enterprise tenants stay `Dedicated`

### Migration adjustments

- Tenant-tagged migrations apply to:
  - `WMS_Shared` once (idempotent re-run safe)
  - Each Dedicated tenant's DB (existing flow)
- Migrations must include `TenantId` on every CREATE TABLE — old migrations need ALTER TABLE wraps to add the column safely
- Seed migrations split: shared-tenant seeds vs per-dedicated-tenant seeds

### Connection factory changes

- `ITenantConnectionFactory.CreateConnection(tenantId)` checks `master.Tenants.TenantMode`:
  - `Dedicated` → existing per-tenant connection
  - `Shared` → fixed shared-DB connection
- Repo base class injects `TenantId` parameter into every query for shared-mode tenants
- Audit log writes adjust accordingly

### Estimated effort

- ~2-3 weeks dev work
- All ~1,000 tests revisited (most pass as-is; ~200 need adjustment for the new repo base)
- New test suite for shared-DB isolation (no tenant-A query returns tenant-B rows)
- One round of penetration testing focused on cross-tenant data leak (a tightly-bounded scope but high stakes)

---

## When to revisit (triggers)

Revisit the deferral when ANY of:

1. **First SMB customer signed** — sales pressure for sub-$15K ACV economics
2. **MAU > 500 across 50+ tenants** — operational overhead of N DBs becomes painful
3. **SaaS funnel launched** (v3.1+) — self-service signup volume requires lower-cost tier
4. **DBA pushback** — SQL Server team flags per-DB resource limits
5. **Backup/storage cost dominates** — small-tenant backup storage becomes 80%+ of infra cost
6. **Cross-tenant analytics requirement** — a customer or internal team needs aggregate queries across all tenants (today: stitched together at app layer, painful)

Until then, ADR-016 / ADR-001 hold.

---

## Consequences

### Positive
- Zero pre-launch refactor effort
- v3.0.0 ships on time for enterprise customers
- Decision documented + revisit triggers clear
- Code stays simpler (one pattern, not two)

### Negative
- Can't serve SMB until refactor lands (acceptable per ADR-015)
- Marketing message has to be enterprise-only until v3.1
- Refactor cost grows as more migrations + tests accumulate (mitigated by deferring not the work itself — when triggered, we still have to do it)

---

## Related ADRs

- [ADR-001 — Multi-tenant DB per Tenant](./) (informal in CLAUDE.md)
- [ADR-015 — Land + Expand GTM Strategy](./ADR-015_Land_Expand_GTM_Strategy.md)
- [ADR-016 — DB-per-Tenant Confirmed for v3.0.0](./ADR-016_DB_per_Tenant_Confirmed_v3.md)

## Related TDs

- TD-065 — Hybrid DB strategy deferred
- TD-072 — Distributed cache for multi-instance (related: shared-DB SMB tier needs distributed cache too)
