using WMS.DAL.Common;
using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14A — tenant-scoped persistence for outbound.SalesOrders +
// SalesOrderLines. MVP scope: header + lines CRUD + atomic status
// flip. Allocation / pick / pack flows arrive in Phase 14B with
// additional methods on this surface (don't pre-stage them today —
// the schema decisions belong to those phases).
public interface ISalesOrderRepository
{
    // Atomic header + lines insert. Both inputs' Ids are caller-
    // assigned. Audit (CreatedAt default + CreatedBy = userId) is
    // stamped per row.
    Task CreateAsync(
        SalesOrder header,
        IReadOnlyList<SalesOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Header + lines in one round-trip via QueryMultiple. Null when
    // the SO doesn't exist.
    Task<SalesOrderDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Same shape as GetByIdAsync, resolved by tenant-wide-unique
    // SoNumber.
    Task<SalesOrderDetail?> GetByNumberAsync(
        string soNumber, CancellationToken ct = default);

    // Phase 14A — list-page query. JOINs Customers (Code+Name) +
    // Warehouses (Code) + per-SO line aggregate. SortBy whitelisted
    // via SalesOrderSortMapper.
    Task<PagedResult<SalesOrderListRow>> GetPagedAsync(
        SalesOrderFilter filter, CancellationToken ct = default);

    // Phase 14A — chip-count aggregates. Counts respect Customer +
    // Warehouse + Search; ignore Status.
    Task<SalesOrderStatusCounts> GetStatusCountsAsync(
        SalesOrderFilter filter, CancellationToken ct = default);

    // Phase 14A — Detail Lines tab. JOINs Product / Owner / UoM
    // codes for display. Single round-trip.
    Task<IReadOnlyList<SalesOrderLineRow>> GetLineRowsByIdAsync(
        Guid salesOrderId, CancellationToken ct = default);

    // Phase 14A — Edit form: header fields update only. SoNumber +
    // CustomerId + WarehouseId frozen post-create (natural-key shape;
    // changing them would orphan future allocation FKs). Editable:
    // OrderDate, RequestedShipDate, Notes. Status flips go through
    // SetStatusAsync.
    Task<bool> UpdateHeaderAsync(
        SalesOrder entity, Guid? userId, CancellationToken ct = default);

    // Phase 14A — Edit form: replace all lines. Caller (service) is
    // expected to have validated the SO is in 'Draft' state — once
    // an SO is Open, line edits go through allocation reversal in
    // Phase 14B+. Atomic DELETE + INSERT.
    Task ReplaceLinesAsync(
        Guid salesOrderId,
        IReadOnlyList<SalesOrderLine> lines,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 14A — atomic status setter. Idempotent via WHERE
    // Status=@from filter (UPDATE returns 0 rows when already in
    // target). Service composes Submit (Draft→Open) and Cancel
    // (Draft|Open→Cancelled) on top.
    Task<bool> SetStatusAsync(
        Guid salesOrderId,
        string fromStatus,
        string toStatus,
        Guid? userId,
        CancellationToken ct = default);

    // Phase 14A — for SO-YYYYMMDD-NNNN number assignment. Returns
    // count of SOs whose SoNumber LIKE @prefix + '%'.
    Task<int> CountForDatePrefixAsync(
        string datePrefix, CancellationToken ct = default);
}
