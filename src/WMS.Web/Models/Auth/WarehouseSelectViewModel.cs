using WMS.Common.Auth;

namespace WMS.Web.Models.Auth;

// Backs the Step 3 picker view + the corresponding POST. Same render /
// re-validate pattern as TenantSelectViewModel: Warehouses is populated
// server-side, only SelectedWarehouseId binds from the form, and the
// chosen Id is checked against the tenant's *real* allowed list before
// the cookie is re-issued.
public class WarehouseSelectViewModel
{
    public IReadOnlyList<WarehouseInfo> Warehouses { get; set; } = Array.Empty<WarehouseInfo>();

    public Guid? SelectedWarehouseId { get; set; }
}
