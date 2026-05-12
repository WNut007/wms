using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Master;

// Edit view-model. Code + Type are read-only:
//   - Code: FK orphan risk (Products reference UoM by Id but admin
//           muscle-memory thinks in Codes).
//   - Type: flipping Type would orphan IsBase invariants (e.g.,
//           change 'Count' → 'Weight' on a base UoM would duplicate
//           the existing Weight base).
//
// IsBase + Name + IsActive are editable. Validator re-runs the
// "one base per Type" check on every Edit submit.
public sealed class UomEditViewModel
{
    public static readonly IReadOnlyList<string> AllTypes =
        UomCreateViewModel.AllTypes;

    public Guid Id { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Display(Name = "Type")]
    public string Type { get; set; } = "Count";

    [Display(Name = "Base unit for this type")]
    public bool IsBase { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
