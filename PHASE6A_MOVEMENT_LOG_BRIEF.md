# Phase 6A — Stock Movement Log Implementation

**Branch**: `feat/movement-log-impl` (new, from `main`)
**Goal**: Implement `inventory.StockMovements` per **ADR-014** + retrofit existing stock mutations
**Time estimate**: 1 day (~6–8 hours)
**Approach**: Atomic commits, integration tests for write path, no UI changes yet
**Tag**: `v0.6.0-movement-log`
**Sequencing**: BEFORE Phase 6B (Real Master Data); T8 deferred to 6B.

---

## Pre-flight

```bash
cd C:\dev\wms
git status
git checkout main
git checkout -b feat/movement-log-impl
```

Read first:
- `docs/decisions/ADR-014_Stock_Movement_Log.md` — the schema authority
- Existing `IStockRepository.UpsertOnHandAsync` and `TransferStockAsync` in
  `src/WMS.DAL/Repositories/Inventory/StockRepository.cs`
- Existing `ReceivingService` (`src/WMS.BLL/Services/Inbound/ReceivingService.cs`)
  and `PutawayService` (`src/WMS.BLL/Services/Inbound/PutawayService.cs`) to
  understand transaction boundaries
- Existing repository factory pattern, e.g.
  `src/WMS.DAL/Repositories/Master/WarehouseRepositoryFactory.cs`

---

## Decisions Locked (per ADR-014)

```
✅ Materialized log (Option A)
✅ Forward-only — no backfill
✅ Transactional with mutation (same SQL batch)
✅ SIGNED QuantityDelta — direction encoded in the column, not MovementType
✅ One row per stock-row mutation
✅ Putaway / Transfer = TWO rows (source -qty, dest +qty)
✅ StockId FK — every movement points at exactly one inventory.Stock row
✅ NO TenantId column — DB-per-tenant architecture (ADR-001) makes it redundant
✅ FK PK column names match codebase: Id everywhere (NOT ProductId/UomId/etc.)
✅ PerformedBy NULLABLE per CLAUDE.md "Audit Field FK Rules"
✅ Putaway ReferenceId = null for now (ADR-004 will fix — see TD-004)
✅ Receive ReferenceId = ReceivingLineId
✅ No DELETE / no UPDATE — append-only
✅ MovementType values: Receive, Putaway, Pick, Adjust, Transfer, Return, Cycle
   (only Receive + Putaway used in this phase)
```

---

## Task Breakdown

### T1 — Migration

**File**: `tools/WMS.Migrate/Migrations/Tenant/Migration_20260508_002_CreateStockMovementsTable.cs`

Match existing FluentMigrator naming (`Migration_YYYYMMDD_NNN_Description`,
`[Migration(YYYYMMDDNNN L)]`, `[Tags("Tenant")]`). Numbering follows
documents migration `20260508001`.

Schema (per ADR-014):

```sql
CREATE TABLE inventory.StockMovements (
    Id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),

    -- Every movement ties to exactly one Stock row. NOT NULL so the
    -- per-Stock history index is dense and reconciliation
    -- (SUM(QuantityDelta) per StockId) is always well-defined.
    StockId         UNIQUEIDENTIFIER NOT NULL,

    -- Closed enum + DB-side CHECK so typos at insert time become
    -- errors. Adding a type means a follow-up migration, by design.
    MovementType    VARCHAR(20)      NOT NULL,

    -- Both nullable. Receive: From=NULL, To=loc. Pick: From=loc, To=NULL.
    -- Putaway/Transfer: both set. Adjust (in-place): both NULL.
    FromLocationId  UNIQUEIDENTIFIER NULL,
    ToLocationId    UNIQUEIDENTIFIER NULL,

    -- Signed delta — receive/dest = positive; pick/source = negative.
    -- Reconciliation: SUM(QuantityDelta) per StockId equals current
    -- Stock.QuantityOnHand minus the row's pre-history starting balance.
    QuantityDelta   DECIMAL(18,4)    NOT NULL,

    -- Pinned at insert so movements stay interpretable even if the
    -- Stock row's UoM/Owner changes (rare, via reclassification).
    UomId           UNIQUEIDENTIFIER NOT NULL,
    OwnerId         UNIQUEIDENTIFIER NOT NULL,

    -- Provenance back to the domain row that drove this movement.
    --   Receive:  Type='ReceivingLine', Id=<line guid>
    --   Putaway:  Type='Putaway',       Id=NULL  (TD-004 — no header yet)
    --   Adjust:   Type='AdjustmentLine',Id=<line guid>  (post ADR-013)
    --   Transfer: Type='TransferLine',  Id=<line guid>  (post ADR-012)
    ReferenceType   VARCHAR(30)      NULL,
    ReferenceId     UNIQUEIDENTIFIER NULL,

    -- Free-form note (mostly for adjustments).
    Notes           NVARCHAR(500)    NULL,

    -- Audit. PerformedBy NULL allowed for system actions per CLAUDE.md
    -- audit rules (mirrors security.AuditLog migration 039).
    PerformedBy     UNIQUEIDENTIFIER NULL,
    PerformedAt     DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_StockMovements PRIMARY KEY (Id),

    CONSTRAINT CK_StockMovements_MovementType CHECK (
        MovementType IN ('Receive','Putaway','Pick','Adjust','Transfer','Return','Cycle')
    ),

    -- All FKs target the actual PK column (.Id). No ON DELETE CASCADE —
    -- movements outlive any single domain row; deletions of referenced
    -- entities should be soft (IsActive=0), per ADR + CLAUDE.md.
    CONSTRAINT FK_StockMovements_Stock
        FOREIGN KEY (StockId)        REFERENCES inventory.Stock(Id),
    CONSTRAINT FK_StockMovements_FromLoc
        FOREIGN KEY (FromLocationId) REFERENCES master.Locations(Id),
    CONSTRAINT FK_StockMovements_ToLoc
        FOREIGN KEY (ToLocationId)   REFERENCES master.Locations(Id),
    CONSTRAINT FK_StockMovements_Uom
        FOREIGN KEY (UomId)          REFERENCES master.UnitsOfMeasure(Id),
    CONSTRAINT FK_StockMovements_Owner
        FOREIGN KEY (OwnerId)        REFERENCES master.Owners(Id),
    CONSTRAINT FK_StockMovements_PerformedBy
        FOREIGN KEY (PerformedBy)    REFERENCES security.Users(Id)
        ON DELETE NO ACTION,

    -- Per-Stock activity feed (the hot read).
    INDEX IX_StockMovements_Stock
        (StockId, PerformedAt DESC)
        INCLUDE (MovementType, QuantityDelta, FromLocationId, ToLocationId),

    -- Provenance lookup ("what movements came from receiving line X?").
    -- Filtered to skip Putaway's NULL ReferenceId rows so the index is
    -- dense for the meaningful case.
    INDEX IX_StockMovements_Reference
        (ReferenceType, ReferenceId)
        WHERE ReferenceId IS NOT NULL,

    -- Global activity feed ("everything in the last 24 h" reports).
    INDEX IX_StockMovements_PerformedAt (PerformedAt DESC)
);
```

Implementation notes:
- Use FluentMigrator's fluent API for the basic `Create.Table(...)` and
  most FKs/columns. Drop to `Execute.Sql(...)` for the CHECK
  constraint, the `INCLUDE` index, and the partial `WHERE` index — same
  pattern migrations 039 (`IX_AuditLog_Entity`) and 050 use.
- No `BaseEntity` audit columns (`UpdatedAt`/`Version`) — movements are
  immutable; `PerformedAt` is the only timestamp. Mirrors
  `security.AuditLog` from migration 039.
- `Down()` drops the table (and all indexes/CHECK with it).

**Apply** to `WMS_Tenant_Template` after the file builds:

```bash
dotnet build tools/WMS.Migrate
dotnet run --project tools/WMS.Migrate -- list tenant   # confirm pending
dotnet run --project tools/WMS.Migrate -- up tenant     # apply
```

**Commit**: `feat(db): add inventory.StockMovements per ADR-014`

---

### T2 — Domain types

**File**: `src/WMS.Domain/Entities/Inventory/StockMovement.cs`

```csharp
namespace WMS.Domain.Entities.Inventory;

// Maps to inventory.StockMovements. Append-only — no audit columns
// beyond PerformedBy/PerformedAt because the row IS the audit record.
// Doesn't inherit BaseEntity for the same reason.
public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid StockId { get; set; }
    public StockMovementType MovementType { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public decimal QuantityDelta { get; set; }
    public Guid UomId { get; set; }
    public Guid OwnerId { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public Guid? PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }
}

public enum StockMovementType
{
    Receive,
    Putaway,
    Pick,
    Adjust,
    Transfer,
    Return,
    Cycle,
}
```

Dapper note: `MovementType` is stored as VARCHAR(20). Map via Dapper's
`SqlMapper.AddTypeHandler` for the enum, OR keep a `string MovementTypeRaw`
column and a computed property — pick whichever the existing code prefers.
Easiest is to set the column type as enum + register a one-line
`EnumStringHandler<StockMovementType>` in `Program.cs`.

**File**: `src/WMS.Common/Inventory/StockMovementContext.cs`

```csharp
using WMS.Domain.Entities.Inventory;

namespace WMS.Common.Inventory;

// Context that flows into IStockRepository write methods so the repo
// can compose the matching StockMovements INSERT inside the same SQL
// batch. PerformedBy is nullable per CLAUDE.md audit rules.
public sealed record StockMovementContext(
    StockMovementType MovementType,
    Guid? PerformedBy,
    string? ReferenceType = null,
    Guid?  ReferenceId = null,
    string? Notes = null);
```

Living next to `StockKey` (also in `WMS.Common.Inventory`).

**Commit**: `feat(domain): add StockMovement + StockMovementType + StockMovementContext`

---

### T3 — `IStockMovementRepository` (read-side only)

Match the existing factory pattern (`WarehouseRepositoryFactory`,
`StockRepositoryFactory`) — interface + class + factory, all under
`src/WMS.DAL/Repositories/Inventory/`.

**File**: `src/WMS.DAL/Repositories/Inventory/IStockMovementRepository.cs`

```csharp
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

public interface IStockMovementRepository
{
    // Per-Stock activity feed — uses IX_StockMovements_Stock seek.
    Task<IReadOnlyList<StockMovement>> GetByStockAsync(
        Guid stockId, int limit = 50, CancellationToken ct = default);

    // Provenance lookup — uses IX_StockMovements_Reference (partial).
    Task<IReadOnlyList<StockMovement>> GetByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken ct = default);

    // Per-product activity feed. JOINs inventory.Stock to filter by
    // ProductId — accepts a 2-step seek (IX_Stock_Product → per-Stock
    // history) rather than denormalising ProductId on the movement
    // row. Phase 1 OK; revisit at Phase 2 if reports are slow.
    Task<IReadOnlyList<StockMovement>> GetByProductAsync(
        Guid productId, DateTime? since = null, int limit = 100,
        CancellationToken ct = default);
}
```

**File**: `src/WMS.DAL/Repositories/Inventory/IStockMovementRepositoryFactory.cs`

```csharp
namespace WMS.DAL.Repositories.Inventory;

public interface IStockMovementRepositoryFactory
{
    IStockMovementRepository For(Guid tenantId);
}
```

**File**: `src/WMS.DAL/Repositories/Inventory/StockMovementRepository.cs`

Bound to a tenant connection in its ctor (mirrors `WarehouseRepository`,
`StockRepository`). NO `WHERE TenantId = …` filters — the connection IS
tenant-scoped. Dapper queries against the actual column list.

```csharp
public Task<IReadOnlyList<StockMovement>> GetByStockAsync(
    Guid stockId, int limit = 50, CancellationToken ct = default) =>
    _connection.QueryAsync<StockMovement>(new CommandDefinition(
        @"SELECT TOP (@limit) Id, StockId, MovementType, FromLocationId, ToLocationId,
                 QuantityDelta, UomId, OwnerId, ReferenceType, ReferenceId, Notes,
                 PerformedBy, PerformedAt
          FROM inventory.StockMovements
          WHERE StockId = @stockId
          ORDER BY PerformedAt DESC",
        new { stockId, limit },
        cancellationToken: ct))
    .ContinueWith(t => (IReadOnlyList<StockMovement>)t.Result.AsList());
```

(Use the same await pattern as `WarehouseRepository.GetActiveAsync` —
`async Task<IReadOnlyList<...>>` with `(await ...).AsList()`. The
`.ContinueWith` above is just for brevity in the spec.)

For `GetByProductAsync`:

```csharp
@"SELECT TOP (@limit) m.Id, m.StockId, m.MovementType,
         m.FromLocationId, m.ToLocationId,
         m.QuantityDelta, m.UomId, m.OwnerId,
         m.ReferenceType, m.ReferenceId, m.Notes,
         m.PerformedBy, m.PerformedAt
  FROM inventory.StockMovements m
  JOIN inventory.Stock s ON s.Id = m.StockId
  WHERE s.ProductId = @productId
    AND (@since IS NULL OR m.PerformedAt >= @since)
  ORDER BY m.PerformedAt DESC"
```

**File**: `src/WMS.DAL/Repositories/Inventory/StockMovementRepositoryFactory.cs`

```csharp
public sealed class StockMovementRepositoryFactory : IStockMovementRepositoryFactory
{
    private readonly ITenantConnectionFactory _connectionFactory;

    public StockMovementRepositoryFactory(ITenantConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public IStockMovementRepository For(Guid tenantId) =>
        new StockMovementRepository(_connectionFactory.CreateConnection(tenantId));
}
```

**Register** in `Program.cs`:

```csharp
builder.Services.AddScoped<IStockMovementRepositoryFactory, StockMovementRepositoryFactory>();
```

The write side is **not** on `IStockMovementRepository` — the INSERT
lives inside `StockRepository`'s atomic SQL batches (T4) so the "same
transaction" invariant is enforceable.

**Commit**: `feat(dal): add IStockMovementRepository (read-side only)`

---

### T4 — `IStockRepository` signatures + transactional INSERT

**Controlled refactor — no overloads.** Both call sites (Receiving +
Putaway) are updated in T5 in the same change.

**File**: `src/WMS.DAL/Repositories/Inventory/IStockRepository.cs`

Drop the standalone `Guid? userId` parameter — it folds into
`StockMovementContext.PerformedBy`.

```csharp
// OLD:
// Task<Stock> UpsertOnHandAsync(StockKey key, decimal delta, Guid? userId, CancellationToken ct = default);
// Task<(Stock,Stock)> TransferStockAsync(Guid fromStockId, Guid toLocationId, decimal qty, Guid? userId, CancellationToken ct = default);

// NEW:
Task<Stock> UpsertOnHandAsync(
    StockKey key,
    decimal delta,
    StockMovementContext movementCtx,
    CancellationToken ct = default);

Task<(Stock From, Stock To)> TransferStockAsync(
    Guid fromStockId,
    Guid toLocationId,
    decimal quantity,
    StockMovementContext movementCtx,
    CancellationToken ct = default);
```

**File**: `src/WMS.DAL/Repositories/Inventory/StockRepository.cs`

#### `UpsertOnHandAsync`

The existing `MERGE WITH (HOLDLOCK)` already returns the affected
Stock row via `OUTPUT inserted.*`. Capture its `Id` into a table
variable, then INSERT one movement before returning.

Sketch (the actual SQL keeps its current shape — only adds the post-
MERGE INSERT):

```sql
DECLARE @merged TABLE (Id UNIQUEIDENTIFIER, ...);

MERGE inventory.Stock WITH (HOLDLOCK) AS target
USING ( ... ) AS src
ON ...
WHEN MATCHED THEN UPDATE SET ...
WHEN NOT MATCHED THEN INSERT ...
OUTPUT inserted.Id, inserted.LocationId, inserted.ProductId, ...
INTO @merged;

INSERT INTO inventory.StockMovements
    (StockId, MovementType, FromLocationId, ToLocationId,
     QuantityDelta, UomId, OwnerId,
     ReferenceType, ReferenceId, Notes, PerformedBy)
SELECT m.Id, @MovementType,
       NULL, m.LocationId,        -- From=NULL for Receive (Adjust will pass both NULL)
       @Delta, m.UomId, m.OwnerId,
       @RefType, @RefId, @Notes, @PerformedBy
FROM @merged m;

SELECT * FROM @merged;            -- existing return shape
```

Note: the From/To pattern for `UpsertOnHandAsync` is currently
"receive into a location" (From=NULL, To=key.LocationId). When
`MovementType=Adjust` lands later, the SQL stays the same because
adjustments will go through a different repo method (or pass null
LocationIds via context expansion — out of scope here).

#### `TransferStockAsync`

The existing batch already runs source UPDATE + destination MERGE
inside `BEGIN TRAN; … COMMIT;`. Add **two** INSERTs into
`StockMovements` — one after the source UPDATE (signed -qty), one
after the destination MERGE (signed +qty). Both share the same
`ReferenceType` + `ReferenceId`.

```sql
-- after source UPDATE (which already locked the row via UPDLOCK + HOLDLOCK):
INSERT INTO inventory.StockMovements
    (StockId, MovementType, FromLocationId, ToLocationId,
     QuantityDelta, UomId, OwnerId,
     ReferenceType, ReferenceId, Notes, PerformedBy)
VALUES
    (@FromStockId, @MovementType, @currentLoc, @ToLocationId,
     -@Quantity, @uomId, @ownerId,
     @RefType, @RefId, @Notes, @PerformedBy);

-- after destination MERGE OUTPUT INTO @merged:
INSERT INTO inventory.StockMovements
    (StockId, MovementType, FromLocationId, ToLocationId,
     QuantityDelta, UomId, OwnerId,
     ReferenceType, ReferenceId, Notes, PerformedBy)
SELECT m.Id, @MovementType, @currentLoc, @ToLocationId,
       @Quantity, m.UomId, m.OwnerId,
       @RefType, @RefId, @Notes, @PerformedBy
FROM @merged m;
```

**Critical**: both INSERTs are **inside** the existing
`BEGIN TRAN; … COMMIT TRAN;` boundary. `SET XACT_ABORT ON;` (already
present) ensures any subsequent failure rolls everything back.

**Commit**: `feat(dal): write StockMovements rows inside Stock mutation transactions`

---

### T5 — Update callers

**File**: `src/WMS.BLL/Services/Inbound/ReceivingService.cs`

`ReceiveLineAsync` already creates the `ReceivingLine` row before the
Stock upsert — the new `Id` is available locally. Pass it as
`ReferenceId`:

```csharp
var movementCtx = new StockMovementContext(
    MovementType: StockMovementType.Receive,
    PerformedBy:  currentUserId,
    ReferenceType: "ReceivingLine",
    ReferenceId:   line.Id);

await stockRepo.UpsertOnHandAsync(key, request.Quantity, movementCtx, ct);
```

(Confirm during T5 whether the line is inserted before or after the
stock upsert in the current orchestration; if after, swap the order
or add a pre-generated Guid. The line's Id must exist before the
stock upsert.)

**File**: `src/WMS.BLL/Services/Inbound/PutawayService.cs`

```csharp
var movementCtx = new StockMovementContext(
    MovementType: StockMovementType.Putaway,
    PerformedBy:  currentUserId,
    ReferenceType: "Putaway",
    ReferenceId:   null);              // TD-004 — no header table yet

var (afterSource, destination) = await repo.TransferStockAsync(
    source.Id, request.ToLocationId, request.Quantity, movementCtx, ct);
```

These are the only two call sites system-wide — audit confirmed.

**Commit**: `feat(services): pass StockMovementContext from Receiving + Putaway`

---

### T6 — Test fixture updates

**Files**:
- `tests/WMS.UnitTests/Services/Inbound/ReceivingServiceTests.cs`
- `tests/WMS.UnitTests/Services/Inbound/PutawayServiceTests.cs`

Existing Moq setups using `It.IsAny<Guid?>()` for the userId parameter
must change to `It.IsAny<StockMovementContext>()`. Existing 5 putaway
tests + N receiving tests should pass after parameter updates.

Add **2 new unit tests** per service that capture the context with
`Callback` and assert reference binding:

- `ReceiveLineAsync_PassesReceivingLineIdAsReferenceId`
- `ReceiveLineAsync_PassesReceiveMovementType`
- `PutawayStockAsync_PassesPutawayMovementType`
- `PutawayStockAsync_PassesNullReferenceId`  (TD-004 invariant)

**Commit**: `test(services): update mocks for StockMovementContext + assert reference binding`

---

### T7 — Integration tests (write path)

**File**: `tests/WMS.IntegrationTests/Inventory/StockMovementLogTests.cs`

> **Heads-up on infrastructure**: existing integration tests
> (`WMS.IntegrationTests/Multitenancy/...`, `Filters/...`,
> `Storage/LocalFileStorageServiceTests.cs`) are unit-style — they
> stub `IDbConnection` / repositories and avoid SQL Server. The Stock
> mutation logic that we need to verify lives in raw SQL inside
> `StockRepository`, so a real DB is the only honest test.
>
> **Approach**: probe whether a localdb / docker SQL fixture exists
> when implementing T7. If not, ship `StockMovementLogTests` as
> Moq-level tests of `StockRepository` SQL composition (verify the
> command text contains the expected INSERT clauses) **and** rely on
> the manual smoke test at the end of T9 for end-to-end coverage.
> Document the gap in `docs/TECH_DEBT.md` as a follow-up.

If a real DB fixture is available, author at minimum:

1. **Receive writes one movement** — call through `ReceivingService` or
   `StockRepository.UpsertOnHandAsync` against a seeded DB. Assert
   exactly 1 row in `inventory.StockMovements`,
   `MovementType='Receive'`, `FromLocationId IS NULL`,
   `ToLocationId = key.LocationId`, `QuantityDelta = +qty`,
   `ReferenceType = 'ReceivingLine'`, `ReferenceId = <expected Guid>`.

2. **Putaway writes two movements** — assert 2 rows, both with the
   same `ReferenceType='Putaway'` and `ReferenceId IS NULL`, opposite
   signs (`-qty` source / `+qty` dest), source's `StockId =
   fromStockId`, dest's `StockId = <merged dest Id>`.

3. **Reconciliation** — `SUM(QuantityDelta)` per `StockId` matches
   `Stock.QuantityOnHand` change for the row.

4. **Failed mutation = no movement row** — trigger `THROW 50002`
   (insufficient quantity) in `TransferStockAsync`, assert zero rows
   inserted into `StockMovements`.

5. **GetByProductAsync** — insert 3 movements across 2 Stock rows
   sharing one ProductId, assert all 3 returned in `PerformedAt DESC`.

**Commit**: `test(integration): assert StockMovements written transactionally`

---

### ~~T8 — Wire to Activity tab~~ (deferred to Phase 6B)

Skipped this phase per project decision. Lands in Phase 6B alongside
real Products/Customers data so the Activity tab stops being a
frankenstein of mock + real.

---

### T9 — TECH_DEBT + CLAUDE.md + merge

**File**: `docs/TECH_DEBT.md`

Add to the Open table:

```markdown
| TD-004 | Putaway StockMovements rows carry ReferenceId = NULL | Medium | 2026-05-08 | Closes when ADR-004 lands and introduces a putaway header table; backfill via UPDATE matching ReferenceType='Putaway' AND PerformedAt range | Per ADR-014 |
| TD-005 | ADR-004 missing — referenced by docs/01 + IPutawayService comments but no ADR file in docs/decisions/ | Low | 2026-05-08 | Draft ADR-004 alongside the suggestion-engine implementation (templates + scoring) | — |
| TD-006 | Integration test for StockRepository write path needs a real SQL Server fixture | Low | 2026-05-08 | Stand up a localdb / testcontainers SQL fixture when adding ADR-013 / ADR-012 — they'll need it too | Currently relying on manual smoke + Moq-level SQL composition tests |
```

**File**: `CLAUDE.md`

- Add ADR-014 to the ADR list under "Important Decisions"
- Add Phase 6A section under the existing Day 5/6 entries
- Bump **Last updated** + **Version** at the bottom

```markdown
### Day 6 — Phase 6A (Stock Movement Log)

**Branch**: `feat/movement-log-impl` → merged to `main` · **Tag**: `v0.6.0-movement-log` · **ADR**: ADR-014

Components:
- Migration `20260508002` — `inventory.StockMovements`. StockId-FK'd, signed
  `QuantityDelta`, no `TenantId` (DB-per-tenant), CHECK on closed enum,
  3 indexes including INCLUDE-covered per-Stock seek.
- Domain: `StockMovement` entity, `StockMovementType` enum, `StockMovementContext` record.
- `IStockMovementRepository` (read-side: by Stock / by Reference / by Product).
- `IStockRepository` signatures: `UpsertOnHandAsync` + `TransferStockAsync` now accept
  `StockMovementContext` instead of `Guid? userId` (controlled refactor — both call sites updated together).
- `ReceivingService` writes `MovementType=Receive` with `ReferenceType='ReceivingLine'`,
  `ReferenceId=<line guid>`.
- `PutawayService` writes `MovementType=Putaway` (TWO rows: source -qty, dest +qty)
  with `ReferenceType='Putaway'`, `ReferenceId=null` (TD-004).

Forward-only — pre-existing Stock rows have no synthesized history.

Foundation for: ADR-013 Adjustment, ADR-012 Transfer, future Pick/Pack, Cycle Count,
Activity tab (Phase 6B).
```

```markdown
- ADR-014: Stock Movement Log — materialised log table, transactional with Stock mutations, signed delta, StockId-FK'd, forward-only
```

**Commit**: `docs: add ADR-014 + update TECH_DEBT.md + CLAUDE.md for Phase 6A`

---

## Acceptance Criteria

```
DB:
✅ inventory.StockMovements table exists in WMS_Tenant_Template
✅ All FKs + CHECK + 3 indexes active
✅ Migration is reversible (Down drops the table cleanly)

Services:
✅ ReceivingService writes 1 movement, MovementType='Receive', ReferenceType='ReceivingLine'
✅ PutawayService writes 2 movements (source -qty, dest +qty), MovementType='Putaway',
   ReferenceType='Putaway', ReferenceId=NULL on both
✅ Movement INSERT(s) and Stock UPDATE share one transaction
✅ Failed mutation (THROW 50001/50002/50003) → no movement rows (rollback)

Tests:
✅ Existing unit tests pass after StockMovementContext signature change
✅ 2 new unit tests per service (assert reference binding + MovementType)
✅ Integration tests for write path (Moq-level if no real-DB fixture; document gap as TD-006)
✅ Total test count: 57 prior + ~10 new = ~67 passing

Build:
✅ 0 errors, 0 warnings
✅ No drop in test count

Docs:
✅ ADR-014 in docs/decisions/
✅ TECH_DEBT.md notes ADR-004 (TD-004, TD-005) + integration-test gap (TD-006)
✅ CLAUDE.md updated with Phase 6A entry + ADR list bump
```

---

## Out of Scope (Phase 6B and beyond)

```
🔄 Activity tab wiring (Phase 6B — alongside real Master Data)
🔄 Movement history UI page (separate phase)
🔄 ADR-004 (Hybrid Putaway: Template + Scoring)
🔄 ADR-013 Adjustment implementation
🔄 ADR-012 Transfer implementation
🔄 Reconciliation job (Stock vs SUM(StockMovements))
🔄 Cycle Count
🔄 Outbound flow (Pick/Pack/Ship)
🔄 MovementType: Pick/Adjust/Transfer/Return/Cycle values exist in enum but unused
🔄 Real SQL Server integration-test fixture (TD-006)
```

---

## Smoke Test (manual, browser)

Run the dev app, login, then:

1. **Receive 1 unit** of any seeded product (`/Receive`) →
   in SSMS or `sqlcmd`:
   ```sql
   SELECT TOP 5 Id, MovementType, QuantityDelta,
                FromLocationId, ToLocationId, ReferenceType, ReferenceId
   FROM inventory.StockMovements
   ORDER BY PerformedAt DESC;
   ```
   Expect: 1 new row, `MovementType='Receive'`, `QuantityDelta=+1`,
   `FromLocationId IS NULL`, `ToLocationId = <receiving location>`,
   `ReferenceType='ReceivingLine'`, `ReferenceId IS NOT NULL`.

2. **Putaway 1 unit** (`/Putaway`) — same SQL →
   Expect: 2 new rows. Both `MovementType='Putaway'`, both
   `ReferenceType='Putaway'`, both `ReferenceId IS NULL`. One row
   `QuantityDelta=-1` (source), the other `QuantityDelta=+1` (dest).
   Their `FromLocationId`/`ToLocationId` match the operation.

3. **Total movement count** after smoke = previous count + 3.

---

## Merge

```bash
git checkout main
git merge feat/movement-log-impl --no-ff -m "Merge feat/movement-log-impl into main"
git tag -a v0.6.0-movement-log -m "Phase 6A: Stock Movement Log per ADR-014"
git branch -d feat/movement-log-impl
```

Add `PHASE6A_MOVEMENT_LOG_BRIEF.md` to `.gitignore`.

---

## Final report shape

- Final commit hash on `main`
- Number of commits in branch
- Tag confirmation
- Movement count after smoke test (`SELECT COUNT(*) FROM inventory.StockMovements`)
- Any blockers or schema discoveries

---

**End of brief.**

Next phase: **6B — Real Master Data** (Products + Customers tables, repositories, wire Activity tab to per-product movements via `IStockMovementRepository.GetByProductAsync`).
