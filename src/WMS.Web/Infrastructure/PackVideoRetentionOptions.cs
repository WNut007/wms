namespace WMS.Web.Infrastructure;

// Phase 17 (ADR-009) — pack video retention configuration. Bound from
// "PackVideoRetention" section of appsettings.json. RetentionDays
// drives the WHERE cutoff in PackVideoRetentionCleanupJob; CronSchedule
// drives Hangfire's recurring registration.
public sealed class PackVideoRetentionOptions
{
    public const string SectionName = "PackVideoRetention";

    // 10-day default per ADR-009. Per-tenant override deferred (TD).
    public int RetentionDays { get; set; } = 10;

    // 03:00 UTC daily. Hangfire cron format.
    public string CronSchedule { get; set; } = "0 3 * * *";

    // Hangfire job-id key. Stable across restarts so AddOrUpdate
    // replaces rather than duplicates.
    public string JobId { get; set; } = "pack-video-retention-cleanup";
}
