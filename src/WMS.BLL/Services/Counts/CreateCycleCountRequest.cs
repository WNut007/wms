namespace WMS.BLL.Services.Counts;

// Phase 12 — input shape for ICycleCountService.CreateAsync.
// LocationFilter null = whole-warehouse scope; set = single Location.
public sealed record CreateCycleCountRequest(
    Guid WarehouseId,
    Guid? LocationFilter,
    string? Notes);

// Per-line save payload for SaveCountedQuantitiesAsync. CountedQuantity
// nullable (operator can re-clear an entry); LineStatus drives whether
// the line is treated as counted/skipped/pending on apply.
public sealed record CountLineUpdate(
    Guid LineId,
    decimal? CountedQuantity,
    string LineStatus,
    string? Notes);
