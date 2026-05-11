using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WMS.BLL.Services.Email;

namespace WMS.Web.Infrastructure.HealthChecks;

// Phase 30A — config-shape probe for the email service. Reports:
//   - Healthy   = provider configured, ready to send OR TestMode is on
//   - Degraded  = TestMode is on (intentional safe-default in dev; not
//                  an error, but worth surfacing in production
//                  readiness checks so an operator notices before they
//                  expect real emails to flow)
//   - Unhealthy = required credentials missing AND TestMode is off
//                  (production deploy without env vars wired up — this
//                  would throw on first real send anyway; better to
//                  surface at /healthz/ready)
//
// Does NOT attempt to open a real SMTP connection — that would burn
// Gmail quota on every health probe and add 1-2s latency to /healthz.
// The "can we actually send mail" check happens at first real send.
public sealed class EmailServiceHealthCheck : IHealthCheck
{
    private readonly EmailOptions _options;

    public EmailServiceHealthCheck(IOptions<EmailOptions> options) =>
        _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_options.TestMode)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Email service in TestMode — outbound emails are LOGGED, not sent. " +
                "Set Email:TestMode=false (typically via env var) to enable real sending.",
                data: new Dictionary<string, object>
                {
                    ["testMode"] = true,
                    ["provider"] = _options.Provider,
                }));
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.Username))    missing.Add("Email:Username");
        if (string.IsNullOrWhiteSpace(_options.Password))    missing.Add("Email:Password");
        if (string.IsNullOrWhiteSpace(_options.FromAddress)) missing.Add("Email:FromAddress");

        if (missing.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Email service configured for production (TestMode=false) but required keys missing: {string.Join(", ", missing)}.",
                data: new Dictionary<string, object>
                {
                    ["missing"] = string.Join(", ", missing),
                    ["provider"] = _options.Provider,
                }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Email service configured ({_options.Provider} → {_options.SmtpHost}:{_options.SmtpPort}). " +
            "Live send not probed — first real send confirms connectivity.",
            data: new Dictionary<string, object>
            {
                ["provider"] = _options.Provider,
                ["host"] = _options.SmtpHost,
                ["port"] = _options.SmtpPort,
            }));
    }
}
