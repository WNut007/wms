using FluentValidation;
using WMS.Web.Models.Master;

namespace WMS.Web.Validators.Master;

// Edit validator. No Code uniqueness check (read-only on Edit).
// Cycle prevention is enforced by Migration_030's trigger — controller
// catches the RAISERROR and surfaces a friendly ParentId error.
//
// The parent picker also pre-filters self + descendants (Path prefix-
// match in GetPickerItemsAsync) so the trigger backstop only fires on
// truly pathological inputs (form tampered, race with another edit).
public sealed class CategoryEditValidator : AbstractValidator<CategoryEditViewModel>
{
    public CategoryEditValidator()
    {
        // Defense-in-depth on the trivial self-parent case. The DB
        // CHECK constraint (Migration_029) is the authoritative backstop,
        // but catching it here avoids a 500-style SqlException round-trip.
        RuleFor(x => x.ParentId)
            .Must((vm, parentId) => parentId is null || parentId != vm.Id)
            .WithMessage("Parent cannot be the category itself.");
    }
}
