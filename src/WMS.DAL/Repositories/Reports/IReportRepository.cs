namespace WMS.DAL.Repositories.Reports;

// Phase 23 — read-only aggregation API for the Reports module. Lives
// separately from operational repos so chart-shape projections don't
// drag into the IStockRepository / ISalesOrderRepository surfaces.
//
// All methods are pure reads (no writes, no movements). Date ranges
// are caller-supplied; controllers translate the fixed-preset
// dropdown (today / week / month / quarter / year) to (from, to).
//
// Implementation: inline Dapper SQL with JOINs against existing
// schema. No new tables this phase. The Stock value calc is deferred
// (Product entity has no Cost field — TD-045; pricing lives in
// ProductOwners.SettlementPrice which is owner-scoped).
public interface IReportRepository
{
    // ── Inventory ──────────────────────────────────────────────────────

    Task<InventorySummary> GetInventorySummaryAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StockByWarehouseRow>> GetStockByWarehouseAsync(
        CancellationToken ct = default);

    // 4 buckets keyed on Stock.CreatedAt — newest material first. Empty
    // buckets are still returned (with zero counts) so chart axes
    // render every bucket label.
    Task<IReadOnlyList<StockAgingBucket>> GetStockAgingBucketsAsync(
        CancellationToken ct = default);

    Task<IReadOnlyList<TopProductRow>> GetTopProductsByQuantityAsync(
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<SlowMoverRow>> GetSlowMoversAsync(
        int daysThreshold,
        int limit,
        CancellationToken ct = default);

    // ── Orders ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<OrderStatusCount>> GetOrdersByStatusAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<OrdersByDateRow>> GetOrdersByDateAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<TopCustomerRow>> GetTopCustomersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<FulfillmentCycleRow>> GetFulfillmentCycleAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    // ── Operational KPIs ───────────────────────────────────────────────

    Task<IReadOnlyList<MovementByDayRow>> GetPicksByDayAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<MovementByDayRow>> GetPacksByDayAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    Task<CycleCountVarianceSummary> GetCycleCountVarianceAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    Task<OnTimeShippingSummary> GetOnTimeShippingAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<TopOperatorRow>> GetTopPickersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct = default);
}
