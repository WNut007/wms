using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WMS.BLL.Services.Email;
using WMS.Web.Infrastructure.HealthChecks;

namespace WMS.IntegrationTests.Infrastructure;

// Phase 30A — verify the health check classifies each config shape
// correctly. Critical for /healthz/ready signalling readiness to a
// load balancer or to the operator inspecting the JSON envelope.
public class EmailServiceHealthCheckTests
{
    private static EmailServiceHealthCheck BuildCheck(EmailOptions opts) =>
        new(Options.Create(opts));

    [Fact]
    public async Task TestModeTrue_ReportsDegraded()
    {
        var sut = BuildCheck(new EmailOptions { TestMode = true });
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("TestMode", result.Description);
    }

    [Fact]
    public async Task TestModeFalse_MissingCredentials_ReportsUnhealthy()
    {
        var sut = BuildCheck(new EmailOptions { TestMode = false });
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Email:Username", result.Description);
        Assert.Contains("Email:Password", result.Description);
        Assert.Contains("Email:FromAddress", result.Description);
    }

    [Fact]
    public async Task TestModeFalse_PartialCredentials_ReportsUnhealthy()
    {
        var sut = BuildCheck(new EmailOptions
        {
            TestMode = false,
            Username = "test@gmail.com",
            // Password missing
            FromAddress = "test@gmail.com",
        });
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Email:Password", result.Description);
    }

    [Fact]
    public async Task TestModeFalse_AllCredentials_ReportsHealthy()
    {
        var sut = BuildCheck(new EmailOptions
        {
            TestMode = false,
            Username = "test@gmail.com",
            Password = "app-password-16ch",
            FromAddress = "test@gmail.com",
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 587,
        });
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("smtp.gmail.com", result.Description);
    }
}
