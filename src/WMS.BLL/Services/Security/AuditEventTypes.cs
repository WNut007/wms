namespace WMS.BLL.Services.Security;

// Phase 24 — closed-set event-type constants emitted by SecurityService.
// Free-form vocabulary at the schema level (the migration explicitly
// avoids a CHECK constraint so future events don't require migrations),
// but the BLL stays disciplined: every audit write goes through one of
// these constants.
//
// EntityType complement constants: 'User' and 'Role' are the only two
// entity types this phase touches. Future phases extend the list.
public static class AuditEventTypes
{
    public const string UserCreated   = "UserCreated";
    public const string UserUpdated   = "UserUpdated";
    public const string UserActivated = "UserActivated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserUnlocked  = "UserUnlocked";

    public const string RolePermissionChanged = "RolePermissionChanged";

    // EntityType values
    public const string EntityUser = "User";
    public const string EntityRole = "Role";
}
