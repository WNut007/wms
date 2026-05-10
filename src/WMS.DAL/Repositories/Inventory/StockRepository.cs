using System.Data;
using Dapper;
using WMS.Common.Inventory;
using WMS.Domain.Entities.Inventory;

namespace WMS.DAL.Repositories.Inventory;

// Dapper-backed reader for inventory.Stock. Bound to a single tenant
// DB connection; the factory creates one per tenantId via
// ITenantConnectionFactory.
internal sealed class StockRepository : IStockRepository
{
    // Column list shared across all SELECTs so the dapper materializer
    // hits properties by name without surprises.
    private const string SelectColumns = @"
        SELECT Id, LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
               QuantityOnHand, QuantityAllocated, LastMovementAt,
               CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
        FROM inventory.Stock";

    private readonly IDbConnection _connection;

    public StockRepository(IDbConnection connection) => _connection = connection;

    public Task<Stock?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Stock?>(new CommandDefinition(
            SelectColumns + " WHERE Id = @id",
            new { id },
            cancellationToken: ct));

    public async Task<IReadOnlyList<Stock>> GetByLocationAsync(
        Guid locationId, CancellationToken ct = default) =>
        (await _connection.QueryAsync<Stock>(new CommandDefinition(
            SelectColumns + " WHERE LocationId = @locationId",
            new { locationId },
            cancellationToken: ct))).AsList();

    public async Task<IReadOnlyList<Stock>> GetByProductAsync(
        Guid productId, CancellationToken ct = default) =>
        (await _connection.QueryAsync<Stock>(new CommandDefinition(
            SelectColumns + " WHERE ProductId = @productId",
            new { productId },
            cancellationToken: ct))).AsList();

    // NULL-safe match on LotId + PalletId — passing NULL for either
    // matches a row that is itself NULL there, instead of the SQL
    // default "NULL = NULL → unknown → not matched".
    public Task<Stock?> GetByKeyAsync(StockKey key, CancellationToken ct = default) =>
        _connection.QuerySingleOrDefaultAsync<Stock?>(new CommandDefinition(
            SelectColumns + @"
            WHERE LocationId = @LocationId
              AND ProductId  = @ProductId
              AND ((@LotId    IS NULL AND LotId    IS NULL) OR LotId    = @LotId)
              AND ((@PalletId IS NULL AND PalletId IS NULL) OR PalletId = @PalletId)
              AND OwnerId    = @OwnerId
              AND UomId      = @UomId",
            new
            {
                key.LocationId,
                key.ProductId,
                key.LotId,
                key.PalletId,
                key.OwnerId,
                key.UomId,
            },
            cancellationToken: ct));

    // MERGE WITH (HOLDLOCK) is the single-statement atomic upsert SQL
    // Server endorses for this pattern: HOLDLOCK serializes concurrent
    // executions on the same key range, eliminating the classic
    // SELECT-then-INSERT race. NULL-safe key matching mirrors
    // GetByKeyAsync above.
    //
    // Wrapped in BEGIN TRAN so the MERGE and the matching
    // StockMovements INSERT (per ADR-014) commit together — if the
    // movement insert fails (e.g. CHECK constraint typo), the Stock
    // change rolls back too. SET XACT_ABORT ON ensures any
    // mid-batch failure aborts the whole transaction.
    //
    // OUTPUT INTO @merged captures the upserted row's Id (and the
    // columns the caller expects back) so the StockMovements INSERT
    // can reference it by StockId without a second SELECT.
    public Task<Stock> UpsertOnHandAsync(
        StockKey key,
        decimal quantityDelta,
        StockMovementContext movementCtx,
        CancellationToken ct = default) =>
        _connection.QuerySingleAsync<Stock>(new CommandDefinition(
            @"SET XACT_ABORT ON;
              BEGIN TRAN;

              DECLARE @merged TABLE (
                  Id                UNIQUEIDENTIFIER,
                  LocationId        UNIQUEIDENTIFIER,
                  ProductId         UNIQUEIDENTIFIER,
                  LotId             UNIQUEIDENTIFIER NULL,
                  PalletId          UNIQUEIDENTIFIER NULL,
                  OwnerId           UNIQUEIDENTIFIER,
                  UomId             UNIQUEIDENTIFIER,
                  QuantityOnHand    DECIMAL(18,4),
                  QuantityAllocated DECIMAL(18,4),
                  LastMovementAt    DATETIME2 NULL,
                  CreatedAt         DATETIME2,
                  UpdatedAt         DATETIME2 NULL,
                  CreatedBy         UNIQUEIDENTIFIER NULL,
                  UpdatedBy         UNIQUEIDENTIFIER NULL,
                  Version           INT
              );

              MERGE inventory.Stock WITH (HOLDLOCK) AS target
              USING (
                  SELECT @LocationId AS LocationId,
                         @ProductId  AS ProductId,
                         @LotId      AS LotId,
                         @PalletId   AS PalletId,
                         @OwnerId    AS OwnerId,
                         @UomId      AS UomId
              ) AS src
              ON  target.LocationId = src.LocationId
              AND target.ProductId  = src.ProductId
              AND ((src.LotId    IS NULL AND target.LotId    IS NULL) OR target.LotId    = src.LotId)
              AND ((src.PalletId IS NULL AND target.PalletId IS NULL) OR target.PalletId = src.PalletId)
              AND target.OwnerId    = src.OwnerId
              AND target.UomId      = src.UomId
              WHEN MATCHED THEN
                  UPDATE SET
                      QuantityOnHand = target.QuantityOnHand + @Delta,
                      LastMovementAt = SYSUTCDATETIME(),
                      UpdatedAt      = SYSUTCDATETIME(),
                      UpdatedBy      = @PerformedBy,
                      Version        = target.Version + 1
              WHEN NOT MATCHED THEN
                  INSERT (LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
                          QuantityOnHand, QuantityAllocated, LastMovementAt,
                          CreatedAt, CreatedBy)
                  VALUES (src.LocationId, src.ProductId, src.LotId, src.PalletId,
                          src.OwnerId, src.UomId,
                          @Delta, 0, SYSUTCDATETIME(),
                          SYSUTCDATETIME(), @PerformedBy)
              OUTPUT inserted.Id, inserted.LocationId, inserted.ProductId,
                     inserted.LotId, inserted.PalletId,
                     inserted.OwnerId, inserted.UomId,
                     inserted.QuantityOnHand, inserted.QuantityAllocated,
                     inserted.LastMovementAt,
                     inserted.CreatedAt, inserted.UpdatedAt,
                     inserted.CreatedBy, inserted.UpdatedBy,
                     inserted.Version
              INTO @merged;

              -- Movement row pairs with the Stock UPSERT, same TX. For
              -- UpsertOnHandAsync (Receive / future Adjust+), From is
              -- NULL and To is the merged row's location.
              INSERT INTO inventory.StockMovements
                  (StockId, MovementType, FromLocationId, ToLocationId,
                   QuantityDelta, UomId, OwnerId,
                   ReferenceType, ReferenceId, Notes, PerformedBy)
              SELECT m.Id, @MovementType, NULL, m.LocationId,
                     @Delta, m.UomId, m.OwnerId,
                     @ReferenceType, @ReferenceId, @Notes, @PerformedBy
              FROM @merged m;

              COMMIT TRAN;

              SELECT * FROM @merged;",
            new
            {
                key.LocationId,
                key.ProductId,
                key.LotId,
                key.PalletId,
                key.OwnerId,
                key.UomId,
                Delta         = quantityDelta,
                MovementType  = movementCtx.MovementType.ToString(),
                ReferenceType = movementCtx.ReferenceType,
                ReferenceId   = movementCtx.ReferenceId,
                Notes         = movementCtx.Notes,
                PerformedBy   = movementCtx.PerformedBy,
            },
            cancellationToken: ct));

    // Putaway / move primitive. The whole batch runs inside one
    // BEGIN...COMMIT TRAN with XACT_ABORT ON so a THROW (or any other
    // failure) rolls everything back automatically.
    //
    // The leading SELECT takes UPDLOCK + HOLDLOCK on the source row so
    // concurrent transfers from the same row serialise — the second
    // caller's SELECT waits for the first's COMMIT before reading
    // OnHand. The WHERE-checked UPDATE then provides a safety net even
    // for callers that bypass this method.
    public async Task<(Stock Source, Stock Destination)> TransferStockAsync(
        Guid fromStockId,
        Guid toLocationId,
        decimal quantity,
        StockMovementContext movementCtx,
        CancellationToken ct = default)
    {
        if (_connection.State != ConnectionState.Open)
            (_connection as Microsoft.Data.SqlClient.SqlConnection)?.Open();

        // Two StockMovements rows pair with the source UPDATE +
        // destination MERGE — same transaction (per ADR-014). Both
        // share movementCtx's ReferenceType/ReferenceId so the pair
        // reconciles in reports.
        //
        // The source movement INSERT happens after the UPDATE (so
        // failed validation aborts before any movement row is
        // written). The destination movement INSERT consumes the
        // merged row's Id via OUTPUT INTO @destMerged.
        //
        // SET XACT_ABORT ON guarantees any THROW (50001/50002/50003)
        // or constraint violation rolls back BOTH Stock changes AND
        // BOTH movement rows.
        const string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @prodId    UNIQUEIDENTIFIER,
        @lotId     UNIQUEIDENTIFIER,
        @palletId  UNIQUEIDENTIFIER,
        @ownerId   UNIQUEIDENTIFIER,
        @uomId     UNIQUEIDENTIFIER,
        @currentQty DECIMAL(18, 4),
        @currentLoc UNIQUEIDENTIFIER;

SELECT @prodId   = ProductId,
       @lotId    = LotId,
       @palletId = PalletId,
       @ownerId  = OwnerId,
       @uomId    = UomId,
       @currentQty = QuantityOnHand,
       @currentLoc = LocationId
FROM inventory.Stock WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @FromStockId;

IF @prodId IS NULL
    THROW 50001, 'Source stock row not found.', 1;

IF @currentLoc = @ToLocationId
    THROW 50003, 'Destination location must differ from source.', 1;

IF @currentQty < @Quantity
    THROW 50002, 'Insufficient quantity at source.', 1;

UPDATE inventory.Stock
SET QuantityOnHand = QuantityOnHand - @Quantity,
    LastMovementAt = SYSUTCDATETIME(),
    UpdatedAt      = SYSUTCDATETIME(),
    UpdatedBy      = @PerformedBy,
    Version        = Version + 1
WHERE Id = @FromStockId;

-- Source-side movement: signed -Quantity against @FromStockId.
INSERT INTO inventory.StockMovements
    (StockId, MovementType, FromLocationId, ToLocationId,
     QuantityDelta, UomId, OwnerId,
     ReferenceType, ReferenceId, Notes, PerformedBy)
VALUES
    (@FromStockId, @MovementType, @currentLoc, @ToLocationId,
     -@Quantity, @uomId, @ownerId,
     @ReferenceType, @ReferenceId, @Notes, @PerformedBy);

DECLARE @destMerged TABLE (
    Id      UNIQUEIDENTIFIER,
    UomId   UNIQUEIDENTIFIER,
    OwnerId UNIQUEIDENTIFIER
);

MERGE inventory.Stock WITH (HOLDLOCK) AS target
USING (SELECT @ToLocationId AS LocationId,
              @prodId       AS ProductId,
              @lotId        AS LotId,
              @palletId     AS PalletId,
              @ownerId      AS OwnerId,
              @uomId        AS UomId) AS src
ON  target.LocationId = src.LocationId
AND target.ProductId  = src.ProductId
AND ((src.LotId    IS NULL AND target.LotId    IS NULL) OR target.LotId    = src.LotId)
AND ((src.PalletId IS NULL AND target.PalletId IS NULL) OR target.PalletId = src.PalletId)
AND target.OwnerId    = src.OwnerId
AND target.UomId      = src.UomId
WHEN MATCHED THEN
    UPDATE SET
        QuantityOnHand = target.QuantityOnHand + @Quantity,
        LastMovementAt = SYSUTCDATETIME(),
        UpdatedAt      = SYSUTCDATETIME(),
        UpdatedBy      = @PerformedBy,
        Version        = target.Version + 1
WHEN NOT MATCHED THEN
    INSERT (LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
            QuantityOnHand, QuantityAllocated, LastMovementAt,
            CreatedAt, CreatedBy)
    VALUES (src.LocationId, src.ProductId, src.LotId, src.PalletId,
            src.OwnerId, src.UomId,
            @Quantity, 0, SYSUTCDATETIME(),
            SYSUTCDATETIME(), @PerformedBy)
OUTPUT inserted.Id, inserted.UomId, inserted.OwnerId
INTO @destMerged;

-- Destination-side movement: signed +Quantity against the merged
-- (or just-created) destination StockId.
INSERT INTO inventory.StockMovements
    (StockId, MovementType, FromLocationId, ToLocationId,
     QuantityDelta, UomId, OwnerId,
     ReferenceType, ReferenceId, Notes, PerformedBy)
SELECT m.Id, @MovementType, @currentLoc, @ToLocationId,
       @Quantity, m.UomId, m.OwnerId,
       @ReferenceType, @ReferenceId, @Notes, @PerformedBy
FROM @destMerged m;

COMMIT TRAN;

-- Source after the transfer.
SELECT Id, LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
       QuantityOnHand, QuantityAllocated, LastMovementAt,
       CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
FROM inventory.Stock
WHERE Id = @FromStockId;

-- Destination after the transfer (matched on the captured 6-tuple).
SELECT Id, LocationId, ProductId, LotId, PalletId, OwnerId, UomId,
       QuantityOnHand, QuantityAllocated, LastMovementAt,
       CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, Version
FROM inventory.Stock
WHERE LocationId = @ToLocationId
  AND ProductId  = @prodId
  AND ((LotId    IS NULL AND @lotId    IS NULL) OR LotId    = @lotId)
  AND ((PalletId IS NULL AND @palletId IS NULL) OR PalletId = @palletId)
  AND OwnerId    = @ownerId
  AND UomId      = @uomId;
";

        using var multi = await _connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new
            {
                FromStockId   = fromStockId,
                ToLocationId  = toLocationId,
                Quantity      = quantity,
                MovementType  = movementCtx.MovementType.ToString(),
                ReferenceType = movementCtx.ReferenceType,
                ReferenceId   = movementCtx.ReferenceId,
                Notes         = movementCtx.Notes,
                PerformedBy   = movementCtx.PerformedBy,
            },
            cancellationToken: ct));

        var source = await multi.ReadSingleAsync<Stock>();
        var destination = await multi.ReadSingleAsync<Stock>();
        return (source, destination);
    }

    public async Task<IReadOnlyList<Stock>> GetPositiveOnHandByWarehouseAsync(
        Guid warehouseId, Guid? locationFilter, CancellationToken ct = default)
    {
        // Stock has LocationId, not WarehouseId. Resolve warehouse via
        // Locations join. Filtering by IX_Stock_Location is fine because
        // the warehouse-narrow predicate is on Locations (which has
        // WarehouseId indexed via FK).
        const string sql = @"
SELECT s.Id, s.LocationId, s.ProductId, s.LotId, s.PalletId, s.OwnerId, s.UomId,
       s.QuantityOnHand, s.QuantityAllocated, s.LastMovementAt,
       s.CreatedAt, s.UpdatedAt, s.CreatedBy, s.UpdatedBy, s.Version
FROM inventory.Stock s
JOIN master.Locations loc ON loc.Id = s.LocationId
WHERE loc.WarehouseId = @warehouseId
  AND (@locationFilter IS NULL OR s.LocationId = @locationFilter)
  AND s.QuantityOnHand > 0
ORDER BY loc.Code, s.ProductId;";

        var rows = await _connection.QueryAsync<Stock>(new CommandDefinition(
            sql, new { warehouseId, locationFilter }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Stock>> GetAllocationCandidatesAsync(
        Guid warehouseId,
        Guid productId,
        Guid ownerId,
        Guid uomId,
        CancellationToken ct = default)
    {
        // Available = OnHand - Allocated. CK_Stock_Allocated_NotOver-
        // OnHand keeps that non-negative; the > 0 predicate filters
        // fully-allocated rows. Sort CreatedAt ASC = FIFO-friendly
        // default; strategies may re-order in memory before picking.
        const string sql = @"
SELECT s.Id, s.LocationId, s.ProductId, s.LotId, s.PalletId, s.OwnerId, s.UomId,
       s.QuantityOnHand, s.QuantityAllocated, s.LastMovementAt,
       s.CreatedAt, s.UpdatedAt, s.CreatedBy, s.UpdatedBy, s.Version
FROM inventory.Stock s
JOIN master.Locations loc ON loc.Id = s.LocationId
WHERE loc.WarehouseId = @warehouseId
  AND s.ProductId     = @productId
  AND s.OwnerId       = @ownerId
  AND s.UomId         = @uomId
  AND (s.QuantityOnHand - s.QuantityAllocated) > 0
ORDER BY s.CreatedAt;";

        var rows = await _connection.QueryAsync<Stock>(new CommandDefinition(
            sql, new { warehouseId, productId, ownerId, uomId },
            cancellationToken: ct));
        return rows.AsList();
    }

    public Task AdjustQuantityAllocatedAsync(
        Guid stockId,
        decimal delta,
        Guid? userId,
        CancellationToken ct = default) =>
        _connection.ExecuteAsync(new CommandDefinition(
            @"UPDATE inventory.Stock
              SET QuantityAllocated = QuantityAllocated + @Delta,
                  UpdatedAt         = SYSUTCDATETIME(),
                  UpdatedBy         = @UserId,
                  Version           = Version + 1
              WHERE Id = @StockId;",
            new { StockId = stockId, Delta = delta, UserId = userId },
            cancellationToken: ct));

    public async Task<IReadOnlyList<PutawayQueueRow>> GetPutawayQueueAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        // Stock at locations whose Zone.Type IN ('Receiving','Staging')
        // with positive OnHand. JOINs cover natural-key code rendering
        // for the per-card view. LEFT JOINs on Lots + Pallets so non-
        // lot/pallet products still render. CreatedAt ASC = FIFO oldest
        // first (operator clears the backlog).
        const string sql = @"
SELECT
    s.Id              AS StockId,
    s.QuantityOnHand,
    s.LocationId,
    loc.Code          AS LocationCode,
    z.Type            AS ZoneType,
    s.ProductId,
    p.Code            AS ProductCode,
    p.Name            AS ProductName,
    p.TrackingMethod,
    s.OwnerId,
    o.Code            AS OwnerCode,
    s.LotId,
    lot.LotNumber     AS LotNumber,
    s.PalletId,
    pal.PalletNumber  AS PalletNumber,
    s.UomId,
    u.Code            AS UomCode,
    s.LastMovementAt,
    s.CreatedAt
FROM inventory.Stock s
JOIN master.Locations loc ON loc.Id = s.LocationId
JOIN master.Zones     z   ON z.Id   = loc.ZoneId
JOIN master.Products  p   ON p.Id   = s.ProductId
JOIN master.Owners    o   ON o.Id   = s.OwnerId
JOIN master.UnitsOfMeasure u ON u.Id = s.UomId
LEFT JOIN inventory.Lots    lot ON lot.Id = s.LotId
LEFT JOIN inventory.Pallets pal ON pal.Id = s.PalletId
WHERE loc.WarehouseId = @warehouseId
  AND z.Type IN ('Receiving', 'Staging')
  AND s.QuantityOnHand > 0
ORDER BY s.CreatedAt;";

        var rows = await _connection.QueryAsync<PutawayQueueRow>(new CommandDefinition(
            sql, new { warehouseId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<SuggestedLocationResult?> GetSuggestedPutawayLocationAsync(
        Guid warehouseId, Guid productId, CancellationToken ct = default)
    {
        // Storage-zone candidates only (Active/IsActive). LEFT JOIN to
        // an aggregate counting same-product Stock rows already at the
        // location (the cluster-picks heuristic). Order: same-product
        // count DESC > BinRank ASC > IsPickface ASC. TOP 1 = winner.
        // Capacity-aware tie-break is a TD (no per-location current
        // load vs max comparison without product-volume data).
        const string sql = @"
WITH same_product AS (
    SELECT LocationId, COUNT(*) AS Cnt
    FROM inventory.Stock
    WHERE ProductId = @productId AND QuantityOnHand > 0
    GROUP BY LocationId
)
SELECT TOP (1)
    loc.Id        AS LocationId,
    loc.Code      AS LocationCode,
    z.Code        AS ZoneCode,
    z.Name        AS ZoneName,
    loc.BinRank,
    loc.IsPickface,
    ISNULL(sp.Cnt, 0) AS SameProductRowCount
FROM master.Locations loc
JOIN master.Zones z ON z.Id = loc.ZoneId
LEFT JOIN same_product sp ON sp.LocationId = loc.Id
WHERE loc.WarehouseId = @warehouseId
  AND z.Type     = 'Storage'
  AND loc.IsActive = 1
  AND loc.Status   = 'Active'
ORDER BY ISNULL(sp.Cnt, 0) DESC, loc.BinRank ASC,
         CASE WHEN loc.IsPickface = 1 THEN 1 ELSE 0 END ASC,
         loc.Code ASC;";

        var hit = await _connection.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
            sql, new { warehouseId, productId }, cancellationToken: ct));
        if (hit is null) return null;

        var reasons = new List<string>();
        int sameCount = (int)hit.SameProductRowCount;
        int? binRank = (int?)hit.BinRank;
        bool isPickface = (bool)hit.IsPickface;

        if (sameCount > 0)
            reasons.Add($"Same product nearby ({sameCount} stock row{(sameCount == 1 ? "" : "s")})");
        if (binRank is { } rank && rank < 50)
            reasons.Add($"Low bin rank ({rank})");
        if (isPickface)
            reasons.Add("Pick face (last-resort target)");
        if (reasons.Count == 0)
            reasons.Add("Available storage location");

        return new SuggestedLocationResult(
            LocationId: (Guid)hit.LocationId,
            LocationCode: (string)hit.LocationCode,
            ZoneCode: (string)hit.ZoneCode,
            ZoneName: (string)hit.ZoneName,
            BinRank: binRank,
            IsPickface: isPickface,
            SameProductRowCount: sameCount,
            Reasons: reasons);
    }
}
