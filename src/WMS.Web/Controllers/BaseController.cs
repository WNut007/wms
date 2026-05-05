using Microsoft.AspNetCore.Mvc;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;

namespace WMS.Web.Controllers;

public abstract class BaseController : Controller
{
    private ILogger? _logger;

    protected ILogger Logger =>
        _logger ??= HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(GetType());

    protected ICurrentUser CurrentUser =>
        HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    protected ITenantContext TenantContext =>
        HttpContext.RequestServices.GetRequiredService<ITenantContext>();

    protected IActionResult JsonError(string message, int statusCode = 400) =>
        new JsonResult(new { error = message }) { StatusCode = statusCode };
}
