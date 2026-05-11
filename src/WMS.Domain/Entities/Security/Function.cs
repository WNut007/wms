namespace WMS.Domain.Entities.Security;

// Maps to security.Functions. Catalogue of permissionable surfaces;
// rows are seeded by migrations + Phase 23 added REPORTS.VIEW.
// Code is the stable identifier the BLL gates on ("INVENTORY.STOCK");
// Name is the operator-facing label; Module groups them in the matrix.
public sealed class Function
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Module { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
