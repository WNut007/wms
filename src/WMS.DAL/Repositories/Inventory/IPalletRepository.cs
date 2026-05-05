namespace WMS.DAL.Repositories.Inventory;

// Tenant-scoped writes against inventory.Pallets. Today the only
// primitive is GetOrCreate; lifecycle mutations (Empty / Damaged /
// Retired) come with the pallet-management chunk.
public interface IPalletRepository
{
    // Returns the existing Pallet.Id for palletNumber; creates the row
    // first if it doesn't exist. Idempotent and race-safe — the repo
    // serialises concurrent callers via MERGE WITH (HOLDLOCK) on the
    // unique PalletNumber index.
    Task<Guid> GetOrCreateAsync(
        string palletNumber,
        Guid? userId,
        CancellationToken ct = default);
}
