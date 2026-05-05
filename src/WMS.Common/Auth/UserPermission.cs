namespace WMS.Common.Auth;

// One (Function, Action) the user is allowed to perform — already
// aggregated across every role they hold. If any role grants Stock.View
// the user's list contains UserPermission("INVENTORY.STOCK", "View"),
// regardless of other roles.
public sealed record UserPermission(string FunctionCode, string Action);
