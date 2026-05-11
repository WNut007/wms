namespace WMS.Web.Infrastructure;

// Phase 26 — production security headers. Values configurable via the
// SecurityHeaders section in appsettings.{Environment}.json so the
// CSP can be tuned per deployment without code changes.
//
// Defaults (when a value is empty / missing in config) are NOT applied
// — the middleware skips emitting that header. This lets a deployment
// opt out of, say, the Permissions-Policy header by clearing it in
// config, without rewriting the middleware.
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        SecurityHeadersOptions options)
    {
        _next = next;
        _options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting fires just before headers are flushed. Adding here
        // ensures the headers stick even when downstream middleware
        // composes its own response (e.g. status code pages).
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            TrySet(h, "X-Frame-Options", _options.FrameOptions);
            TrySet(h, "X-Content-Type-Options", _options.ContentTypeOptions);
            TrySet(h, "Referrer-Policy", _options.ReferrerPolicy);
            TrySet(h, "Content-Security-Policy", _options.ContentSecurityPolicy);
            TrySet(h, "Permissions-Policy", _options.PermissionsPolicy);

            // Remove server fingerprint header — IIS / Kestrel both
            // emit Server: by default. Silent hardening (security
            // through obscurity isn't a defense, but no need to
            // advertise the stack either).
            h.Remove("Server");
            return Task.CompletedTask;
        });

        return _next(context);
    }

    private static void TrySet(IHeaderDictionary headers, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        // Don't overwrite if downstream code already set it (controllers
        // may override CSP for specific endpoints — pack-video player
        // could be one).
        if (headers.ContainsKey(name)) return;
        headers[name] = value;
    }
}

public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    public string? FrameOptions { get; set; } = "DENY";
    public string? ContentTypeOptions { get; set; } = "nosniff";
    public string? ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    // Default CSP — permissive enough for the WMS surface (Bootstrap +
    // Alpine + ApexCharts + Tabler icons). Production should override
    // via appsettings.Production.json with a tighter / nonce-based
    // policy if XSS surfaces become a concern.
    public string? ContentSecurityPolicy { get; set; }

    public string? PermissionsPolicy { get; set; } =
        "camera=(self), microphone=(), geolocation=(), payment=()";
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions options) =>
        app.UseMiddleware<SecurityHeadersMiddleware>(options);
}
