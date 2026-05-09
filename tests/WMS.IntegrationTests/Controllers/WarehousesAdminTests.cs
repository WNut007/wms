using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;
using WMS.Web.Controllers;
using WMS.Web.Models.Master;
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Controllers;

// Phase 7F admin-side tests for WarehousesController.Admin.cs partial.
// Simplest of the three admin surfaces — no lookup repos consumed,
// no Status string (IsActive bool is the canonical archive toggle).
public class WarehousesAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        WarehousesController Controller,
        Mock<IWarehouseRepository> Repo,
        Mock<IValidator<WarehouseCreateViewModel>> CreateValidator,
        Mock<IValidator<WarehouseEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<IWarehouseRepository>();
        var factory = new Mock<IWarehouseRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        var receiving        = new Mock<IReceivingHeaderRepository>();
        var receivingFactory = new Mock<IReceivingHeaderRepositoryFactory>();
        var movements        = new Mock<IStockMovementRepository>();
        var movementFactory  = new Mock<IStockMovementRepositoryFactory>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        receivingFactory.Setup(x => x.For(It.IsAny<Guid>())).Returns(receiving.Object);
        movementFactory.Setup(x => x.For(It.IsAny<Guid>())).Returns(movements.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>();

        var createValidator = new Mock<IValidator<WarehouseCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<WarehouseCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<WarehouseEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<WarehouseEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new WarehousesController(
            factory.Object,
            receivingFactory.Object,
            movementFactory.Object,
            tenant.Object,
            docs,
            createValidator.Object,
            editValidator.Object,
            currentUser.Object);

        return new AdminBuild(ctrl, repo, createValidator, editValidator, userId);
    }

    [Fact]
    public void Create_Get_ReturnsView()
    {
        var b = BuildAdmin();
        var result = b.Controller.Create();

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<WarehouseCreateViewModel>(view.Model);
    }

    [Fact]
    public async Task Create_Post_HappyPath_CallsInsert_Redirects()
    {
        var b = BuildAdmin();
        Warehouse? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Warehouse>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Warehouse, Guid?, CancellationToken>((w, uid, _) => { captured = w; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var vm = new WarehouseCreateViewModel
        {
            Code = "  WH-NEW  ",
            Name = "  New Warehouse  ",
            Type = "Satellite",
            Address = "  123 Industrial Rd  ",
            PhoneNumber = "  +66 2 555 0001  ",
            ManagerName = "  Somchai  ",
            TimeZoneId = "  Asia/Bangkok  ",
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("WH-NEW", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("WH-NEW", captured!.Code);
        Assert.Equal("New Warehouse", captured.Name);
        Assert.Equal("Satellite", captured.Type);
        Assert.Equal("Asia/Bangkok", captured.TimeZoneId);
        Assert.True(captured.IsActive);
        Assert.Equal(b.UserId, capturedUserId);
    }

    [Fact]
    public async Task Create_Post_FluentValidationFails_AddsErrors()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<WarehouseCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Code", "Code 'WH-DUP' is already in use."),
            }));

        var vm = new WarehouseCreateViewModel { Code = "WH-DUP", Name = "Dup", Type = "Main" };
        var result = await b.Controller.Create(vm, default);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(b.Controller.ModelState["Code"]!.Errors,
            e => e.ErrorMessage.Contains("already in use"));
    }

    [Fact]
    public async Task Create_Post_BlankNullableStrings_StoreAsNull()
    {
        var b = BuildAdmin();
        Warehouse? captured = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Warehouse>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Warehouse, Guid?, CancellationToken>((w, _, _) => captured = w)
            .ReturnsAsync(Guid.NewGuid());

        var vm = new WarehouseCreateViewModel
        {
            Code = "WH-MIN",
            Name = "Minimal",
            Type = "Main",
            Address = "   ",
            PhoneNumber = "",
            ManagerName = null,
            TimeZoneId = " ",
            IsActive = true,
        };
        await b.Controller.Create(vm, default);

        Assert.NotNull(captured);
        Assert.Null(captured!.Address);
        Assert.Null(captured.PhoneNumber);
        Assert.Null(captured.ManagerName);
        Assert.Null(captured.TimeZoneId);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Warehouse?)null);

        var result = await b.Controller.Edit("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesVm()
    {
        var b = BuildAdmin();
        var entity = new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = "WH-MAIN",
            Name = "Main DC",
            Type = "Main",
            Address = "1 Main St",
            IsActive = true,
        };
        b.Repo.Setup(r => r.GetByCodeAsync("WH-MAIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("WH-MAIN", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<WarehouseEditViewModel>(view.Model);
        Assert.Equal(entity.Id, vm.Id);
        Assert.Equal("WH-MAIN", vm.Code);
        Assert.Equal("Main DC", vm.Name);
        Assert.Equal("Main", vm.Type);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Warehouse>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new WarehouseEditViewModel
        {
            Id = Guid.NewGuid(), Code = "X", Name = "X", Type = "Main", IsActive = true,
        };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_HappyPath_CallsUpdate_Redirects()
    {
        var b = BuildAdmin();
        Warehouse? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Warehouse>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Warehouse, Guid?, CancellationToken>((w, _, _) => captured = w)
            .ReturnsAsync(true);

        var vm = new WarehouseEditViewModel
        {
            Id = Guid.NewGuid(),
            Code = "WH-MAIN",
            Name = "Renamed DC",
            Type = "Branch",
            IsActive = false,
        };
        var result = await b.Controller.Edit("WH-MAIN", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("WH-MAIN", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("WH-MAIN", captured!.Code);
        Assert.Equal("Renamed DC", captured.Name);
        Assert.Equal("Branch", captured.Type);
        Assert.False(captured.IsActive);
    }
}
