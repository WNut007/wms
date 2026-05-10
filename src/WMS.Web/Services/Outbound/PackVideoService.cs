using Microsoft.Extensions.Logging;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;
using WMS.Web.Services.Storage;

namespace WMS.Web.Services.Outbound;

// Phase 17 (ADR-009) — pack video orchestration. Glue between the
// controller (multipart upload + stream playback) and the two
// underlying systems: IDocumentStorageService (the bytes) +
// IPackVideoRepository (the metadata).
public sealed class PackVideoService : IPackVideoService
{
    private readonly IPackVideoRepositoryFactory _videoRepoFactory;
    private readonly IPackTaskRepositoryFactory _packRepoFactory;
    private readonly IDocumentStorageService _storage;
    private readonly ILogger<PackVideoService> _logger;

    public PackVideoService(
        IPackVideoRepositoryFactory videoRepoFactory,
        IPackTaskRepositoryFactory packRepoFactory,
        IDocumentStorageService storage,
        ILogger<PackVideoService> logger)
    {
        _videoRepoFactory = videoRepoFactory;
        _packRepoFactory = packRepoFactory;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Guid> UploadAsync(
        Guid tenantId,
        Guid packTaskId,
        Stream content,
        string fileName,
        string contentType,
        int durationSec,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (durationSec < 0)
            throw new ArgumentException("DurationSec must be non-negative.", nameof(durationSec));

        // Validate pack task exists + is in a state that warrants
        // a video. Pending tasks have no carton sealed yet so video
        // is meaningless; Cancelled tasks won't ship; only Packed
        // accepts uploads.
        var packRepo = _packRepoFactory.For(tenantId);
        var task = await packRepo.GetByIdAsync(packTaskId, ct)
            ?? throw new InvalidOperationException(
                $"PackTask {packTaskId} not found.");
        if (task.Header.Status != "Packed")
            throw new InvalidOperationException(
                $"Cannot attach video to pack task in '{task.Header.Status}' state — must be Packed.");

        // Storage write happens first. If the DB insert below fails,
        // the orphan blob lives until the retention job clears it
        // (via documents.Files's own audit timestamp). Acceptable
        // trade-off — keeps Upload single-write per layer.
        var meta = await _storage.UploadAsync(
            content,
            fileName,
            contentType,
            entityType: "PackTask",
            entityId: packTaskId.ToString(),
            category: "PackVideo",
            uploadedBy: currentUserId.ToString(),
            ct);

        var videoRepo = _videoRepoFactory.For(tenantId);
        var video = new PackVideo
        {
            Id = Guid.NewGuid(),
            PackTaskId = packTaskId,
            DocumentFileId = meta.DocumentId,
            DurationSec = durationSec,
        };
        await videoRepo.CreateAsync(video, currentUserId, ct);

        _logger.LogInformation(
            "Uploaded pack video {VideoId} for task {PackNumber} ({PackTaskId}) — {Bytes} bytes, {Sec}s",
            video.Id, task.Header.PackNumber, packTaskId, meta.FileSize, durationSec);

        return video.Id;
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> GetStreamAsync(
        Guid tenantId,
        Guid packVideoId,
        CancellationToken ct = default)
    {
        var videoRepo = _videoRepoFactory.For(tenantId);
        var video = await videoRepo.GetByIdAsync(packVideoId, ct);
        if (video is null) return null;

        var streamResult = await _storage.GetStreamAsync(video.DocumentFileId, ct);
        if (streamResult is null) return null;

        var (stream, meta) = streamResult.Value;
        return (stream, meta.ContentType, meta.FileName);
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid packVideoId,
        CancellationToken ct = default)
    {
        var videoRepo = _videoRepoFactory.For(tenantId);
        var video = await videoRepo.GetByIdAsync(packVideoId, ct);
        if (video is null) return false;

        // Storage delete first (per ADR-009 retention pattern). If
        // the storage delete fails, the metadata stays — easier to
        // re-attempt cleanup than to chase orphan blobs.
        await _storage.DeleteAsync(video.DocumentFileId, ct);
        await videoRepo.DeleteAsync(packVideoId, ct);

        _logger.LogInformation(
            "Deleted pack video {VideoId} (PackTaskId={PackTaskId})",
            packVideoId, video.PackTaskId);

        return true;
    }

    public Task<PackVideo?> GetLatestForPackTaskAsync(
        Guid tenantId,
        Guid packTaskId,
        CancellationToken ct = default) =>
        _videoRepoFactory.For(tenantId).GetLatestByPackTaskAsync(packTaskId, ct);
}
