using FluentValidation;

namespace WMS.Web.ViewModels.Security;

// Phase 24 — server-side cross-field rules. Email + password basics
// duplicated from SecurityService.Validate* on purpose: validator gives
// ASP.NET ModelState the precise field name for inline error display,
// service guard catches calls that bypass the controller (e.g. tests).

public sealed class UserCreateValidator : AbstractValidator<UserCreateViewModel>
{
    public UserCreateValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(100);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100);

        RuleFor(x => x.FullName).MaximumLength(200);

        RuleFor(x => x.ApprovalLimit)
            .GreaterThanOrEqualTo(0)
            .When(x => x.ApprovalLimit.HasValue);
    }
}

public sealed class UserEditValidator : AbstractValidator<UserEditViewModel>
{
    public UserEditValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(100);

        RuleFor(x => x.FullName).MaximumLength(200);

        RuleFor(x => x.ApprovalLimit)
            .GreaterThanOrEqualTo(0)
            .When(x => x.ApprovalLimit.HasValue);
    }
}
