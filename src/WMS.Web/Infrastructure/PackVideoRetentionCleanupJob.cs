using Dapper;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using WMS.DAL.Repositories.Documents;
using WMS.DAL.Repositories.Outbound;
using WMS.Web.Services.Storage;

namespace WMS.Web.Infrastructure;

// Phase 17 (ADR-009) — daily pack-video retention cleanup. Deletes
// videos older than `RetentionDays` (default 10) from every active
// tenant.
//
// Why master-DB tenant enumeration instead of an injected
// ITenantsRepository: there's no such repo today (only IUserTenantMap-
// Repository which is auth-flow-shaped). Pulling the list with raw
// Dapper inside the job stays self-contained and avoids spawning a
// new repo for one job. If a second job needs the same enumeration
// later, refactor to a shared interface.
//
// Why no LocalFileStorageService.DeleteAsync: it depends on
// ITenantContext (HTTP-scoped). The job has no HTTP request, so it
// would need a mutable JobTenantContext + scope-per-iteration. The
// per-tenant on-disk path is well-defined ({root}/{tenantId:N}/...),
// so the job replicates the delete-then-row pattern inline. Five
// lines, no DI gymnastics.
public sealed class PackVideoRetentionCleanupJob
{
    private readonly IConfiguration _config;
    private readonly IPackVideoRepositoryFactory _videoRepoFactory;
    private readonly IDocumentRepositoryFactory _docRepoFactory;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly PackVideoRetentionOptions _retentionOptions;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PackVideoRetentionCleanupJob> _logger;

    public PackVideoRetentionCleanupJob(
        IConfiguration config,
        IPackVideoRepositoryFactory videoRepoFactory,
        IDocumentRepositoryFactory docRepoFactory,
        IOptions<DocumentStorageOptions> storageOptions,
        IOptions<PackVideoRetentionOptions> retentionOptions,
        IWebHostEnvironment env,
        ILogger<PackVideoRetentionCleanupJob> logger)
    {
        _config = config;
        _videoRepoFactory = videoRepoFactory;
        _docRepoFactory = docRepoFactory;
        _storageOptions = storageOptions.Value;
        _retentionOptions = retentionOptions.Value;
        _env = env;
        _logger = logger;
    }

    // Hangfire calls this via its own DI scope (RecurringJob.AddOr-
    // Update wires it). The [DisableConcurrentExecution] attribute
    // protects against overlapping daily runs (e.g. yesterday's run
    // hung).
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(_retentionOptions.RetentionDays);
        _logger.LogInformation(
            "Pack video retention cleanup starting — cutoff {Cutoff:O} ({Days} days)",
            cutoff, _retentionOptions.RetentionDays);

        var tenantIds = await GetActiveTenantIdsAsync(ct);

        var rootPath = ResolveStorageRoot();

        int totalDeleted = 0;
        foreach (var tenantId in tenantIds)
        {
            ct.ThrowIfCancellationRequested();
            var perTenant = await CleanupTenantAsync(tenantId, cutoff, rootPath, ct);
            totalDeleted += perTenant;
        }

        _logger.LogInformation(
            "Pack video retention cleanup done — {TenantCount} tenant(s), {VideoCount} video(s) deleted",
            tenantIds.Count, totalDeleted);
    }

    private async Task<int> CleanupTenantAsync(
        Guid tenantId, DateTime cutoff, string rootPath, CancellationToken ct)
    {
        var videoRepo = _videoRepoFactory.For(tenantId);
        var docRepo = _docRepoFactory.For(tenantId);

        var olds = await videoRepo.GetOlderThanAsync(cutoff, ct);
        if (olds.Count == 0) return 0;

        int deleted = 0;
        foreach (var v in olds)
        {
            ct.ThrowIfCancellationRequested();

            // Storage delete first (per ADR-009 retention pattern):
            //   1. Resolve documents.Files row to get StorageKey.
            //   2. Delete on-disk bytes (TryDelete, skip-if-missing).
            //   3. Delete documents.Files row.
            //   4. Delete outbound.PackVideos row.
            // If any step fails, the next daily run picks it up
            // (idempotent — already-deleted rows just disappear from
            // the next GetOlderThanAsync result).
            try
            {
                var docFile = await docRepo.GetByIdAsync(v.DocumentFileId, ct);
                if (docFile is not null)
                {
                    var absPath = Path.Combine(
                        rootPath,
                        docFile.StorageKey.Replace('/', Path.DirectorySeparatorChar));
                    TryDelete(absPath);
                    await docRepo.DeleteAsync(v.DocumentFileId, ct);
                }
                await videoRepo.DeleteAsync(v.Id, ct);
                deleted++;
            }
            catch (Exception ex)
            {
                // Log + continue. The next run will retry.
                _logger.LogWarning(ex,
                    "Failed to delete pack video {VideoId} (tenant {TenantId}, recorded {RecordedAt:O}) — will retry next run",
                    v.Id, tenantId, v.RecordedAt);
            }
        }

        if (deleted > 0)
            _logger.LogInformation(
                "Tenant {TenantId} — deleted {Count} pack video(s)",
                tenantId, deleted);

        return deleted;
    }

    // Read the live tenant list from master DB. Active tenants only —
    // suspended/inactive tenants might still want their videos held
    // (TD if business policy disagrees).
    private async Task<List<Guid>> GetActiveTenantIdsAsync(CancellationToken ct)
    {
        var masterConn = _config.GetConnectionString("MasterDb")
            ?? throw new InvalidOperationException("MasterDb connection string is required.");

        await using var conn = new SqlConnection(masterConn);
        var ids = await conn.QueryAsync<Guid>(new CommandDefinition(
            "SELECT Id FROM master.Tenants WHERE Status = 'Active';",
            cancellationToken: ct));
        return ids.AsList();
    }

    // Mirror of LocalFileStorageService's root-path resolution so the
    // job hits the same disk location an interactive UploadAsync /
    // DeleteAsync would.
    private string ResolveStorageRoot()
    {
        var configured = _storageOptions.Local.RootPath;
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_env.ContentRootPath, configured);
    }

    private void TryDelete(string absPath)
    {
        try
        {
            if (File.Exists(absPath)) File.Delete(absPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not delete pack-video file {Path} — orphan blob, will retry next run", absPath);
        }
    }
}
