namespace WMS.BLL.Services.Inbound;

// Continuous Putaway primitive — moves stock from a source 6-tuple
// to a destination location, preserving Product / Lot / Pallet /
// Owner / UoM. Suggestion logic (templates / scoring / ADR-004
// hybrid) sits on top of this and lands in a separate chunk.
public interface IPutawayService
{
    Task<PutawayResult> PutawayStockAsync(
        Guid tenantId,
        PutawayRequest request,
        Guid? currentUserId,
        CancellationToken ct = default);
}
