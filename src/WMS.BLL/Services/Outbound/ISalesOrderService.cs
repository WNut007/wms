using WMS.DAL.Repositories.Outbound;

namespace WMS.BLL.Services.Outbound;

// Phase 14A — outbound sales-order workflow (MVP foundation).
// State flow:
//   Draft → Open                      (terminal happy)
//   Draft|Open → Cancelled            (terminal failure)
//
// Allocation/Pick/Pack/Ship transitions land in Phase 14B+ as new
// methods on this interface. Don't pre-stage their signatures here;
// schema decisions belong to those phases.
public interface ISalesOrderService
{
    // Validates the request shape, assigns SO-YYYYMMDD-NNNN, persists
    // header (Status='Draft') + lines, returns the freshly-fetched
    // Detail. Throws ArgumentException for shape errors.
    Task<SalesOrderDetail> CreateAsync(
        Guid tenantId,
        CreateSalesOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    Task<SalesOrderDetail?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<SalesOrderDetail?> GetByNumberAsync(
        Guid tenantId, string soNumber, CancellationToken ct = default);

    // Edit form. Header always editable at non-terminal status; lines
    // replacement gated on Status='Draft'. Throws InvalidOperation-
    // Exception when SO is missing, when ReplaceLines=true on a non-
    // Draft SO, or when the post-update header write fails.
    Task<SalesOrderDetail> UpdateAsync(
        Guid tenantId,
        Guid salesOrderId,
        UpdateSalesOrderRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);

    // Draft → Open. Idempotent (already-Open returns false). Throws
    // InvalidOperationException for non-Draft non-Open source states
    // (Cancelled or any future intermediate state).
    Task<bool> SubmitAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid? currentUserId,
        CancellationToken ct = default);

    // Either Draft or Open → Cancelled. Idempotent (already-Cancelled
    // returns false). MVP: no reason field — operator can edit Notes
    // first if context needs to be recorded. Phase 14B+ will likely
    // add a CancelReason column once allocation reversal lands.
    Task<bool> CancelAsync(
        Guid tenantId,
        Guid salesOrderId,
        Guid? currentUserId,
        CancellationToken ct = default);
}
