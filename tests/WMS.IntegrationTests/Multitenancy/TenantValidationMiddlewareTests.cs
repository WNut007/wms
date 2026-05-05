using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.Web.Multitenancy;

namespace WMS.IntegrationTests.Multitenancy;

// Lives in WMS.IntegrationTests because WMS.Web is net8.0-windows and the
// unit-test project is plain net8.0 — TFM mismatch prevents a direct
// reference. Despite the project name these are unit-style: a hand-built
// DefaultHttpContext, no TestServer, no real auth handler.
public class TenantValidationMiddlewareTests
{
    [Fact]
    public async Task AnonymousRequest_PassesThrough_WithoutHittingStatusReader()
    {
        var statusReader = new Mock<ITenantStatusReader>(MockBehavior.Strict);
        var nextCalled = false;
        var sut = NewMiddleware(statusReader.Object, _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = NewContext(authenticated: false);
        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        statusReader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HealthPath_PassesThrough_EvenWhenAuthenticated()
    {
        var statusReader = new Mock<ITenantStatusReader>(MockBehavior.Strict);
        var nextCalled = false;
        var sut = NewMiddleware(statusReader.Object, _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = NewContext(authenticated: true, tenantId: Guid.NewGuid(), path: "/health");
        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        statusReader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthPath_PassesThrough_EvenWhenAuthenticated()
    {
        var statusReader = new Mock<ITenantStatusReader>(MockBehavior.Strict);
        var nextCalled = false;
        var sut = NewMiddleware(statusReader.Object, _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = NewContext(authenticated: true, tenantId: Guid.NewGuid(), path: "/Auth/Logout");
        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        statusReader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticatedWithoutTenantClaim_PassesThrough()
    {
        // Mid-flow between login step 1 and step 2 — controllers handle
        // the partial-session case, middleware mustn't block it.
        var statusReader = new Mock<ITenantStatusReader>(MockBehavior.Strict);
        var nextCalled = false;
        var sut = NewMiddleware(statusReader.Object, _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = NewContext(authenticated: true, tenantId: null);
        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        statusReader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActiveTenant_PassesThrough()
    {
        var tenantId = Guid.NewGuid();
        var statusReader = new Mock<ITenantStatusReader>();
        statusReader.Setup(r => r.IsActiveAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var nextCalled = false;
        var sut = NewMiddleware(statusReader.Object, _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = NewContext(authenticated: true, tenantId: tenantId);
        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        statusReader.Verify(r => r.IsActiveAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InactiveTenant_SignsOutAndRedirects_WithoutCallingNext()
    {
        var tenantId = Guid.NewGuid();
        var statusReader = new Mock<ITenantStatusReader>();
        statusReader.Setup(r => r.IsActiveAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var authMock = new Mock<IAuthenticationService>();
        authMock
            .Setup(s => s.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var nextCalled = false;
        var sut = NewMiddleware(statusReader.Object, _ => { nextCalled = true; return Task.CompletedTask; });

        var services = new ServiceCollection();
        services.AddSingleton(authMock.Object);
        var ctx = NewContext(
            authenticated: true,
            tenantId: tenantId,
            requestServices: services.BuildServiceProvider());

        await sut.InvokeAsync(ctx);

        Assert.False(nextCalled);
        Assert.Equal("/Auth/Login", ctx.Response.Headers.Location.ToString());
        authMock.Verify(s => s.SignOutAsync(
            It.IsAny<HttpContext>(),
            It.IsAny<string>(),
            It.IsAny<AuthenticationProperties>()), Times.Once);
    }

    private static TenantValidationMiddleware NewMiddleware(
        ITenantStatusReader reader,
        RequestDelegate next) =>
        new(next, reader, NullLogger<TenantValidationMiddleware>.Instance);

    private static DefaultHttpContext NewContext(
        bool authenticated,
        Guid? tenantId = null,
        string path = "/",
        IServiceProvider? requestServices = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.RequestServices = requestServices ?? new ServiceCollection().BuildServiceProvider();

        if (!authenticated)
            return ctx;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        if (tenantId is not null)
            claims.Add(new Claim(WmsClaimTypes.TenantId, tenantId.Value.ToString()));

        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
        return ctx;
    }
}
