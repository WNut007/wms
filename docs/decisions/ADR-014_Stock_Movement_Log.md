# ADR-014: Stock Movement Log

**Status**: Accepted
**Date**: 2026-05-08
**Phase**: 1 (Required for B2B launch — ADR-012 + ADR-013 depend on it)

---

## Context

`inventory.Stock` is the only source of truth for stock balances. Every
mutation (receive, putaway, adjust, transfer, pick) updates this table
in place and stamps `LastMovementAt` / `UpdatedBy`. Two columns is one
data point — there is **no history**, no way to answer:

- "What moved this stock to QuantityOnHand = 0?"
- "Who picked the last 50 units of SKU-XYZ in the last 24 h?"
- "Reconcile last week's stock against the current snapshot."

Today, only two services mutate stock:

- `ReceivingService.ReceiveLineAsync` → `IStockRepository.UpsertOnHandAsync`
- `PutawayService.PutawayStockAsync` → `IStockRepository.TransferStockAsync`

Neither writes any audit trail beyond mutating the row in place.

`docs/02_WMS_Database_Schema.md` already drafted `inventory.StockMovements`
(line 769). It was never migrated. Both **ADR-012 (Transfer)** and
**ADR-013 (Adjustment)** explicitly reference `StockMovements` as the
audit substrate they will write into — neither can ship without it.
ADR-012 specifically rejected "Direct stock move (no document)" because
of the missing audit trail; Putaway today is exactly that rejected
alternative.

This ADR fixes the gap.

## Decision

### Option A: Materialised log table (chosen)

Every mutation to `inventory.Stock` writes a corresponding immutable row
to `inventory.StockMovements`. The INSERT lives **inside the same
transaction** as the Stock UPDATE — atomicity is non-negotiable. A
movement that doesn't tie to a Stock change is a bug; a Stock change
without a movement is a bigger bug.

### Schema

```sql
CREATE TABLE inventory.StockMovements (
    Id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    -- Which Stock row was affected. NOT NULL because every movement
    -- always corresponds to exactly one Stock row (post-merge for
    -- transfers — see "Transfer = two movements" below).
    StockId         UNIQUEIDENTIFIER NOT NULL,

    -- Movement vocabulary. CHECK constraint pins the closed set so
    -- typos at insert time become errors. Adding a new type means a
    -- migration, by design.
    MovementType    VARCHAR(20) NOT NULL
        CHECK (MovementType IN
            ('Receive','Putaway','Pick','Adjust','Transfer','Return','Cycle')),

    -- Both nullable because not every type uses both:
    --   Receive  — From=NULL, To=receiving location
    --   Putaway  — From=source, To=destination
    --   Pick     — From=source, To=NULL
    --   Adjust   — From=NULL, To=NULL (qty change in place)
    --   Transfer — From=source, To=destination
    FromLocationId  UNIQUEIDENTIFIER NULL,
    ToLocationId    UNIQUEIDENTIFIER NULL,

    -- Signed delta. Receive + Adjust(increase) + Putaway(dest) = positive;
    -- Pick + Adjust(decrease) + Putaway(source) = negative. Caller
    -- supplies the sign — the column doesn't infer.
    QuantityDelta   DECIMAL(18,4) NOT NULL,

    -- Pinned at insert so movements are interpretable even if the
    -- Stock row's UoM changes (rare, but possible via reclassification).
    UomId           UNIQUEIDENTIFIER NOT NULL,
    OwnerId         UNIQUEIDENTIFIER NOT NULL,

    -- Provenance. ReferenceType is the domain entity (e.g. 'ReceivingLine',
    -- 'PutawayOperation', 'AdjustmentLine', 'PickLine'). ReferenceId
    -- points at the row in that domain table. Both nullable because:
    --   - Receive: Type='ReceivingLine', Id=<line-guid>
    --   - Putaway: Type='Putaway', Id=NULL  (TD-004 — no header table yet)
    --   - Adjust: Type='AdjustmentLine', Id=<line-guid>  (post ADR-013)
    --   - Transfer: Type='TransferLine', Id=<line-guid>  (post ADR-012)
    ReferenceType   VARCHAR(30) NULL,
    ReferenceId     UNIQUEIDENTIFIER NULL,

    -- Free-form note (mostly for adjustments). Optional.
    Notes           NVARCHAR(500) NULL,

    -- Audit. PerformedBy → security.Users (NO ACTION, same pattern as
    -- the rest of the tenant DB). NULL allowed for system actions
    -- (background jobs, future imports), per CLAUDE.md audit rules.
    PerformedBy     UNIQUEIDENTIFIER NULL,
    PerformedAt     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_StockMovements_Stock
        FOREIGN KEY (StockId) REFERENCES inventory.Stock(Id),
    CONSTRAINT FK_StockMovements_PerformedBy
        FOREIGN KEY (PerformedBy) REFERENCES security.Users(Id)
        ON DELETE NO ACTION,

    -- Per-Stock activity feed (the hot read). INCLUDE keeps the page
    -- from chasing the heap for the columns the Activity panel renders.
    INDEX IX_StockMovements_Stock (StockId, PerformedAt DESC)
        INCLUDE (MovementType, QuantityDelta, FromLocationId, ToLocationId),

    -- Provenance lookup ("what movements came from receiving line X?").
    -- Filtered to skip Putaway's NULL ReferenceId rows (TD-004) so the
    -- index stays dense for the meaningful case.
    INDEX IX_StockMovements_Reference (ReferenceType, ReferenceId)
        WHERE ReferenceId IS NOT NULL,

    -- Global activity feed ("everything in the last 24 h" reports).
    INDEX IX_StockMovements_PerformedAt (PerformedAt DESC)
);
```

Key choices vs the original draft in `docs/02`:

- **Renamed `Qty` → `QuantityDelta`** (signed) — the draft was ambiguous.
  Pick = -50, Receive = +50.
- **`MovementType` is `VARCHAR(20)` with a CHECK** rather than free text.
- **`StockId NOT NULL`** rather than nullable — every movement points
  at exactly one Stock row.
- **Added `Notes NVARCHAR(500)`** for adjustment narrative (ADR-013 will need it).
- **Added `IX_StockMovements_PerformedAt`** for "show all activity in
  the last 24 h" reports without a StockId filter.

### Transfer = two movements

A putaway / inter-warehouse transfer creates **two** rows: one with
`QuantityDelta < 0` against the source `StockId`, one with
`QuantityDelta > 0` against the destination `StockId`. Both share the
same `ReferenceType` + `ReferenceId` so they reconcile in reports.

Rationale: keeping movements 1:1 with a Stock row makes the
"per-balance history" index (`IX_StockMovements_Stock`) work without
playing tricks. The two rows live or die together inside the same
transaction.

### API shape

A new `StockMovementContext` value type carries movement-only fields
that don't belong on `IStockRepository.UpsertOnHandAsync` /
`TransferStockAsync` parameter lists:

```csharp
public sealed record StockMovementContext(
    StockMovementType MovementType,
    Guid? PerformedBy,
    string? ReferenceType = null,
    Guid?  ReferenceId = null,
    string? Notes = null);

public enum StockMovementType
{
    Receive, Putaway, Pick, Adjust, Transfer, Return, Cycle
}
```

`UpsertOnHandAsync` / `TransferStockAsync` signatures change:

```csharp
// Before
Task<Stock> UpsertOnHandAsync(StockKey, decimal delta, Guid? userId, CancellationToken);
Task<(Stock,Stock)> TransferStockAsync(Guid from, Guid toLoc, decimal qty, Guid? userId, CancellationToken);

// After
Task<Stock> UpsertOnHandAsync(StockKey, decimal delta, StockMovementContext, CancellationToken);
Task<(Stock,Stock)> TransferStockAsync(Guid from, Guid toLoc, decimal qty, StockMovementContext, CancellationToken);
```

`Guid? userId` folds into `StockMovementContext.PerformedBy`.

**Controlled refactor — no overloads.** Both call sites
(`ReceivingService`, `PutawayService`) update in the same change. We
have full control of every caller; an overload-and-deprecate dance is
cost without benefit.

### IStockMovementRepository (read-side only)

The write happens inside the existing `StockRepository` SQL batches —
keeping the writer collocated with the Stock mutation guarantees the
"same transaction" invariant. A separate `IStockMovementRepository`
covers reads:

```csharp
public interface IStockMovementRepository
{
    Task<IReadOnlyList<StockMovement>> GetByStockAsync(
        Guid stockId, int limit = 50, CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> GetByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> GetByProductAsync(
        Guid productId, DateTime? since = null, int limit = 100,
        CancellationToken ct = default);
}
```

The Activity tab on the Detail page (Phase 6B) consumes
`GetByProductAsync`. The Stock detail (future phase) consumes
`GetByStockAsync`.

### Forward-only — no backfill

Stock rows that exist today carry a single `LastMovementAt` /
`UpdatedBy`. That is one event, not a history. We deliberately do
**not** synthesize movements from existing audit columns: the result
would be a single row per Stock that says "something happened on
some-date" — a worse lie than honest absence.

After this ADR ships, Stock has movements going forward. Pre-existing
rows simply have nothing in `inventory.StockMovements`.

This is acceptable because:

- No production deployment exists yet (Phase 1 internal dev).
- Reports that want history can disclose "movements before <date> are
  not recorded."
- A backfill that loses fidelity is worse than no backfill.

If ever needed in the future: a separate migration could insert one
"baseline" movement per Stock row using `LastMovementAt` / `UpdatedBy`
with `MovementType='Cycle'` + `Notes='Baseline at log introduction'`.
We're not doing that now.

## Consequences

### Positive

- Every stock change is auditable forever — receive, putaway, future
  adjust / transfer / pick.
- Activity tab on Detail pages (Phase 6B) gets real data instead of
  hardcoded strings.
- ADR-012 (Transfer) + ADR-013 (Adjustment) implementations unblock —
  both already assume this table exists.
- Reconciliation: `SUM(QuantityDelta) per StockId` should equal
  `Stock.QuantityOnHand` minus the row's "starting balance". Powerful
  daily integrity check.
- Atomic with the Stock UPDATE — no partial-failure window where
  Stock changed but nothing got recorded.

### Negative

- **One row per stock mutation, forever.** At 5,000 B2C orders/day
  with say 3 movements per order, that's ~5.5M rows/year. The
  composite index on `(StockId, PerformedAt DESC)` keeps the hot read
  fast; partitioning by month is a Phase 2+ tuning call.
- **Schema discipline**: every new mutation path must remember to
  write a movement. Mitigation: the writer is wired inside
  `StockRepository`'s atomic batches so callers can't forget.
- **CHECK on `MovementType`** — adding a new type requires a migration.
  Not a downside; this is the point.

### Neutral

- ~150 LOC repo + service changes. ~3 unit tests updated. ~5 new
  integration tests. ~1 day of work.
- 0 user-visible changes in this phase. Phase 6B is when the Activity
  tab lights up.

## Alternatives Considered

### Alternative 1: Reduce-on-read from existing tables

Aggregate `inbound.ReceivingLines` + future `outbound.PickingLines` +
`inventory.StockAdjustments` to compute history on demand.

**Rejected**: Putaway already has no header to query. Adjustments
will write to a dedicated table, but the join across N domain tables
to reconstruct a single Stock row's history is expensive and gets
worse as we add domains. A unified log is what every reasonable WMS
ships with.

### Alternative 2: Event sourcing (StockMovements as the source of truth)

Drop `inventory.Stock` entirely; project balances from movements on
read.

**Rejected**: Too heavy for current scale. The 6-tuple Stock row + its
indexes are the hot read path for every UI. Event sourcing trades that
hot-read perf for write-side simplicity we don't need.

### Alternative 3: Status quo (no log)

**Rejected**: ADR-012 and ADR-013 cannot ship; daily data loss
continues; Activity tab stays fake. Already eliminated by the
audit conversation that produced this ADR.

## Related ADRs

- **ADR-007**: Owner concept — every movement carries OwnerId so
  per-owner reports are trivial.
- **ADR-010**: Function-CRUD permission matrix — read access to
  movements gated under `INVENTORY.STOCK.View`.
- **ADR-012**: Transfer — writes movements with
  `ReferenceType='TransferLine'`.
- **ADR-013**: Adjustment — writes movements with
  `ReferenceType='AdjustmentLine'`.
- **ADR-004 (missing)**: Hybrid putaway template + scoring — when it
  lands, putaway gets a real header table and movements gain a
  `ReferenceId` instead of NULL (closes TD-004).

## Implementation Notes

### Phase 6A scope (this ADR)

- T1: Migration `Migration_20260508_002_CreateStockMovementsTable`
- T2: Domain types — `StockMovement` entity, `StockMovementType` enum,
  `StockMovementContext` record
- T3: `IStockMovementRepository` (read-side) + factory
- T4: `IStockRepository` signature change (controlled refactor, no
  overloads), with INSERT into `StockMovements` inside the same SQL
  batches as the Stock UPDATE / MERGE
- T5: Update `ReceivingService` (`MovementType=Receive`,
  `ReferenceType='ReceivingLine'`, `ReferenceId=<line-guid>`) +
  `PutawayService` (`MovementType=Putaway`, `ReferenceType='Putaway'`,
  `ReferenceId=null`)
- T6: Update existing unit tests for the new signatures
- T7: New integration tests: receive writes 1 row; putaway writes 2
  rows (source -, dest +); reference fields round-trip
- T8 (deferred to 6B): Activity tab UI wired to `GetByProductAsync`
- T9: TECH_DEBT entry (TD-004 Putaway ReferenceId null, ADR-004 missing),
  CLAUDE.md update, merge + tag

### Tech debt logged

- **TD-004**: Putaway movements carry `ReferenceId = NULL` because
  putaway has no header/line tables yet. Closes when ADR-004 (hybrid
  putaway template + scoring) ships and introduces a header.

### Future tuning

- Partition `inventory.StockMovements` by month once row count crosses
  ~50M. Index strategy stays the same; partition switch happens
  via a separate ADR.
- Cold-storage archival (>2 years) into a parquet export. Phase 3+.

---

**Last updated**: 2026-05-08
