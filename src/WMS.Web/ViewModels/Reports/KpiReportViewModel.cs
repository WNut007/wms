using WMS.DAL.Repositories.Reports;

namespace WMS.Web.ViewModels.Reports;

// Phase 23 — bundle for /Reports/Kpis. Operational metrics —
// throughput (picks/packs per day), accuracy (cycle count variance),
// SLA (on-time shipping %), and performance (top pickers).
public sealed class KpiReportViewModel
{
    public IReadOnlyList<MovementByDayRow> PicksByDay { get; set; } =
        Array.Empty<MovementByDayRow>();

    public IReadOnlyList<MovementByDayRow> PacksByDay { get; set; } =
        Array.Empty<MovementByDayRow>();

    public CycleCountVarianceSummary CycleCountVariance { get; set; } =
        new(0, 0, 0, 0);

    public OnTimeShippingSummary OnTimeShipping { get; set; } =
        new(0, 0);

    public IReadOnlyList<TopOperatorRow> TopPickers { get; set; } =
        Array.Empty<TopOperatorRow>();

    public string Preset { get; set; } = DateRangePreset.Default;
    public string PresetLabel { get; set; } = "";
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    // Computed convenience for the stat tiles.
    public int TotalPicks => PicksByDay.Sum(p => p.Operations);
    public int TotalPacks => PacksByDay.Sum(p => p.Operations);

    public decimal OnTimePercentage =>
        OnTimeShipping.TotalShipped == 0
            ? 0
            : Math.Round((decimal)OnTimeShipping.OnTimeShipped * 100m / OnTimeShipping.TotalShipped, 1);

    public decimal VariancePercentage =>
        CycleCountVariance.CountedLines == 0
            ? 0
            : Math.Round((decimal)CycleCountVariance.VarianceLines * 100m / CycleCountVariance.CountedLines, 1);

    public decimal AccuracyPercentage => 100m - VariancePercentage;
}
