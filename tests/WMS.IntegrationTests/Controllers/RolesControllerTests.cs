using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.BLL.Services.Security;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Security;
using WMS.Domain.Entities.Security;
using WMS.Web.Controllers;
using WMS.Web.ViewModels.Security;

namespace WMS.IntegrationTests.Controllers;

public class RolesControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorId  = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private record Build(
        RolesController Controller,
        Mock<IRoleRepository> RoleRepo,
        Mock<ISecurityService> Security);

    private static Build BuildController()
    {
        var roleRepo = new Mock<IRoleRepository>();
        var roleFactory = new Mock<IRoleRepositoryFactory>();
        roleFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(roleRepo.Object);

        var functionRepo = new Mock<IFunctionRepository>();
        var functionFactory = new Mock<IFunctionRepositoryFactory>();
        functionFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(functionRepo.Object);

        var security = new Mock<ISecurityService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(ActorId);

        roleRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RoleListRow>());
        roleRepo.Setup(r => r.GetPermissionsForRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePermissionRow>());

        var ctrl = new RolesController(
            roleFactory.Object, functionFactory.Object, security.Object,
            tenant.Object, currentUser.Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        return new Build(ctrl, roleRepo, security);
    }

    [Fact]
    public async Task Index_ReturnsViewWithList()
    {
        var b = BuildController();
        b.RoleRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleListRow>
            {
                new(Guid.NewGuid(), "ADMIN", "Admin", null, true, true, 1, 30, DateTime.UtcNow),
                new(Guid.NewGuid(), "PICKER", "Picker", null, true, true, 0, 4, DateTime.UtcNow),
            });

        var view = Assert.IsType<ViewResult>(await b.Controller.Index());
        var model = Assert.IsAssignableFrom<IReadOnlyList<RoleListRow>>(view.Model);
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.RoleRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var result = await b.Controller.Detail(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_GroupsPermissionsByModule()
    {
        var b = BuildController();
        var roleId = Guid.NewGuid();
        b.RoleRepo.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = roleId, Code = "MANAGER", Name = "Manager" });
        b.RoleRepo.Setup(r => r.GetPermissionsForRoleAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePermissionRow>
            {
                new(Guid.NewGuid(), "INVENTORY.STOCK", "Stock", "Inventory", 1, true, false, false, false, false),
                new(Guid.NewGuid(), "INVENTORY.LOTS",  "Lots",  "Inventory", 2, true, false, false, false, false),
                new(Guid.NewGuid(), "MASTER.PRODUCTS", "Products", "Master", 2, true, false, false, false, false),
            });

        var view = Assert.IsType<ViewResult>(await b.Controller.Detail(roleId));
        var vm = Assert.IsType<RoleDetailViewModel>(view.Model);
        Assert.Equal(2, vm.Groups.Count);   // Inventory + Master
        Assert.Equal("Inventory", vm.Groups[0].Module);
        Assert.Equal(2, vm.Groups[0].Rows.Count);
    }

    [Fact]
    public async Task SetPermission_HappyPath_ReturnsOk()
    {
        var b = BuildController();
        b.Security.Setup(s => s.SetPermissionAsync(
                TenantId, It.IsAny<SetPermissionRequest>(), ActorId,
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var body = new SetPermissionPostBody(
            RoleId: Guid.NewGuid(), FunctionId: Guid.NewGuid(),
            CanView: true, CanAdd: false, CanEdit: false, CanDelete: false, CanApprove: false);

        var result = await b.Controller.SetPermission(body);
        Assert.IsType<JsonResult>(result);
    }

    [Fact]
    public async Task SetPermission_ServiceThrows_ReturnsBadRequest()
    {
        var b = BuildController();
        b.Security.Setup(s => s.SetPermissionAsync(
                It.IsAny<Guid>(), It.IsAny<SetPermissionRequest>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System role — permissions are baseline"));

        var body = new SetPermissionPostBody(
            RoleId: Guid.NewGuid(), FunctionId: Guid.NewGuid(),
            CanView: true, CanAdd: false, CanEdit: false, CanDelete: false, CanApprove: false);

        var result = await b.Controller.SetPermission(body);
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
