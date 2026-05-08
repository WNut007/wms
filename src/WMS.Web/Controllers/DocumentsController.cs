using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WMS.Web.Services.Storage;

namespace WMS.Web.Controllers;

// Document upload / download / delete / list endpoints. Tenant-scope
// comes from the cookie auth claim — the storage service requires it
// (RequireTenantId throws if missing) so any request that reaches an
// action here has already passed login Steps 1 + 2.
//
// All four endpoints return JSON or the file bytes — no Razor views;
// the Detail page panels call these via fetch.
[Authorize]
[Route("Documents")]
public class DocumentsController : BaseController
{
    private readonly IDocumentStorageService _storage;
    private readonly DocumentStorageOptions _options;

    public DocumentsController(
        IDocumentStorageService storage,
        IOptions<DocumentStorageOptions> options)
    {
        _storage = storage;
        _options = options.Value;
    }

    [HttpPost("Upload")]
    [RequestSizeLimit(100 * 1024 * 1024)]   // 100 MB hard ceiling at the framework
    [RequestFormLimits(MultipartBodyLengthLimit = 100 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string entityType,
        [FromForm] string entityId,
        [FromForm] string? category,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return JsonError("No file received.");

        try
        {
            await using var stream = file.OpenReadStream();
            var meta = await _storage.UploadAsync(
                stream,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                entityType,
                entityId,
                category ?? "Document",
                CurrentUser.FullName ?? CurrentUser.Email ?? "Unknown",
                ct);

            return Json(new
            {
                documentId        = meta.DocumentId,
                fileName          = meta.FileName,
                category          = meta.Category,
                fileSize          = meta.FileSize,
                fileSizeFormatted = meta.FileSizeFormatted,
                uploadedBy        = meta.UploadedBy,
                uploadedAt        = meta.UploadedAt,
                iconClass         = meta.IconClass,
                iconBgColor       = meta.IconColorBg,
                iconFgColor       = meta.IconColorFg,
            });
        }
        catch (StorageValidationException ex)
        {
            return JsonError(ex.Message);
        }
    }

    [HttpGet("Download/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await _storage.GetStreamAsync(id, ct);
        if (result is null) return NotFound();

        var (stream, meta) = result.Value;
        return File(stream, meta.ContentType, meta.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _storage.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("List")]
    public async Task<IActionResult> List(
        [FromQuery] string entityType,
        [FromQuery] string entityId,
        [FromQuery] string? kind,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
            return JsonError("entityType and entityId are required.");

        var docs = await _storage.ListByEntityAsync(entityType, entityId, ct);

        // kind=image  → image/* only (Images tab on Detail page)
        // kind=document → everything else (Documents tab — keeps PDFs out of the gallery if a future
        //                  uploader misroutes a PNG into Documents)
        // anything else / omitted → no filter, full list
        IEnumerable<DocumentMetadata> filtered = kind?.ToLowerInvariant() switch
        {
            "image"    => docs.Where(d => d.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)),
            "document" => docs.Where(d => !d.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)),
            _          => docs,
        };
        var list = filtered.ToList();

        return Json(new
        {
            items = list.Select(d => new
            {
                contentType       = d.ContentType,
                downloadUrl       = $"/Documents/Download/{d.DocumentId}",
                documentId        = d.DocumentId,
                fileName          = d.FileName,
                category          = d.Category,
                fileSize          = d.FileSize,
                fileSizeFormatted = d.FileSizeFormatted,
                uploadedBy        = d.UploadedBy,
                uploadedAt        = d.UploadedAt,
                iconClass         = d.IconClass,
                iconBgColor       = d.IconColorBg,
                iconFgColor       = d.IconColorFg,
            }),
            total              = list.Count,
            maxFileSizeMB      = _options.MaxFileSizeMB,
            allowedExtensions  = _options.AllowedExtensions,
        });
    }
}
