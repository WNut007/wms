using WMS.Common.Auth;

namespace WMS.Web.Models.Auth;

// Backs the Step 2 picker view + the corresponding POST.
//
// On render: Tenants is the user's allowed list, SelectedTenantId is null.
// On POST:   only SelectedTenantId is bound; Tenants[] is empty and gets
//            re-fetched server-side before re-rendering on validation
//            failure (and to authorise the chosen Id against the user's
//            real list — never trust the form value alone).
public class TenantSelectViewModel
{
    public IReadOnlyList<UserTenantInfo> Tenants { get; set; } = Array.Empty<UserTenantInfo>();

    public Guid? SelectedTenantId { get; set; }
}
