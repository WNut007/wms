using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WMS.Web.Controllers;

// Phase 26 — production-grade error surface. ASP.NET Core's UseStatus-
// CodePagesWithReExecute("/Error/{0}") re-runs the pipeline against
// this controller when a status code in the range 400-599 surfaces
// without a response body. UseExceptionHandler("/Error/500") catches
// thrown exceptions.
//
// All actions render a friendly view with the error code as model. No
// stack traces in production responses; full details land in Serilog
// via the existing log pipeline.
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorController : Controller
{
    [Route("Error/{statusCode:int}")]
    public IActionResult HttpStatusCodeHandler(int statusCode)
    {
        // Preserve the original status code so the response reflects
        // the underlying failure (re-execute resets to 200 by default).
        Response.StatusCode = statusCode;

        return statusCode switch
        {
            403 => View("Forbidden", BuildModel(403, "Forbidden",
                       "You don't have permission to access this resource.")),
            404 => View("NotFound", BuildModel(404, "Not found",
                       "The page you're looking for doesn't exist or was moved.")),
            500 => View("InternalError", BuildModel(500, "Server error",
                       "Something went wrong on our end. Please try again in a moment.")),
            _   => View("Generic", BuildModel(statusCode, $"Error {statusCode}",
                       "An unexpected error occurred.")),
        };
    }

    // Hit when an unhandled exception bubbles up. Logged separately via
    // Serilog through UseSerilogRequestLogging; this action just renders
    // the user-facing page.
    [Route("Error/500")]
    public IActionResult InternalError()
    {
        Response.StatusCode = 500;
        var exception = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        // Don't surface exception details to the operator. Serilog has
        // the full stack already (logged by UseExceptionHandler).
        var path = exception?.Path ?? "(unknown)";
        return View("InternalError", BuildModel(500, "Server error",
            $"Something went wrong while serving {path}. The team has been notified."));
    }

    private static ErrorPageViewModel BuildModel(int code, string title, string message) =>
        new() { Code = code, Title = title, Message = message };
}

public sealed class ErrorPageViewModel
{
    public int Code { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
}
