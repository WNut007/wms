namespace WMS.Domain.Entities.Security;

// Maps to security.UserRoles — the (User × Role) assignment matrix.
// ValidFrom/ValidTo support time-bounded role validity; NULL on either
// means "effective immediately" / "no expiry". Phase 24 v1 assigns
// roles with both NULL (effective forever); time-bounded UI is a TD.
//
// AssignedBy is an unconstrained Guid soft-FK (the migration's comment
// explains: avoids circular CASCADE so deleting an admin doesn't tear
// down every assignment they made).
public sealed class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public Guid? AssignedBy { get; set; }
}
