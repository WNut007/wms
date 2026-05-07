namespace WMS.Web.ViewModels.Home;

public class DashboardViewModel
{
    public int ReceiptsTotal { get; init; }
    public int OrdersTotal { get; init; }
    public int[] LiveFeedData { get; init; } = Array.Empty<int>();
    public ProgressItem PendingPutaway { get; init; } = null!;
    public ProgressItem PickedToday { get; init; } = null!;
    public ProgressItem OrderAccuracy { get; init; } = null!;
    public ProgressItem SlaCompliance { get; init; } = null!;
    public MetricCard[] MetricCards { get; init; } = Array.Empty<MetricCard>();
}

public record ProgressItem(string Label, int Current, int Total, string ColorHex)
{
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Current / Total);
}

public record MetricCard(
    string Label,
    int Value,
    string ColorHex,
    int[] Sparkline,
    string ChipPrimary,
    string ChipSecondary);
