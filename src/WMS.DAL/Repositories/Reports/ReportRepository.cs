using System.Data;
using Dapper;

namespace WMS.DAL.Repositories.Reports;

// Phase 23 — read-only aggregation surface for Reports. Single class
// bound to one tenant connection (factory-scoped). All queries are
// idempotent reads; no writes / no movement log mutations.
//
// SQL style: inline Dapper with explicit JOINs. Each method's SQL is
// self-contained — duplication over abstraction so the report-shaped
// projections stay easy to tune per chart.
internal sealed class ReportRepository : IReportRepository
{
    private readonly IDbConnection _connection;

    public ReportRepository(IDbConnection connection) => _connection = connection;

    // ── Inventory ──────────────────────────────────────────────────────

    public async Task<InventorySummary> GetInventorySummaryAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    ISNULL(SUM(s.QuantityOnHand), 0) AS TotalOnHand,
    ISNULL(SUM(s.QuantityAllocated), 0) AS TotalAllocated,
    COUNT(DISTINCT s.ProductId) AS DistinctProducts,
    COUNT(DISTINCT s.LocationId) AS DistinctLocations
FROM inventory.Stock s
WHERE s.QuantityOnHand > 0;";

        return await _connection.QuerySingleAsync<InventorySummary>(
            new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<StockByWarehouseRow>> GetStockByWarehouseAsync(
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    w.Id AS WarehouseId,
    w.Code AS WarehouseCode,
    w.Name AS WarehouseName,
    ISNULL(SUM(s.QuantityOnHand), 0) AS TotalOnHand,
    COUNT(DISTINCT s.ProductId) AS ProductCount
FROM master.Warehouses w
LEFT JOIN master.Locations l ON l.WarehouseId = w.Id
LEFT JOIN inventory.Stock s
    ON s.LocationId = l.Id AND s.QuantityOnHand > 0
WHERE w.IsActive = 1
GROUP BY w.Id, w.Code, w.Name
ORDER BY TotalOnHand DESC, w.Code;";

        var rows = await _connection.QueryAsync<StockByWarehouseRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockAgingBucket>> GetStockAgingBucketsAsync(
        CancellationToken ct = default)
    {
        // 4 buckets keyed off Stock.CreatedAt (proxy for "when did this
        // material first land in this 6-tuple"). Returns all 4 even if
        // empty so the chart label axis stays stable.
        const string sql = @"
WITH Buckets AS (
    SELECT '0-30d' AS Bucket, 1 AS SortOrder
    UNION ALL SELECT '30-90d', 2
    UNION ALL SELECT '90-180d', 3
    UNION ALL SELECT '180+d', 4
),
StockBuckets AS (
    SELECT
        CASE
            WHEN DATEDIFF(DAY, s.CreatedAt, SYSUTCDATETIME()) < 30 THEN '0-30d'
            WHEN DATEDIFF(DAY, s.CreatedAt, SYSUTCDATETIME()) < 90 THEN '30-90d'
            WHEN DATEDIFF(DAY, s.CreatedAt, SYSUTCDATETIME()) < 180 THEN '90-180d'
            ELSE '180+d'
        END AS Bucket,
        s.QuantityOnHand
    FROM inventory.Stock s
    WHERE s.QuantityOnHand > 0
)
SELECT
    b.Bucket,
    ISNULL(COUNT(sb.Bucket), 0) AS StockRows,
    ISNULL(SUM(sb.QuantityOnHand), 0) AS TotalOnHand
FROM Buckets b
LEFT JOIN StockBuckets sb ON sb.Bucket = b.Bucket
GROUP BY b.Bucket, b.SortOrder
ORDER BY b.SortOrder;";

        var rows = await _connection.QueryAsync<StockAgingBucket>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TopProductRow>> GetTopProductsByQuantityAsync(
        int limit,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT TOP (@limit)
    p.Id AS ProductId,
    p.Code AS ProductCode,
    p.Name AS ProductName,
    SUM(s.QuantityOnHand) AS TotalOnHand
FROM inventory.Stock s
INNER JOIN master.Products p ON p.Id = s.ProductId
WHERE s.QuantityOnHand > 0
GROUP BY p.Id, p.Code, p.Name
ORDER BY TotalOnHand DESC, p.Code;";

        var rows = await _connection.QueryAsync<TopProductRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SlowMoverRow>> GetSlowMoversAsync(
        int daysThreshold,
        int limit,
        CancellationToken ct = default)
    {
        // Slow mover = product with positive Stock whose most-recent
        // LastMovementAt across ALL its Stock rows is older than the
        // threshold (or NULL — never moved since arrival).
        const string sql = @"
WITH ProductActivity AS (
    SELECT
        s.ProductId,
        SUM(s.QuantityOnHand) AS TotalOnHand,
        MAX(s.LastMovementAt) AS LastMovementAt
    FROM inventory.Stock s
    WHERE s.QuantityOnHand > 0
    GROUP BY s.ProductId
)
SELECT TOP (@limit)
    p.Id AS ProductId,
    p.Code AS ProductCode,
    p.Name AS ProductName,
    pa.TotalOnHand,
    pa.LastMovementAt,
    CASE
        WHEN pa.LastMovementAt IS NULL THEN NULL
        ELSE DATEDIFF(DAY, pa.LastMovementAt, SYSUTCDATETIME())
    END AS DaysSinceMovement
FROM ProductActivity pa
INNER JOIN master.Products p ON p.Id = pa.ProductId
WHERE pa.LastMovementAt IS NULL
   OR DATEDIFF(DAY, pa.LastMovementAt, SYSUTCDATETIME()) >= @daysThreshold
ORDER BY
    CASE WHEN pa.LastMovementAt IS NULL THEN 0 ELSE 1 END,
    pa.LastMovementAt,
    pa.TotalOnHand DESC;";

        var rows = await _connection.QueryAsync<SlowMoverRow>(
            new CommandDefinition(sql, new { daysThreshold, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    // ── Orders ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrderStatusCount>> GetOrdersByStatusAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    so.Status,
    COUNT(*) AS OrderCount
FROM outbound.SalesOrders so
WHERE so.CreatedAt >= @fromUtc AND so.CreatedAt < @toUtc
GROUP BY so.Status
ORDER BY OrderCount DESC;";

        var rows = await _connection.QueryAsync<OrderStatusCount>(
            new CommandDefinition(sql, new { fromUtc, toUtc }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OrdersByDateRow>> GetOrdersByDateAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    CAST(so.OrderDate AS DATETIME) AS Day,
    COUNT(*) AS OrderCount
FROM outbound.SalesOrders so
WHERE so.OrderDate >= @fromDate AND so.OrderDate < @toDate
GROUP BY so.OrderDate
ORDER BY so.OrderDate;";

        var rows = await _connection.QueryAsync<OrdersByDateRow>(
            new CommandDefinition(
                sql,
                new { fromDate = fromUtc.Date, toDate = toUtc.Date.AddDays(1) },
                cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TopCustomerRow>> GetTopCustomersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT TOP (@limit)
    c.Id AS CustomerId,
    c.Code AS CustomerCode,
    c.Name AS CustomerName,
    COUNT(DISTINCT so.Id) AS OrderCount,
    ISNULL(SUM(sol.OrderedQuantity), 0) AS TotalQuantity
FROM outbound.SalesOrders so
INNER JOIN master.Customers c ON c.Id = so.CustomerId
LEFT JOIN outbound.SalesOrderLines sol ON sol.SalesOrderId = so.Id
WHERE so.CreatedAt >= @fromUtc AND so.CreatedAt < @toUtc
  AND so.Status <> 'Cancelled'
GROUP BY c.Id, c.Code, c.Name
ORDER BY OrderCount DESC, TotalQuantity DESC;";

        var rows = await _connection.QueryAsync<TopCustomerRow>(
            new CommandDefinition(sql, new { fromUtc, toUtc, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<FulfillmentCycleRow>> GetFulfillmentCycleAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        // Joins Shipments back to SalesOrders to compute days between
        // OrderDate and ShippedAt. Groups by YYYY-MM bucket. Only
        // Shipped shipments count (Pending/Cancelled excluded).
        const string sql = @"
SELECT
    YEAR(sh.ShippedAt) * 100 + MONTH(sh.ShippedAt) AS MonthBucket,
    DATENAME(MONTH, sh.ShippedAt) + ' ' + CAST(YEAR(sh.ShippedAt) AS VARCHAR(4)) AS Label,
    CAST(AVG(CAST(DATEDIFF(DAY, so.OrderDate, sh.ShippedAt) AS DECIMAL(10,2))) AS DECIMAL(18,4)) AS AvgDays,
    COUNT(*) AS OrdersShipped
FROM outbound.Shipments sh
INNER JOIN outbound.SalesOrders so ON so.Id = sh.SalesOrderId
WHERE sh.Status = 'Shipped'
  AND sh.ShippedAt IS NOT NULL
  AND sh.ShippedAt >= @fromUtc AND sh.ShippedAt < @toUtc
GROUP BY YEAR(sh.ShippedAt), MONTH(sh.ShippedAt), DATENAME(MONTH, sh.ShippedAt)
ORDER BY MonthBucket;";

        var rows = await _connection.QueryAsync<FulfillmentCycleRow>(
            new CommandDefinition(sql, new { fromUtc, toUtc }, cancellationToken: ct));
        return rows.AsList();
    }

    // ── Operational KPIs ───────────────────────────────────────────────

    public async Task<IReadOnlyList<MovementByDayRow>> GetPicksByDayAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        // Counts PickTasks completed (status Picked or PartiallyPicked)
        // grouped by CompletedAt day. Cancelled / Pending / InProgress
        // excluded — KPI is "picks DONE per day".
        const string sql = @"
SELECT
    CAST(pt.CompletedAt AS DATE) AS Day,
    COUNT(*) AS Operations
FROM outbound.PickTasks pt
WHERE pt.CompletedAt IS NOT NULL
  AND pt.Status IN ('Picked', 'PartiallyPicked')
  AND pt.CompletedAt >= @fromUtc AND pt.CompletedAt < @toUtc
GROUP BY CAST(pt.CompletedAt AS DATE)
ORDER BY Day;";

        var rows = await _connection.QueryAsync<MovementByDayRow>(
            new CommandDefinition(sql, new { fromUtc, toUtc }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MovementByDayRow>> GetPacksByDayAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    CAST(pk.PackedAt AS DATE) AS Day,
    COUNT(*) AS Operations
FROM outbound.PackTasks pk
WHERE pk.PackedAt IS NOT NULL
  AND pk.Status = 'Packed'
  AND pk.PackedAt >= @fromUtc AND pk.PackedAt < @toUtc
GROUP BY CAST(pk.PackedAt AS DATE)
ORDER BY Day;";

        var rows = await _connection.QueryAsync<MovementByDayRow>(
            new CommandDefinition(sql, new { fromUtc, toUtc }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<CycleCountVarianceSummary> GetCycleCountVarianceAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    (SELECT COUNT(*) FROM counts.CycleCounts cc
     WHERE cc.CreatedAt >= @fromUtc AND cc.CreatedAt < @toUtc) AS TotalSessions,
    (SELECT COUNT(*) FROM counts.CycleCounts cc
     WHERE cc.Status = 'Applied'
       AND cc.AppliedAt IS NOT NULL
       AND cc.AppliedAt >= @fromUtc AND cc.AppliedAt < @toUtc) AS AppliedSessions,
    (SELECT COUNT(*) FROM counts.CycleCountLines ccl
       INNER JOIN counts.CycleCounts cc ON cc.Id = ccl.CycleCountId
       WHERE cc.Status = 'Applied'
         AND cc.AppliedAt >= @fromUtc AND cc.AppliedAt < @toUtc
         AND ccl.CountedQuantity IS NOT NULL
         AND ccl.CountedQuantity <> ccl.ExpectedQuantity) AS VarianceLines,
    (SELECT COUNT(*) FROM counts.CycleCountLines ccl
       INNER JOIN counts.CycleCounts cc ON cc.Id = ccl.CycleCountId
       WHERE cc.Status = 'Applied'
         AND cc.AppliedAt >= @fromUtc AND cc.AppliedAt < @toUtc
         AND ccl.CountedQuantity IS NOT NULL) AS CountedLines;";

        return await _connection.QuerySingleAsync<CycleCountVarianceSummary>(
            new CommandDefinition(sql, new { fromUtc, toUtc }, cancellationToken: ct));
    }

    public async Task<OnTimeShippingSummary> GetOnTimeShippingAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        // On-time = ShippedAt date <= RequestedShipDate. SOs without a
        // RequestedShipDate count as on-time (no deadline implies no
        // miss). Compare Shipped within the window.
        const string sql = @"
SELECT
    COUNT(*) AS TotalShipped,
    SUM(CASE
        WHEN so.RequestedShipDate IS NULL THEN 1
        WHEN CAST(sh.ShippedAt AS DATE) <= so.RequestedShipDate THEN 1
        ELSE 0
    END) AS OnTimeShipped
FROM outbound.Shipments sh
INNER JOIN outbound.SalesOrders so ON so.Id = sh.SalesOrderId
WHERE sh.Status = 'Shipped'
  AND sh.ShippedAt IS NOT NULL
  AND sh.ShippedAt >= @fromUtc AND sh.ShippedAt < @toUtc;";

        var result = await _connection.QuerySingleOrDefaultAsync<OnTimeShippingSummary>(
            new CommandDefinition(sql, new { fromUtc, toUtc }, cancellationToken: ct));
        return result ?? new OnTimeShippingSummary(0, 0);
    }

    public async Task<IReadOnlyList<TopOperatorRow>> GetTopPickersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT TOP (@limit)
    u.Id AS UserId,
    COALESCE(NULLIF(u.FullName, ''), u.Email, 'Unknown') AS UserName,
    COUNT(*) AS OperationCount
FROM outbound.PickTasks pt
INNER JOIN security.Users u ON u.Id = pt.CompletedBy
WHERE pt.CompletedAt IS NOT NULL
  AND pt.Status IN ('Picked', 'PartiallyPicked')
  AND pt.CompletedAt >= @fromUtc AND pt.CompletedAt < @toUtc
GROUP BY u.Id, u.FullName, u.Email
ORDER BY OperationCount DESC;";

        var rows = await _connection.QueryAsync<TopOperatorRow>(
            new CommandDefinition(sql, new { fromUtc, toUtc, limit }, cancellationToken: ct));
        return rows.AsList();
    }
}
