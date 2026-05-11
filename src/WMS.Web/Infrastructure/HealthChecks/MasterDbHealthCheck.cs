using Microsoft.Extensions.Diagnostics.HealthChecks;
using WMS.Common.Multitenancy;

namespace WMS.Web.Infrastructure.HealthChecks;

// Phase 26 — Master DB connectivity probe for /healthz/ready.
// Opens a connection from IMasterConnectionFactory and runs SELECT 1.
// Round-trip should be <100ms on a healthy instance; load balancer
// readiness probe should give it ~3-5s before marking unhealthy.
//
// Doesn't probe tenant DBs — there are N of them and any one could be
// temporarily offline without the app itself being unready. Tenant-
// specific health is observable via Application Insights / Hangfire
// failures + audit logs, not via the global readiness probe.
public sealed class MasterDbHealthCheck : IHealthCheck
{
    private readonly IMasterConnectionFactory _masterFactory;

    public MasterDbHealthCheck(IMasterConnectionFactory masterFactory) =>
        _masterFactory = masterFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _masterFactory.CreateConnection();
            // IDbConnection itself is sync-open; explicit cast covers
            // both ADO.NET versions cleanly.
            if (conn is System.Data.Common.DbConnection asyncConn)
            {
                await asyncConn.OpenAsync(cancellationToken);
            }
            else
            {
                conn.Open();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 5;   // seconds
            var result = cmd.ExecuteScalar();
            return result is int i && i == 1
                ? HealthCheckResult.Healthy("Master DB reachable.")
                : HealthCheckResult.Degraded("Master DB responded unexpectedly.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Master DB unreachable.",
                exception: ex,
                data: new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }
}
