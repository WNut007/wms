using WMS.Domain.Entities.Security;

namespace WMS.DAL.Repositories.Security;

// Phase 24 — Read + Append for security.AuditLog. AppendAsync is the
// only write path; rows are immutable per the migration comment.
// Read methods are wired in T3 (the AuditLog viewer); declared here so
// T1 can land the full interface in one shot.
public sealed record AuditLogListRow(
    Guid Id,
    Guid? UserId,
    string? UserEmail,    // resolved via LEFT JOIN security.Users
    string? UserFullName,
    string EventType,
    string? EntityType,
    Guid? EntityId,
    string? IpAddress,
    string? UserAgent,
    string? Details,
    DateTime CreatedAt);

public sealed record AuditLogFilter(
    int Page = 1,
    int PageSize = 50,
    Guid? UserId = null,
    string? EventType = null,
    string? EntityType = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Search = null);

public interface IAuditLogRepository
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default);

    // T3 read surface — paged log with filters.
    Task<WMS.DAL.Common.PagedResult<AuditLogListRow>> GetPagedAsync(
        AuditLogFilter filter,
        CancellationToken ct = default);

    Task<AuditLogListRow?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Closed-set distinct event-types for filter dropdown population.
    Task<IReadOnlyList<string>> GetDistinctEventTypesAsync(CancellationToken ct = default);
}
