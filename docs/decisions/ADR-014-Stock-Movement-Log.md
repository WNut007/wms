# ADR-014: Stock Movement Log (Materialized)

**Status**: Proposed
**Date**: 2026-05-09
**Deciders**: Solo dev
**Context**: Day 6 (post-Phase 5)
**Supersedes**: None
**Related**: ADR-012 (Transfer), ADR-013 (Adjustment), Roadmap Week 5

---

## Context

### Current state

Stock mutations exist at exactly two call sites in the WMS codebase:

1. `ReceivingService.ReceiveLineAsync` → `IStockRepository.UpsertOnHandAsync`
2. `PutawayService.PutawayStockAsync` → `IStockRepository.TransferStockAsync`

Both mutate `inventory.Stock` rows transactionally. They stamp `LastMovementAt` and `UpdatedBy` on the row, but **no separate audit record is persisted**. The `Stock` table holds only the current snapshot.

This means:

- "How did this row get to its current quantity?" — unanswerable from data alone.
- "What was the stock at 09:00 last Tuesday?" — impossible.
- "Who moved 50 units of SKU-001234 last week?" — only deducible if the receiving line happens to still exist and only one move has happened since.
- The Product Detail page's Activity tab (shipped in Phase 4) is currently hardcoded mock data because there is no real source for per-product history.

### Why this is a problem now

ADR-013 (Adjustment) explicitly references `inventory.StockMovements` as a precondition: "MovementType = 'Adjust' (just a type)". Adjustment cannot ship without a movement log.

ADR-012 (Transfer) Alternative 2 explicitly rejects "Direct stock move (no document)" because of the missing audit trail. **Putaway today is exactly Alternative 2.** That's an inconsistency we accumulated by deferring the log.

`docs/02_WMS_Database_Schema.md` has already specified `inventory.StockMovements` (line 769) — full schema with `MovementType`, `From/ToLocationId`, `Quantity`, `ReferenceType`, `ReferenceId`, `PerformedBy`, `PerformedAt`, plus indexes. The design exists; the migration was never written.

The Roadmap Week 5 deliverable lists "✅ Stock movements (full audit)" — never implemented.

### Why deferring is more expensive than implementing

Each new stock-touching feature shipped without a movement log accumulates another call site that will need to be retrofitted later. Adjustment (ADR-013), Transfer (ADR-012), Outbound (Pick/Pack), and Cycle Count are all on the near-term roadmap. Retrofitting four services is more work than adding two log writes now.

The cost of writing-without-displaying is disk space (cheap). The cost of NOT writing is permanent data loss every day we wait. We cannot backfill — `Stock` carries only `LastMovementAt`.

---

## Decision

We will materialize `inventory.StockMovements` as the canonical, immutable, append-only stock history.

### Schema (lifted from docs/02)

```
inventory.StockMovements
- MovementId         uniqueidentifier  PK  default NEWSEQUENTIALID()
- TenantId           uniqueidentifier  not null  (multi-tenant, indexed)
- MovementType       varchar(20)       not null  -- Receive/Putaway/Pick/Adjust/Transfer/Return
- ProductId          uniqueidentifier  not null  FK→master.Products
- LotId              uniqueidentifier  null      FK→inventory.Lots
- PalletId           uniqueidentifier  null      FK→inventory.Pallets
- OwnerId            uniqueidentifier  null      FK→master.Owners
- UomId              uniqueidentifier  not null  FK→master.UnitsOfMeasure
- FromLocationId     uniqueidentifier  null      FK→master.Locations  (null for Receive)
- ToLocationId       uniqueidentifier  null      FK→master.Locations  (null for Pick)
- Quantity           decimal(18,4)     not null  -- always positive; direction = type
- ReferenceType      varchar(50)       null      -- 'ReceivingLine','PutawayOperation','AdjustmentLine','PickingLine'
- ReferenceId        uniqueidentifier  null      -- correlation back to source document
- PerformedBy        uniqueidentifier  not null  FK→security.Users
- PerformedAt        datetime2         not null  default SYSUTCDATETIME()
- Notes              nvarchar(500)     null

Indexes:
- IX_StockMovements_Tenant_Product       (TenantId, ProductId, PerformedAt DESC)
- IX_StockMovements_Tenant_Location      (TenantId, ToLocationId, PerformedAt DESC)
                                         (TenantId, FromLocationId, PerformedAt DESC)
- IX_StockMovements_Reference            (TenantId, ReferenceType, ReferenceId)

No DELETE permitted. No UPDATE permitted (corrections are reverse-movements).
```

### Behavior

**Forward-only.** No backfill of existing `Stock` rows. Pre-rebuild history is permanently lost; this is acknowledged.

**Transactional with the mutation.** The INSERT into `StockMovements` joins the same SQL batch / transaction that updates `inventory.Stock`. Either both happen or neither does. No new transaction boundary is introduced.

**Quantity is unsigned; direction is encoded in `MovementType` and `From/To`.**
- `Receive`: `FromLocationId = null`, `ToLocationId = receiving location`
- `Putaway`: `FromLocationId = staging`, `ToLocationId = bin`
- `Pick`: `FromLocationId = bin`, `ToLocationId = staging` (or `null` for direct ship)
- `Adjust+`: `FromLocationId = null`, `ToLocationId = bin`, positive
- `Adjust-`: `FromLocationId = bin`, `ToLocationId = null`, positive (drains)
- `Transfer`: `FromLocationId = source`, `ToLocationId = destination`

**Reference binding is best-effort.**
- `Receive` → `ReferenceType='ReceivingLine'`, `ReferenceId=<line>`
- `Putaway` → `ReferenceType='PutawayOperation'`, `ReferenceId=null` (no header table today; ADR-004 will add one)
- `Adjust` → `ReferenceType='AdjustmentLine'`, `ReferenceId=<line>` (when ADR-013 ships)
- `Transfer` → `ReferenceType='TransferLine'`, `ReferenceId=<line>` (when ADR-012 ships)

Putaway's null reference is documented tech debt, not a permanent design. ADR-004 (Hybrid Putaway: Template + Scoring) — itself a missing ADR — will introduce a `PutawayOperations` header table; movements will be backfilled with that ID via UPDATE at the moment ADR-004 lands.

---

## Consequences

### Positive

- Full audit. Every stock change becomes traceable.
- Unblocks ADR-013 (Adjustment) and ADR-012 (Transfer).
- Phase 6 Product Detail Activity tab can render real per-product movements (`SELECT TOP 20 * FROM StockMovements WHERE TenantId=@t AND ProductId=@p ORDER BY PerformedAt DESC`).
- Foundation for cycle counting, reconciliation reports, sales velocity, days-on-hand calculations.
- Consistent with industry-standard WMS patterns (SAP EWM, Oracle, Manhattan).

### Negative

- One additional INSERT per stock mutation. Same transaction, negligible overhead.
- Disk growth: ~200 bytes per movement. At 5,000 orders/day with ~3 movements/order = 15,000 rows/day = ~1 GB/year per tenant. Acceptable.
- `Stock` and `SUM(StockMovements)` can diverge if a future bug bypasses the log. Reconciliation job (out of scope) can detect this.
- Backfill impossibility: existing snapshot history is not recoverable. Acknowledged loss.

### Neutral / Tech Debt

- `ReferenceId` for Putaway is null until ADR-004 ships a header table. UPDATE backfill at that point is mechanical.
- ADR-004 itself is missing from `docs/decisions/`. Separate gap, lower priority.

---

## Alternatives Considered

### Alternative 1: Reduce-on-read from source documents

History query scans `ReceivingLines` + future `PickingLines` + future `AdjustmentLines` + future `TransferLines` and reduces.

**Rejected** because:
- Cross-table date-range queries are expensive.
- Every new stock-touching domain must add a "history view" column set.
- No standard WMS does this.
- Putaway has no source document at all today, so it would have no history under this model.

### Alternative 2: Event-sourced Stock

Stock balance is a projection over StockMovements. The `Stock` table goes away (or becomes a cached projection).

**Rejected** because:
- Larger refactor than is warranted now.
- Existing services rely on direct row updates with optimistic concurrency.
- Re-projecting on read is expensive at the volume we expect.
- We can move toward this later if needed; the chosen design doesn't preclude it.

### Alternative 3: Defer until ADR-013/012 implementation forces it

Keep direct mutation, write the log when the first feature that needs it ships.

**Rejected** because:
- Each day deferred = irrecoverable data loss.
- Retrofitting two existing services is the same cost as instrumenting them now, just with stale knowledge.
- Phase 6 Activity tab stays mock data until the log lands.

---

## Implementation Notes

Implementation tasked in `PHASE6A_MOVEMENT_LOG_BRIEF.md`.

Sequence:
1. Migration creating `inventory.StockMovements`.
2. `IStockMovementRepository` (Dapper).
3. `IStockRepository` methods updated to accept movement context and INSERT in same batch.
4. `ReceivingService` passes `ReferenceType="ReceivingLine"`, `ReferenceId=<line>`.
5. `PutawayService` passes `ReferenceType="PutawayOperation"`, `ReferenceId=null`.
6. Integration tests assert: every successful mutation produces exactly one movement row.
7. Activity tab in Detail page reads from `StockMovements` for `MovementType` filtering.

---

## Open Questions / Future Work

- ADR-004 (Hybrid Putaway): when written and implemented, populate Putaway `ReferenceId`.
- ADR-015 (proposed): Reconciliation job comparing `Stock.QuantityOnHand` to `SUM(StockMovements.signed_qty)` per key. Out of scope here.
- Outbound flow (Pick/Pack/Ship) will introduce `PickingLine` references. Schema already supports it.
- Cycle Count and Transfer module will reuse the same log.
- Reporting / analytics queries on `StockMovements` will likely warrant additional indexes; defer until query patterns are known.

---

**End of ADR-014.**
