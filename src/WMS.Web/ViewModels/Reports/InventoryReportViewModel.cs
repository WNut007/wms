using WMS.DAL.Repositories.Reports;

namespace WMS.Web.ViewModels.Reports;

// Phase 23 — bundle for /Reports/Inventory. Five chart-shaped slices
// + one summary tile row. Constructed in the controller; view binds
// directly to it via @model.
public sealed class InventoryReportViewModel
{
    public InventorySummary Summary { get; set; } = new(0, 0, 0, 0);

    public IReadOnlyList<StockByWarehouseRow> StockByWarehouse { get; set; } =
        Array.Empty<StockByWarehouseRow>();

    public IReadOnlyList<StockAgingBucket> AgingBuckets { get; set; } =
        Array.Empty<StockAgingBucket>();

    public IReadOnlyList<TopProductRow> TopProducts { get; set; } =
        Array.Empty<TopProductRow>();

    public IReadOnlyList<SlowMoverRow> SlowMovers { get; set; } =
        Array.Empty<SlowMoverRow>();

    // Filter echo for the as-of-date hint above the page header.
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
}
