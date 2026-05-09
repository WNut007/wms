using WMS.DAL.Common;

namespace WMS.DAL.Repositories.Master;

// Tenant-scoped reads against master.Owners. Bound to a single tenant
// DB connection — get an instance via IOwnerRepositoryFactory.
//
// Phase 9A surface is supplier-dropdown-focused — populates the
// Vendor <select> on /PurchaseOrders/Create + /Edit. Owners are a
// 4-type taxonomy ('Self' / 'Supplier' / 'VMI' / 'Customer3PL'); only
// Supplier + VMI can appear on a PO header (we order FROM them).
public interface IOwnerRepository
{
    // Owners safe to use as the supplier on a PO:
    // OwnerType IN ('Supplier','VMI') AND IsActive = 1.
    // Self + Customer3PL are excluded — Self is the warehouse's own
    // stock (no PO needed); Customer3PL is the OUTBOUND side.
    Task<IReadOnlyList<LookupItem>> GetActiveSuppliersAsync(CancellationToken ct = default);
}
