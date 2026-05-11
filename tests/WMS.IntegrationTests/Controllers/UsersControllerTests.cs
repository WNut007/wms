using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Primitives;
using Moq;
using WMS.BLL.Services.Security;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Security;
using WMS.Domain.Entities.Security;
using WMS.Web.Controllers;
using WMS.Web.ViewModels.Security;

namespace WMS.IntegrationTests.Controllers;

// Phase 24 T1 — UsersController surface. Controller-level plumbing
// covered (route binding, validation flow, redirect targets, TempData
// banner shape). Service invariants tested in SecurityServiceTests.
public class UsersControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorId  = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private record Build(
        UsersController Controller,
        Mock<IUserRepository> UserRepo,
        Mock<IUserRoleRepository> UserRoleRepo,
        Mock<IRoleRepository> RoleRepo,
        Mock<ISecurityService> Security);

    private static Build BuildController()
    {
        var userRepo = new Mock<IUserRepository>();
        var userRoleRepo = new Mock<IUserRoleRepository>();
        var roleRepo = new Mock<IRoleRepository>();
        var security = new Mock<ISecurityService>();

        var userFactory = new Mock<IUserRepositoryFactory>();
        userFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(userRepo.Object);
        var userRoleFactory = new Mock<IUserRoleRepositoryFactory>();
        userRoleFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(userRoleRepo.Object);
        var roleFactory = new Mock<IRoleRepositoryFactory>();
        roleFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(roleRepo.Object);

        // Default-stubbed reads so tests that don't care can ignore them.
        userRepo.Setup(r => r.GetPagedAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<UserListRow>());
        userRepo.Setup(r => r.GetStatusCountsAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserStatusCounts(0, 0, 0, 0));
        roleRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Role>());

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(ActorId);

        var createValidator = new Mock<IValidator<UserCreateViewModel>>();
        createValidator.Setup(v => v.ValidateAsync(
                It.IsAny<UserCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var editValidator = new Mock<IValidator<UserEditViewModel>>();
        editValidator.Setup(v => v.ValidateAsync(
                It.IsAny<UserEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var ctrl = new UsersController(
            userFactory.Object, userRoleFactory.Object, roleFactory.Object,
            security.Object, tenant.Object, currentUser.Object,
            createValidator.Object, editValidator.Object);

        var http = new DefaultHttpContext();
        http.Request.Headers["User-Agent"] = new StringValues("test-agent");
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        ctrl.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());

        return new Build(ctrl, userRepo, userRoleRepo, roleRepo, security);
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var b = BuildController();
        Assert.IsType<ViewResult>(b.Controller.Index());
    }

    [Fact]
    public async Task GetData_ReturnsItemsAndCounts()
    {
        var b = BuildController();
        b.UserRepo.Setup(r => r.GetPagedAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<UserListRow>
            {
                Items = new List<UserListRow>
                {
                    new(Guid.NewGuid(), "a@x.com", "Alice", true, null, 0, null, "ADMIN", DateTime.UtcNow),
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1,
            });
        b.UserRepo.Setup(r => r.GetStatusCountsAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserStatusCounts(1, 1, 0, 0));

        var result = await b.Controller.GetData();
        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.UserRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await b.Controller.Detail(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_Found_ReturnsViewWithViewModel_FlagsCurrentUser()
    {
        var b = BuildController();
        b.UserRepo.Setup(r => r.GetByIdAsync(ActorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = ActorId, Email = "me@x.com", IsActive = true });
        b.UserRoleRepo.Setup(r => r.GetByUserAsync(ActorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserRoleAssignment>());

        var view = Assert.IsType<ViewResult>(await b.Controller.Detail(ActorId));
        var vm = Assert.IsType<UserDetailViewModel>(view.Model);
        Assert.True(vm.IsCurrentUser);
    }

    [Fact]
    public async Task Create_Get_ReturnsViewWithEmptyModel()
    {
        var b = BuildController();
        var view = Assert.IsType<ViewResult>(await b.Controller.Create(CancellationToken.None));
        Assert.IsType<UserCreateViewModel>(view.Model);
    }

    [Fact]
    public async Task Create_Post_HappyPath_RedirectsToDetail()
    {
        var b = BuildController();
        var newId = Guid.NewGuid();
        b.Security.Setup(s => s.CreateUserAsync(
                TenantId, It.IsAny<CreateUserRequest>(), ActorId,
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        var model = new UserCreateViewModel
        {
            Email = "new@x.com", Password = "secret-1234", RoleIds = new List<Guid>(),
        };

        var result = await b.Controller.Create(model);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UsersController.Detail), redirect.ActionName);
        Assert.Equal(newId, redirect.RouteValues!["id"]);
        Assert.Contains("created", b.Controller.TempData["UserMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Create_Post_ServiceThrows_RendersFormWithError()
    {
        var b = BuildController();
        b.Security.Setup(s => s.CreateUserAsync(
                It.IsAny<Guid>(), It.IsAny<CreateUserRequest>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Email collision"));

        var model = new UserCreateViewModel
        {
            Email = "dup@x.com", Password = "secret-1234",
        };

        var result = await b.Controller.Create(model);
        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
    }

    [Fact]
    public async Task ToggleActive_HappyPath_Redirects()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        b.Security.Setup(s => s.ToggleUserActiveAsync(
                TenantId, id, false, ActorId,
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await b.Controller.ToggleActive(id, isActive: false);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UsersController.Detail), redirect.ActionName);
        Assert.Contains("deactivated", b.Controller.TempData["UserMessage"]?.ToString() ?? "");
    }

    [Fact]
    public async Task ToggleActive_ServiceThrows_RedirectsWithError()
    {
        var b = BuildController();
        b.Security.Setup(s => s.ToggleUserActiveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cannot deactivate the last active ADMIN."));

        var result = await b.Controller.ToggleActive(Guid.NewGuid(), isActive: false);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("last active ADMIN", b.Controller.TempData["UserError"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Edit_Post_RouteIdWinsOverBodyId()
    {
        var b = BuildController();
        var routeId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        b.Security.Setup(s => s.UpdateUserAsync(
                It.IsAny<Guid>(), It.IsAny<UpdateUserRequest>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var model = new UserEditViewModel { Id = bodyId, Email = "x@x.com" };

        var result = await b.Controller.Edit(routeId, model);

        b.Security.Verify(s => s.UpdateUserAsync(
            TenantId,
            It.Is<UpdateUserRequest>(r => r.Id == routeId),  // ← route wins
            ActorId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Unlock_HappyPath_Redirects()
    {
        var b = BuildController();
        var result = await b.Controller.Unlock(Guid.NewGuid());
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("unlocked", b.Controller.TempData["UserMessage"]?.ToString() ?? "");
    }
}
