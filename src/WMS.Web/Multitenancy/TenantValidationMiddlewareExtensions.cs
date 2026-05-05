namespace WMS.Web.Multitenancy;

public static class TenantValidationMiddlewareExtensions
{
    // Position between UseAuthentication and UseAuthorization — the
    // middleware reads User.Claims (populated by UseAuthentication) and
    // may short-circuit a request before it ever reaches an authorize
    // policy.
    public static IApplicationBuilder UseTenantValidation(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantValidationMiddleware>();
}
