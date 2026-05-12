namespace WMS.Domain.Entities.Master;

// Maps to master.ProductCategories (migration 012). Tenant-scoped per
// ADR-001. Hierarchical — self-FK on ParentId (NO ACTION on delete:
// must reparent or soft-delete children before removing a parent).
//
// Important: BLL must NEVER set Path on insert/update.
// Migration_031 installed an AFTER INSERT, UPDATE trigger that
// maintains Path as the single source of truth ('/Root/Sub/Leaf').
// The repo INSERT/UPDATE SQL deliberately omits the Path column.
//
// CK_ProductCategories_NoSelfParent (migration 029) rejects self-parent.
// trg_ProductCategories_NoCycles (migration 030) rejects longer cycles
// via RAISERROR + ROLLBACK; controller catches + surfaces as friendly
// error on the ParentId field.
//
// BaseEntity.Version unused — master.ProductCategories has no Version
// column.
public sealed class ProductCategory : BaseEntity
{
    // Tenant-wide unique (UQ index on Code).
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    // Self-FK. NULL = root category.
    public Guid? ParentId { get; set; }

    // Trigger-maintained — DO NOT WRITE FROM BLL.
    // Present on the entity for reads; INSERT/UPDATE SQL omits.
    public string? Path { get; set; }

    public string? Description { get; set; }

    // Sort order within a parent. NULL → UI falls back to Name sort.
    public int? DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
