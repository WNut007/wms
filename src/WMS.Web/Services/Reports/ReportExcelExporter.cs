using ClosedXML.Excel;
using WMS.DAL.Repositories.Reports;
using WMS.Web.ViewModels.Reports;

namespace WMS.Web.Services.Reports;

// Phase 23 — ClosedXML-based Excel exporters for the 3 report
// surfaces. Each method returns the .xlsx byte array; controller
// wraps in FileContentResult.
//
// Style: small helper API per sheet — header + totals shared via
// AddHeader/AddTotalsRow. Sheets are auto-fit on save. Numbers use
// #,##0 / 0.00 format strings; dates use yyyy-mm-dd.
//
// Naming: methods named for the source VM, not the file — keeps the
// controller's File() call self-documenting.
public static class ReportExcelExporter
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static (byte[] Bytes, string FileName, string ContentType) ExportInventory(
        InventoryReportViewModel vm)
    {
        using var wb = new XLWorkbook();

        // Sheet 1 — Summary
        var summary = wb.Worksheets.Add("Summary");
        WriteTitle(summary, "Inventory Dashboard — Summary");
        summary.Cell(3, 1).Value = "Snapshot at (UTC)";
        summary.Cell(3, 2).Value = vm.SnapshotAt;
        summary.Cell(3, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        summary.Cell(5, 1).Value = "Total on hand";
        summary.Cell(5, 2).Value = vm.Summary.TotalOnHand;
        summary.Cell(6, 1).Value = "Total allocated";
        summary.Cell(6, 2).Value = vm.Summary.TotalAllocated;
        summary.Cell(7, 1).Value = "Distinct products";
        summary.Cell(7, 2).Value = vm.Summary.DistinctProducts;
        summary.Cell(8, 1).Value = "Distinct locations";
        summary.Cell(8, 2).Value = vm.Summary.DistinctLocations;
        summary.Range(5, 2, 8, 2).Style.NumberFormat.Format = "#,##0.####";
        summary.Columns().AdjustToContents();

        // Sheet 2 — Stock by warehouse
        var byWarehouse = wb.Worksheets.Add("By Warehouse");
        AddHeader(byWarehouse, "Warehouse code", "Warehouse name", "Total on hand", "Product count");
        int row = 2;
        foreach (var r in vm.StockByWarehouse)
        {
            byWarehouse.Cell(row, 1).Value = r.WarehouseCode;
            byWarehouse.Cell(row, 2).Value = r.WarehouseName;
            byWarehouse.Cell(row, 3).Value = r.TotalOnHand;
            byWarehouse.Cell(row, 4).Value = r.ProductCount;
            row++;
        }
        byWarehouse.Range(2, 3, row - 1, 3).Style.NumberFormat.Format = "#,##0.####";
        byWarehouse.Columns().AdjustToContents();

        // Sheet 3 — Aging
        var aging = wb.Worksheets.Add("Aging");
        AddHeader(aging, "Bucket", "Stock rows", "Total on hand");
        row = 2;
        foreach (var b in vm.AgingBuckets)
        {
            aging.Cell(row, 1).Value = b.Bucket;
            aging.Cell(row, 2).Value = b.StockRows;
            aging.Cell(row, 3).Value = b.TotalOnHand;
            row++;
        }
        aging.Range(2, 3, row - 1, 3).Style.NumberFormat.Format = "#,##0.####";
        aging.Columns().AdjustToContents();

        // Sheet 4 — Top products
        var top = wb.Worksheets.Add("Top SKUs");
        AddHeader(top, "Product code", "Product name", "Total on hand");
        row = 2;
        foreach (var p in vm.TopProducts)
        {
            top.Cell(row, 1).Value = p.ProductCode;
            top.Cell(row, 2).Value = p.ProductName;
            top.Cell(row, 3).Value = p.TotalOnHand;
            row++;
        }
        top.Range(2, 3, row - 1, 3).Style.NumberFormat.Format = "#,##0.####";
        top.Columns().AdjustToContents();

        // Sheet 5 — Slow movers
        var slow = wb.Worksheets.Add("Slow Movers");
        AddHeader(slow, "Product code", "Product name", "On hand", "Last movement", "Days since");
        row = 2;
        foreach (var s in vm.SlowMovers)
        {
            slow.Cell(row, 1).Value = s.ProductCode;
            slow.Cell(row, 2).Value = s.ProductName;
            slow.Cell(row, 3).Value = s.TotalOnHand;
            if (s.LastMovementAt.HasValue)
            {
                slow.Cell(row, 4).Value = s.LastMovementAt.Value;
                slow.Cell(row, 4).Style.DateFormat.Format = "yyyy-mm-dd";
                slow.Cell(row, 5).Value = s.DaysSinceMovement;
            }
            else
            {
                slow.Cell(row, 4).Value = "(never)";
                slow.Cell(row, 5).Value = "(never)";
            }
            row++;
        }
        slow.Range(2, 3, row - 1, 3).Style.NumberFormat.Format = "#,##0.####";
        slow.Columns().AdjustToContents();

        return ToFile(wb, $"inventory-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    public static (byte[] Bytes, string FileName, string ContentType) ExportOrders(
        OrderAnalyticsViewModel vm)
    {
        using var wb = new XLWorkbook();

        // Sheet 1 — Range + totals
        var summary = wb.Worksheets.Add("Summary");
        WriteTitle(summary, $"Order Analytics — {vm.PresetLabel}");
        summary.Cell(3, 1).Value = "From (UTC)";
        summary.Cell(3, 2).Value = vm.FromUtc;
        summary.Cell(4, 1).Value = "To (UTC)";
        summary.Cell(4, 2).Value = vm.ToUtc;
        summary.Range(3, 2, 4, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        summary.Cell(6, 1).Value = "Total orders";
        summary.Cell(6, 2).Value = vm.TotalOrders;
        summary.Cell(7, 1).Value = "Active orders";
        summary.Cell(7, 2).Value = vm.ActiveOrders;
        summary.Cell(8, 1).Value = "Cancelled orders";
        summary.Cell(8, 2).Value = vm.CancelledOrders;
        summary.Columns().AdjustToContents();

        // Sheet 2 — By status
        var byStatus = wb.Worksheets.Add("By Status");
        AddHeader(byStatus, "Status", "Order count");
        int row = 2;
        foreach (var s in vm.OrdersByStatus)
        {
            byStatus.Cell(row, 1).Value = s.Status;
            byStatus.Cell(row, 2).Value = s.OrderCount;
            row++;
        }
        byStatus.Columns().AdjustToContents();

        // Sheet 3 — Daily orders
        var byDate = wb.Worksheets.Add("Daily");
        AddHeader(byDate, "Date", "Order count");
        row = 2;
        foreach (var d in vm.OrdersByDate)
        {
            byDate.Cell(row, 1).Value = d.Day;
            byDate.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            byDate.Cell(row, 2).Value = d.OrderCount;
            row++;
        }
        byDate.Columns().AdjustToContents();

        // Sheet 4 — Top customers
        var customers = wb.Worksheets.Add("Top Customers");
        AddHeader(customers, "Customer code", "Customer name", "Order count", "Total quantity");
        row = 2;
        foreach (var c in vm.TopCustomers)
        {
            customers.Cell(row, 1).Value = c.CustomerCode;
            customers.Cell(row, 2).Value = c.CustomerName;
            customers.Cell(row, 3).Value = c.OrderCount;
            customers.Cell(row, 4).Value = c.TotalQuantity;
            row++;
        }
        customers.Range(2, 4, row - 1, 4).Style.NumberFormat.Format = "#,##0.####";
        customers.Columns().AdjustToContents();

        // Sheet 5 — Fulfillment cycle
        var cycle = wb.Worksheets.Add("Cycle Time");
        AddHeader(cycle, "Month", "Orders shipped", "Avg days");
        row = 2;
        foreach (var c in vm.FulfillmentCycle)
        {
            cycle.Cell(row, 1).Value = c.Label;
            cycle.Cell(row, 2).Value = c.OrdersShipped;
            cycle.Cell(row, 3).Value = c.AvgDays;
            row++;
        }
        cycle.Range(2, 3, row - 1, 3).Style.NumberFormat.Format = "0.00";
        cycle.Columns().AdjustToContents();

        return ToFile(wb,
            $"orders-{vm.Preset}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    public static (byte[] Bytes, string FileName, string ContentType) ExportKpis(
        KpiReportViewModel vm)
    {
        using var wb = new XLWorkbook();

        // Sheet 1 — Summary tiles
        var summary = wb.Worksheets.Add("Summary");
        WriteTitle(summary, $"Operational KPIs — {vm.PresetLabel}");
        summary.Cell(3, 1).Value = "From (UTC)";
        summary.Cell(3, 2).Value = vm.FromUtc;
        summary.Cell(4, 1).Value = "To (UTC)";
        summary.Cell(4, 2).Value = vm.ToUtc;
        summary.Range(3, 2, 4, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        summary.Cell(6, 1).Value = "Total picks";
        summary.Cell(6, 2).Value = vm.TotalPicks;
        summary.Cell(7, 1).Value = "Total packs";
        summary.Cell(7, 2).Value = vm.TotalPacks;
        summary.Cell(8, 1).Value = "On-time shipping %";
        summary.Cell(8, 2).Value = vm.OnTimePercentage;
        summary.Cell(8, 2).Style.NumberFormat.Format = "0.00";
        summary.Cell(9, 1).Value = "Count accuracy %";
        summary.Cell(9, 2).Value = vm.AccuracyPercentage;
        summary.Cell(9, 2).Style.NumberFormat.Format = "0.00";
        summary.Columns().AdjustToContents();

        // Sheet 2 — Daily picks
        var picks = wb.Worksheets.Add("Daily Picks");
        AddHeader(picks, "Date", "Picks");
        int row = 2;
        foreach (var p in vm.PicksByDay)
        {
            picks.Cell(row, 1).Value = p.Day;
            picks.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            picks.Cell(row, 2).Value = p.Operations;
            row++;
        }
        picks.Columns().AdjustToContents();

        // Sheet 3 — Daily packs
        var packs = wb.Worksheets.Add("Daily Packs");
        AddHeader(packs, "Date", "Packs");
        row = 2;
        foreach (var p in vm.PacksByDay)
        {
            packs.Cell(row, 1).Value = p.Day;
            packs.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            packs.Cell(row, 2).Value = p.Operations;
            row++;
        }
        packs.Columns().AdjustToContents();

        // Sheet 4 — Top pickers
        var pickers = wb.Worksheets.Add("Top Pickers");
        AddHeader(pickers, "User", "Picks completed");
        row = 2;
        foreach (var p in vm.TopPickers)
        {
            pickers.Cell(row, 1).Value = p.UserName;
            pickers.Cell(row, 2).Value = p.OperationCount;
            row++;
        }
        pickers.Columns().AdjustToContents();

        // Sheet 5 — Cycle count
        var counts = wb.Worksheets.Add("Cycle Counts");
        AddHeader(counts, "Metric", "Value");
        counts.Cell(2, 1).Value = "Total sessions";
        counts.Cell(2, 2).Value = vm.CycleCountVariance.TotalSessions;
        counts.Cell(3, 1).Value = "Applied sessions";
        counts.Cell(3, 2).Value = vm.CycleCountVariance.AppliedSessions;
        counts.Cell(4, 1).Value = "Counted lines";
        counts.Cell(4, 2).Value = vm.CycleCountVariance.CountedLines;
        counts.Cell(5, 1).Value = "Variance lines";
        counts.Cell(5, 2).Value = vm.CycleCountVariance.VarianceLines;
        counts.Cell(6, 1).Value = "Variance rate %";
        counts.Cell(6, 2).Value = vm.VariancePercentage;
        counts.Cell(6, 2).Style.NumberFormat.Format = "0.00";
        counts.Columns().AdjustToContents();

        return ToFile(wb,
            $"kpis-{vm.Preset}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static void WriteTitle(IXLWorksheet ws, string title)
    {
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
    }

    private static void AddHeader(IXLWorksheet ws, params string[] columns)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = columns[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(83, 74, 183);
            cell.Style.Font.FontColor = XLColor.White;
        }
    }

    private static (byte[] Bytes, string FileName, string ContentType) ToFile(
        XLWorkbook wb, string fileName)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), fileName, ContentType);
    }
}
