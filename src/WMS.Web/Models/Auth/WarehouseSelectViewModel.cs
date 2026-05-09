using WMS.Common.Auth;

namespace WMS.Web.Models.Auth;

// Backs the Step 3 picker view + the corresponding POST. Same render /
// re-validate pattern as TenantSelectViewModel: the picker data is
// populated server-side, only SelectedWarehouseId binds from the form,
// and the chosen Id is checked against the tenant's *real* allowed
// list before the cookie is re-issued.
//
// Phase 8.5 — picker upgraded with search + region grouping. The view
// consumes Items (richer projection); the POST handler still only
// cares about SelectedWarehouseId.
public class WarehouseSelectViewModel
{
    // Phase 8.5 picker projection — Code, Name, Address, Type.
    public IReadOnlyList<WarehousePickerItem> Items { get; set; } =
        Array.Empty<WarehousePickerItem>();

    // Optional tenant code shown in the picker header (e.g. "DEMO").
    public string TenantCode { get; set; } = "";

    public Guid? SelectedWarehouseId { get; set; }
}
