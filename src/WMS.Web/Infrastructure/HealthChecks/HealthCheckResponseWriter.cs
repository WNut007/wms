using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WMS.Web.Infrastructure;

// Phase 26 — JSON response writer for /healthz endpoints. Default
// MapHealthChecks output is plain "Healthy" / "Unhealthy" text; this
// emits structured JSON so the load balancer / monitoring tool can
// drill into per-check status.
//
// Response shape (matches AspNetCore.Diagnostics.HealthChecks UI):
//   {
//     "status": "Healthy",
//     "totalDuration": "00:00:00.0123456",
//     "entries": {
//       "master-db": {
//         "status": "Healthy",
//         "description": "Master DB reachable.",
//         "duration": "00:00:00.0089765"
//       }
//     }
//   }
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static Task WriteJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            entries = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    description = kvp.Value.Description,
                    duration = kvp.Value.Duration,
                }),
        };

        return JsonSerializer.SerializeAsync(
            context.Response.Body, payload, Options);
    }
}
