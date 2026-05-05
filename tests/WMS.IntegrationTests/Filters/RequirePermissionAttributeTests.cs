using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.Web.Filters;

namespace WMS.IntegrationTests.Filters;

// In WMS.IntegrationTests because the filter under test lives in
// WMS.Web (net8.0-windows), but the tests themselves are unit-style:
// hand-built AuthorizationFilterContext, no TestServer.
public class RequirePermissionAttributeTests
{
    private const string FunctionCode = "INVENTORY.STOCK";
    private const string Action = PermissionAction.View;

    [Fact]
    public async Task UnauthenticatedRequest_SetsChallengeResult()
    {
        var permService = new Mock<IPermissionService>(MockBehavior.Strict);
        var ctx = NewContext(authenticated: false, requestServices: BuildServices(permService.Object));

        var sut = new RequirePermissionAttribute(FunctionCode, Action);
        await sut.OnAuthorizationAsync(ctx);

        Assert.IsType<ChallengeResult>(ctx.Result);
        permService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticatedWithoutClaims_SetsForbidResult()
    {
        var permService = new Mock<IPermissionService>(MockBehavior.Strict);
        var ctx = NewContext(
            authenticated: true,
            userId: null,
            tenantId: null,
            requestServices: BuildServices(permService.Object));

        var sut = new RequirePermissionAttribute(FunctionCode, Action);
        await sut.OnAuthorizationAsync(ctx);

        Assert.IsType<ForbidResult>(ctx.Result);
        permService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PermissionGranted_LeavesResultNull()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var permService = new Mock<IPermissionService>();
        permService.Setup(s => s.HasPermissionAsync(
                userId, tenantId, FunctionCode, Action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctx = NewContext(
            authenticated: true,
            userId: userId,
            tenantId: tenantId,
            requestServices: BuildServices(permService.Object));

        var sut = new RequirePermissionAttribute(FunctionCode, Action);
        await sut.OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task PermissionDenied_SetsForbidResult()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var permService = new Mock<IPermissionService>();
        permService.Setup(s => s.HasPermissionAsync(
                userId, tenantId, FunctionCode, Action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ctx = NewContext(
            authenticated: true,
            userId: userId,
            tenantId: tenantId,
            requestServices: BuildServices(permService.Object));

        var sut = new RequirePermissionAttribute(FunctionCode, Action);
        await sut.OnAuthorizationAsync(ctx);

        Assert.IsType<ForbidResult>(ctx.Result);
    }

    private static IServiceProvider BuildServices(IPermissionService permService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(permService);
        return services.BuildServiceProvider();
    }

    private static AuthorizationFilterContext NewContext(
        bool authenticated,
        IServiceProvider requestServices,
        Guid? userId = null,
        Guid? tenantId = null)
    {
        var http = new DefaultHttpContext { RequestServices = requestServices };

        if (authenticated)
        {
            var claims = new List<Claim>();
            if (userId is not null)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
            if (tenantId is not null)
                claims.Add(new Claim(WmsClaimTypes.TenantId, tenantId.Value.ToString()));
            http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
        }

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }
}
