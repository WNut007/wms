using WMS.Domain.Entities.Inventory;

namespace WMS.BLL.Services.Inventory;

// Phase 11A (ADR-013) — Stock Adjustment workflow. Three operations:
//   * CreateAsync — request a new adjustment (always lands as Pending)
//   * ApproveAsync — apply a Pending adjustment to Stock + Movement Log,
//                    in one TransactionScope. Enforces requester != approver.
//   * RejectAsync — terminate a Pending adjustment with a rejection reason.
public interface IAdjustmentService
{
    // Inserts a Pending adjustment. Assigns a tenant-wide unique
    // AdjustmentNumber (ADJ-YYYYMMDD-NNNN). Validates the 6-tuple
    // (FKs caught at INSERT; non-zero delta caught by CHECK).
    // Returns the persisted entity.
    Task<Adjustment> CreateAsync(
        Guid tenantId,
        CreateAdjustmentRequest request,
        Guid currentUserId,
        CancellationToken ct = default);

    // Approves + applies a Pending adjustment in a single TransactionScope:
    //   1. Resolve Stock row by 6-tuple (or rely on UpsertOnHand WHEN NOT
    //      MATCHED to create a new row when AllowCreateNew was set).
    //   2. Stock UpsertOnHandAsync(key, delta, ctx) — writes paired
    //      negative or positive Movement Log row (MovementType=Adjust,
    //      ReferenceType='Adjustment', ReferenceId=adjustment.Id).
    //   3. Repo SetAppliedAsync — flips status + audit, stamps StockId.
    //
    // Rejects with InvalidOperationException when:
    //   * Adjustment doesn't exist
    //   * Adjustment isn't Pending
    //   * approverUserId == adjustment.RequestedBy (separation of duties)
    //   * AllowCreateNew was false AND no Stock row matches the 6-tuple
    //
    // Throws if CK_Stock_OnHand_NonNegative would be violated (operator
    // already consumed enough stock). TX rolls back; nothing is written.
    //
    // Returns true on a successful state change; false if the adjustment
    // was already Applied (idempotent).
    Task<bool> ApproveAsync(
        Guid tenantId,
        Guid adjustmentId,
        Guid approverUserId,
        CancellationToken ct = default);

    // Terminates a Pending adjustment as Rejected. Requires a non-blank
    // reason. Same separation-of-duties rule as ApproveAsync. Idempotent
    // on already-Rejected (returns false).
    Task<bool> RejectAsync(
        Guid tenantId,
        Guid adjustmentId,
        string reason,
        Guid rejecterUserId,
        CancellationToken ct = default);

    Task<Adjustment?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<Adjustment?> GetByNumberAsync(
        Guid tenantId, string adjustmentNumber, CancellationToken ct = default);
}
