using Microsoft.AspNetCore.Mvc;

namespace WMS.Web.Controllers;

public abstract class BaseController : Controller
{
    private ILogger? _logger;

    protected ILogger Logger =>
        _logger ??= HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(GetType());

    // TODO: read from HttpContext.Items["TenantId"] once
    // TenantResolutionMiddleware is in place.
    protected virtual Guid GetCurrentTenantId() =>
        throw new InvalidOperationException(
            "Tenant resolution not yet wired. Implement TenantResolutionMiddleware first.");

    protected IActionResult JsonError(string message, int statusCode = 400) =>
        new JsonResult(new { error = message }) { StatusCode = statusCode };
}
