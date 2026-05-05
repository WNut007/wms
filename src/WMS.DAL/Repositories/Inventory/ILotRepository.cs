namespace WMS.DAL.Repositories.Inventory;

// Tenant-scoped writes against inventory.Lots. Today the only primitive
// is GetOrCreate (the receiving primitive's Lot side); status mutation
// — Quarantine, Hold, Expired transitions — comes when the QC chunk
// arrives.
public interface ILotRepository
{
    // Returns the existing Lot.Id for (productId, lotNumber); creates
    // the row first if it doesn't exist. Idempotent and race-safe — the
    // repo serialises concurrent callers via MERGE WITH (HOLDLOCK) on
    // the UX_Lots_Product_Number index range.
    //
    // ReceivedDate / ExpiryDate are only consulted on insert. Subsequent
    // calls with different dates are no-ops — the lot keeps the dates it
    // was originally recorded with (a lot is a physical batch; its
    // received-on date doesn't change just because someone receives more
    // of the same lot later).
    Task<Guid> GetOrCreateAsync(
        Guid productId,
        string lotNumber,
        DateOnly receivedDate,
        DateOnly? expiryDate,
        Guid? userId,
        CancellationToken ct = default);
}
