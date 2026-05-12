using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// View-model for /Categories/Create. Single-page form. Parent picker
// is rendered as a flat dropdown sorted by Path so hierarchical groups
// stay adjacent ('/Skincare/Cream' next to '/Skincare/Lotion').
public sealed class CategoryCreateViewModel
{
    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name must be at most 100 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    // NULL = root category. The controller surfaces an empty <select>
    // option that maps to null at the model binder.
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
