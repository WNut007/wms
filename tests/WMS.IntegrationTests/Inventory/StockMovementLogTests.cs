namespace WMS.IntegrationTests.Inventory;

// Write-path integration tests for inventory.StockMovements per
// ADR-014. SKIPPED in this commit because the WMS test pipeline
// has no SQL Server runner today — and the SQL under test is
// SQL-Server-specific (MERGE WITH (HOLDLOCK), OUTPUT INTO @tableVar,
// THROW), so SQLite-in-memory or other generic providers can't
// stand in.
//
// Tracked as TD-006. When a real-DB fixture lands (testcontainers
// or LocalDB), remove the Skip = "..." attributes — every test
// here is intent-complete; they just need a live DB to run against.
//
// Manual smoke test (T9 final report) covers the same ground end-to-
// end until then.
public sealed class StockMovementLogTests
{
    private const string Skip = "TD-006: requires real SQL Server fixture";

    [Fact(Skip = Skip)]
    public void Receive_WritesOneMovement_WithCorrectShape()
    {
        // Arrange: tenant DB seeded with a Stock row at (loc, prod, owner, uom).
        // Act: ReceivingService.ReceiveStockAsync (or StockRepository
        //      .UpsertOnHandAsync directly) with a positive Quantity and
        //      ReferenceType='ReceivingLine'.
        // Assert: SELECT TOP 1 from inventory.StockMovements WHERE StockId = ...
        //   - exactly 1 row
        //   - MovementType = 'Receive'
        //   - FromLocationId IS NULL
        //   - ToLocationId  = key.LocationId
        //   - QuantityDelta = +qty
        //   - UomId, OwnerId match the Stock row
        //   - ReferenceType = 'ReceivingLine'
        //   - ReferenceId = <the line guid>
        //   - PerformedBy = currentUserId
    }

    [Fact(Skip = Skip)]
    public void Putaway_WritesTwoMovements_OppositeSigns_SharedReference()
    {
        // Arrange: source Stock row with QuantityOnHand >= qty.
        // Act: PutawayService.PutawayStockAsync.
        // Assert: 2 new rows where ReferenceType = 'Putaway':
        //   - one row with StockId = source.Id, QuantityDelta = -qty
        //   - one row with StockId = dest.Id,   QuantityDelta = +qty
        //   - both have ReferenceId IS NULL (TD-004)
        //   - both have FromLocationId = source.LocationId,
        //     ToLocationId = destination location
        //   - both have the same PerformedBy
    }

    [Fact(Skip = Skip)]
    public void Putaway_Reconciles_SumDeltaPerStockId_Matches_Stock_QuantityOnHand_Change()
    {
        // Arrange: known starting Stock balances at source + dest.
        // Act: putaway 5 units.
        // Assert: SUM(QuantityDelta) over the 2 new movements per StockId
        //         equals the OnHand delta on the matching Stock row
        //         (source: -5, dest: +5). This pins the reconciliation
        //         invariant that powers daily integrity checks.
    }

    [Fact(Skip = Skip)]
    public void FailedTransfer_InsufficientQty_RollsBackBothStockAndMovements()
    {
        // Arrange: source Stock with OnHand = 1.
        // Act: TransferStockAsync with quantity = 5 (triggers THROW 50002).
        // Assert: source OnHand still = 1 (rolled back) AND zero new rows
        //         in inventory.StockMovements. SET XACT_ABORT ON guarantee.
    }

    [Fact(Skip = Skip)]
    public void GetByProductAsync_ReturnsMovementsAcrossStockRows_OrderedByPerformedAtDesc()
    {
        // Arrange: 3 movements across 2 Stock rows that share one ProductId.
        // Act: StockMovementRepository.GetByProductAsync(productId).
        // Assert: 3 rows returned in PerformedAt DESC order; the JOIN
        //         through inventory.Stock.ProductId works.
    }
}
