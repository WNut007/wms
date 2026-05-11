namespace WMS.BLL.Services.Security;

// Phase 25 — basic password policy (D1).
//   - Minimum 8 characters
//   - At least one uppercase letter
//   - At least one lowercase letter
//   - At least one digit
//   - No symbol requirement
//
// Per-tenant config / history / expiration are deferred (TD-057, TD-058,
// TD-059). The static checker keeps the rule set in ONE place — both the
// FluentValidation client-side path AND the SecurityService server-side
// guard call into Validate.
//
// Returns the first failure message so the operator gets a focused
// error per attempt (vs aggregating all failures and overwhelming them).
public static class PasswordPolicy
{
    public const int MinLength = 8;

    // null on success; reason string on failure.
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Password is required.";
        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter.";
        if (!password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter.";
        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";
        return null;
    }

    public static void ThrowIfInvalid(string? password)
    {
        var error = Validate(password);
        if (error is not null)
            throw new ArgumentException(error, nameof(password));
    }
}
