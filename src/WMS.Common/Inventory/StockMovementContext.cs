namespace WMS.Common.Inventory;

// Closed enum — values must match the DB CHECK constraint
// CK_StockMovements_MovementType. Adding a new value here means a
// follow-up migration to widen the CHECK; mismatched values throw at
// INSERT time, by design. Lives in WMS.Common so the (sibling)
// Domain entity StockMovement can reference it without crossing
// abstraction layers — same pattern as StockKey.
public enum StockMovementType
{
    Receive,
    Putaway,
    Pick,
    Adjust,
    Transfer,
    Return,
    Cycle,
}

// Carries the movement-only fields that flow into IStockRepository
// write methods so the repo can compose the matching StockMovements
// INSERT inside the same SQL batch as the Stock UPDATE / MERGE. Per
// ADR-014.
//
// PerformedBy is nullable per CLAUDE.md "Audit Field FK Rules" —
// system actions (background jobs, future imports) write NULL.
// MovementType is the field name (not Type) to avoid shadowing the
// .NET keyword.
//
// ReferenceType + ReferenceId carry provenance. Putaway today passes
// ReferenceType='Putaway' + ReferenceId=null (TD-004 — no header
// table yet); Receive passes ReferenceType='ReceivingLine' +
// ReferenceId=<line guid>.
public sealed record StockMovementContext(
    StockMovementType MovementType,
    Guid? PerformedBy,
    string? ReferenceType = null,
    Guid? ReferenceId = null,
    string? Notes = null);
