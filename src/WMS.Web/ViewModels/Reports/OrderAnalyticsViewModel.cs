using WMS.DAL.Repositories.Reports;

namespace WMS.Web.ViewModels.Reports;

// Phase 23 — bundle for /Reports/Orders. Order counts + top customers
// + fulfillment cycle aggregates. Date range driven by Preset
// (T3 supports today/week/month/quarter/year — custom ranges = TD).
public sealed class OrderAnalyticsViewModel
{
    public IReadOnlyList<OrderStatusCount> OrdersByStatus { get; set; } =
        Array.Empty<OrderStatusCount>();

    public IReadOnlyList<OrdersByDateRow> OrdersByDate { get; set; } =
        Array.Empty<OrdersByDateRow>();

    public IReadOnlyList<TopCustomerRow> TopCustomers { get; set; } =
        Array.Empty<TopCustomerRow>();

    public IReadOnlyList<FulfillmentCycleRow> FulfillmentCycle { get; set; } =
        Array.Empty<FulfillmentCycleRow>();

    public string Preset { get; set; } = DateRangePreset.Default;
    public string PresetLabel { get; set; } = "";
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    // Pre-computed convenience for the stat tiles.
    public int TotalOrders => OrdersByStatus.Sum(o => o.OrderCount);
    public int CancelledOrders =>
        OrdersByStatus.FirstOrDefault(o => o.Status == "Cancelled")?.OrderCount ?? 0;
    public int ActiveOrders => TotalOrders - CancelledOrders;
    public int TopCustomerCount => TopCustomers.Count;
}
