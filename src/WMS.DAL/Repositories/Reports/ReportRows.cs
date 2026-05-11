namespace WMS.DAL.Repositories.Reports;

// Phase 23 — read-projections for the Reports module. Records bind
// positionally to SQL output; column names + types must match exactly
// (per feedback_dapper_record_binding.md). Use AS aliases in repository
// SQL when needed.

// ── Inventory dashboard ────────────────────────────────────────────────

public sealed record InventorySummary(
    decimal TotalOnHand,
    decimal TotalAllocated,
    int DistinctProducts,
    int DistinctLocations);

public sealed record StockByWarehouseRow(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal TotalOnHand,
    int ProductCount);

public sealed record StockAgingBucket(
    string Bucket,        // '0-30d' | '30-90d' | '90-180d' | '180+d'
    int StockRows,
    decimal TotalOnHand);

public sealed record TopProductRow(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal TotalOnHand);

public sealed record SlowMoverRow(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal TotalOnHand,
    DateTime? LastMovementAt,
    int? DaysSinceMovement);

// ── Order analytics ────────────────────────────────────────────────────

public sealed record OrderStatusCount(
    string Status,
    int OrderCount);

public sealed record OrdersByDateRow(
    DateTime Day,
    int OrderCount);

public sealed record TopCustomerRow(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    int OrderCount,
    decimal TotalQuantity);

public sealed record FulfillmentCycleRow(
    int MonthBucket,    // YYYYMM int for stable sort + label
    string Label,       // 'May 2026'
    decimal AvgDays,
    int OrdersShipped);

// ── Operational KPIs ───────────────────────────────────────────────────

public sealed record MovementByDayRow(
    DateTime Day,
    int Operations);

public sealed record CycleCountVarianceSummary(
    int TotalSessions,
    int AppliedSessions,
    int VarianceLines,
    int CountedLines);

public sealed record OnTimeShippingSummary(
    int TotalShipped,
    int OnTimeShipped);

public sealed record TopOperatorRow(
    Guid UserId,
    string UserName,
    int OperationCount);
