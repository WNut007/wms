namespace WMS.Common.Auth;

// Action half of a (Function, Action) permission. Names mirror the
// Boolean flag columns on security.RoleFunctionPermissions
// (CanView / CanAdd / CanEdit / CanDelete / CanApprove). Use the
// constants when calling RequirePermission / HasPermissionAsync —
// "View" vs "view" vs "VIEW" would all silently miss.
public static class PermissionAction
{
    public const string View = "View";
    public const string Add = "Add";
    public const string Edit = "Edit";
    public const string Delete = "Delete";
    public const string Approve = "Approve";
}
