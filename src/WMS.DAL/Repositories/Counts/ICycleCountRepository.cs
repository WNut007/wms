using WMS.DAL.Common;
using WMS.Domain.Entities.Counts;

namespace WMS.DAL.Repositories.Counts;

// Phase 12 — tenant-scoped persistence for the cycle count session +
// lines. State transitions Counting → Review → Applied / Cancelled
// are exposed as dedicated atomic methods so the service can compose
// them inside a TransactionScope alongside per-line Stock writes.
public interface ICycleCountRepository
{
    // Inserts header + per-line snapshot in a single transaction.
    // Caller pre-assigns Ids + CountNumber. The line list is
    // materialised by the service from a stock-snapshot read; the
    // repo just persists it.
    Task CreateAsync(
        CycleCount header,
        IReadOnlyList<CycleCountLine> lines,
        Guid startedBy,
        CancellationToken ct = default);

    // Header + lines in one round-trip (Dapper QueryMultiple).
    Task<CycleCountDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CycleCountDetail?> GetByNumberAsync(string countNumber, CancellationToken ct = default);

    // Lines with resolved Product / Location / Owner / UoM + Lot/Pallet
    // NUMBERS (not just Ids) for the Detail page. INNER JOINs because
    // every line has those FKs populated; LEFT JOIN Lot/Pallet (nullable).
    Task<IReadOnlyList<CycleCountLineRow>> GetLineRowsByIdAsync(
        Guid cycleCountId, CancellationToken ct = default);

    // List page query — JOIN Warehouse + per-session aggregates.
    Task<PagedResult<CycleCountListRow>> GetPagedAsync(
        CycleCountFilter filter, CancellationToken ct = default);

    // Chip-count aggregate (Phase 10A pattern).
    Task<CycleCountStatusCounts> GetStatusCountsAsync(
        CycleCountFilter filter, CancellationToken ct = default);

    // Phase 12 — bulk save of CountedQuantity + LineStatus + Notes for
    // a set of lines. Caller passes per-line {Id, CountedQuantity,
    // LineStatus, Notes}. Atomic — either all lines update or none.
    // Allowed only when session is in Counting state (service-layer
    // gate; repo doesn't enforce — the SQL UPDATE itself is unguarded
    // so the service can also use this for re-edit scenarios).
    Task SaveCountedQuantitiesAsync(
        Guid cycleCountId,
        IReadOnlyList<(Guid LineId, decimal? CountedQuantity, string LineStatus, string? Notes)> updates,
        Guid currentUserId,
        CancellationToken ct = default);

    // Phase 12 — atomic Counting → Review transition. Sets CountedBy +
    // CountedAt. Idempotent (WHERE Status='Counting').
    Task<bool> SetSubmittedForReviewAsync(
        Guid cycleCountId,
        Guid countedBy,
        CancellationToken ct = default);

    // Phase 12 — atomic Review → Applied transition. Sets ReviewedBy +
    // ReviewedAt + AppliedAt. Caller has already written the per-line
    // Stock + Movement Log entries inside the same TransactionScope.
    Task<bool> SetAppliedAsync(
        Guid cycleCountId,
        Guid reviewedBy,
        CancellationToken ct = default);

    // Phase 12 — atomic Cancel transition. Allowed from Counting OR
    // Review (not from Applied). Service tries Counting first via
    // WHERE filter then Review.
    Task<bool> SetCancelledAsync(
        Guid cycleCountId,
        string fromStatus,
        string reason,
        Guid cancelledBy,
        CancellationToken ct = default);

    // Phase 12 — used for CountNumber assignment.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);
}

public interface ICycleCountRepositoryFactory
{
    ICycleCountRepository For(Guid tenantId);
}
