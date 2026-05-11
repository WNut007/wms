using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WMS.Web.Infrastructure;

namespace WMS.IntegrationTests.Infrastructure;

// Phase 26 — fail-fast validator. Production throws; non-Production
// warns to stderr but continues so dev / staging can boot with partial
// config.
public class ConfigurationValidatorTests
{
    private static IConfiguration BuildConfig(string? master, string? tenant) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MasterDb"] = master,
                ["ConnectionStrings:TenantTemplate"] = tenant,
            }!)
            .Build();

    private static IHostEnvironment Env(string name)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.SetupGet(e => e.EnvironmentName).Returns(name);
        return mock.Object;
    }

    [Fact]
    public void Production_BothMissing_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(BuildConfig(null, null), Env("Production")));
        Assert.Contains("ConnectionStrings:MasterDb", ex.Message);
        Assert.Contains("ConnectionStrings:TenantTemplate", ex.Message);
    }

    [Fact]
    public void Production_MasterMissing_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(BuildConfig(null, "Server=t;Database={0}"), Env("Production")));
        Assert.Contains("ConnectionStrings:MasterDb", ex.Message);
        Assert.DoesNotContain("TenantTemplate", ex.Message);
    }

    [Theory]
    [InlineData("Server=...;Database=WMS_Master", "Server=...;Database={0}")]
    public void Production_BothPresent_DoesNotThrow(string master, string tenant)
    {
        ConfigurationValidator.Validate(BuildConfig(master, tenant), Env("Production"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void NonProduction_AllowsMissingKeys(string envName)
    {
        // Should not throw — only warn. Capture stderr to verify warning was emitted.
        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            ConfigurationValidator.Validate(BuildConfig(null, null), Env(envName));
        }
        finally
        {
            Console.SetError(originalErr);
        }
        Assert.Contains("WARNING", sw.ToString());
        Assert.Contains(envName, sw.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_WhitespaceConnectionString_Throws(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(BuildConfig(value, value), Env("Production")));
    }
}
