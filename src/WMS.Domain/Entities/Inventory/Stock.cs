namespace WMS.Domain.Entities.Inventory;

// Maps to inventory.Stock — the per-(Location, Product, Lot, Pallet,
// Owner, UoM) quantity tuple. Lot and Pallet are nullable for products
// that don't track them.
//
// Uniqueness on the 6-tuple is enforced at the service layer because
// SQL Server's composite UNIQUE doesn't have NULL-safe semantics. The
// composite IX_Stock_Key gives lookups an index seek; concurrency on
// updates is guarded by the inherited Version column
// (UPDATE ... WHERE Version = @v, increment on success).
public sealed class Stock : BaseEntity
{
    public Guid LocationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? PalletId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UomId { get; set; }

    public decimal QuantityOnHand { get; set; }
    public decimal QuantityAllocated { get; set; }

    public DateTime? LastMovementAt { get; set; }

    // Convenience read — never persisted. CHECK constraints in the DB
    // already guarantee Allocated <= OnHand, so this is always >= 0.
    public decimal QuantityAvailable => QuantityOnHand - QuantityAllocated;
}
