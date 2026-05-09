namespace WMS.BLL.Services.Inventory;

// Phase 11A — input shape for IAdjustmentService.CreateAsync. Carries
// the stock 6-tuple + signed delta + reason. AllowCreateNew flips the
// behavior when no Stock row matches the 6-tuple at Apply time:
//   false → service throws on Apply (operator must adjust an existing
//           stock row only; safer default for cycle-count scenarios)
//   true  → UpsertOnHandAsync's WHEN NOT MATCHED branch creates a new
//           Stock row at the key (used for "found" scenarios where
//           inventory exists but isn't yet recorded)
public sealed record CreateAdjustmentRequest(
    Guid LocationId,
    Guid ProductId,
    Guid? LotId,
    Guid? PalletId,
    Guid OwnerId,
    Guid UomId,
    Guid WarehouseId,
    decimal QuantityDelta,
    string Reason,
    string? Notes,
    bool AllowCreateNew = false);
