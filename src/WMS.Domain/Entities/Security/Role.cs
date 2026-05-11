namespace WMS.Domain.Entities.Security;

// Maps to security.Roles in the tenant DB. Built-in roles
// (ADMIN/PICKER/PACKER/MANAGER) have IsSystemRole=true and the BLL
// refuses delete/code-change/active-toggle on those rows. Custom
// tenant-defined roles default to IsSystemRole=false.
public sealed class Role : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;
}
