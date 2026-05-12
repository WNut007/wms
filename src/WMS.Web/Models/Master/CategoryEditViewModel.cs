using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code read-only. ParentId IS editable — operators
// may want to reorganize the tree. The parent-picker dropdown excludes
// self + descendants (Path prefix-match) to prevent obvious cycles;
// the cycle trigger (Migration_030) is the authoritative backstop.
//
// Path is read-only — trigger-maintained, never set from BLL.
public sealed class CategoryEditViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    // Surfaced for display only on the Edit header / breadcrumb.
    public string? Path { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Display(Name = "Parent")]
    public Guid? ParentId { get; set; }

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Range(0, 9999)]
    [Display(Name = "Display order")]
    public int? DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
