using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;
using WMS.Web.Controllers;
using WMS.Web.Models.Master;

namespace WMS.IntegrationTests.Controllers;

// Phase 30A.3 Block 1 admin-side tests for BoxTypesController.
// Same shape as WarehousesAdminTests (Phase 7F pattern).
public class BoxTypesAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        BoxTypesController Controller,
        Mock<IBoxTypeRepository> Repo,
        Mock<IValidator<BoxTypeCreateViewModel>> CreateValidator,
        Mock<IValidator<BoxTypeEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<IBoxTypeRepository>();
        var factory = new Mock<IBoxTypeRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var createValidator = new Mock<IValidator<BoxTypeCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<BoxTypeCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<BoxTypeEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<BoxTypeEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new BoxTypesController(
            factory.Object,
            tenant.Object,
            createValidator.Object,
            editValidator.Object,
            currentUser.Object);

        return new AdminBuild(ctrl, repo, createValidator, editValidator, userId);
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var b = BuildAdmin();
        var result = b.Controller.Index();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GetData_Happy_ReturnsItemsAndCounts()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<BoxTypeFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BoxTypeListRow>
            {
                Items = new List<BoxTypeListRow>
                {
                    new(Guid.NewGuid(), "BOX-S", "Small", 20m, 15m, 10m, 22m, 17m, 12m, 0.150m, 5m, 8m, true,
                        DateTime.UtcNow, null)
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BoxTypeStatusCounts(All: 1, Active: 1, Inactive: 0));

        var result = await b.Controller.GetData(ct: default);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public void Create_Get_ReturnsView()
    {
        var b = BuildAdmin();
        var result = b.Controller.Create();
        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<BoxTypeCreateViewModel>(view.Model);
    }

    [Fact]
    public async Task Create_Post_Happy_CallsInsert_RedirectsToEdit()
    {
        var b = BuildAdmin();
        BoxType? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<BoxType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<BoxType, Guid?, CancellationToken>((bt, uid, _) => { captured = bt; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var vm = new BoxTypeCreateViewModel
        {
            Code = "  BOX-M  ",
            Name = "  Medium  ",
            EmptyWeightKg = 0.250m,
            MaxLoadKg = 10m,
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal("BOX-M", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("BOX-M", captured!.Code);
        Assert.Equal("Medium", captured.Name);
        Assert.Equal(0.250m, captured.EmptyWeightKg);
        Assert.True(captured.IsActive);
        Assert.Equal(b.UserId, capturedUserId);
    }

    [Fact]
    public async Task Create_Post_ValidatorFails_AddsErrors_ReturnsView()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<BoxTypeCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Code", "Code 'BOX-DUP' is already in use."),
            }));

        var vm = new BoxTypeCreateViewModel { Code = "BOX-DUP", Name = "Dup" };
        var result = await b.Controller.Create(vm, default);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(b.Controller.ModelState["Code"]!.Errors,
            e => e.ErrorMessage.Contains("already in use"));
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BoxType?)null);

        var result = await b.Controller.Edit("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesVm()
    {
        var b = BuildAdmin();
        var entity = new BoxType
        {
            Id = Guid.NewGuid(), Code = "BOX-L", Name = "Large",
            InternalLengthCm = 50m, InternalWidthCm = 40m, InternalHeightCm = 30m,
            EmptyWeightKg = 0.450m, IsActive = true,
        };
        b.Repo.Setup(r => r.GetByCodeAsync("BOX-L", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("BOX-L", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<BoxTypeEditViewModel>(view.Model);
        Assert.Equal(entity.Id, vm.Id);
        Assert.Equal("BOX-L", vm.Code);
        Assert.Equal("Large", vm.Name);
        Assert.Equal(50m, vm.InternalLengthCm);
        Assert.Equal(0.450m, vm.EmptyWeightKg);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<BoxType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new BoxTypeEditViewModel { Id = Guid.NewGuid(), Code = "X", Name = "X", IsActive = true };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_Happy_CallsUpdate_RedirectsToEdit()
    {
        var b = BuildAdmin();
        BoxType? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<BoxType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<BoxType, Guid?, CancellationToken>((bt, _, _) => captured = bt)
            .ReturnsAsync(true);

        var vm = new BoxTypeEditViewModel
        {
            Id = Guid.NewGuid(), Code = "BOX-M", Name = "Medium Renamed",
            EmptyWeightKg = 0.300m, IsActive = false,
        };
        var result = await b.Controller.Edit("BOX-M", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal("BOX-M", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("BOX-M", captured!.Code);
        Assert.Equal("Medium Renamed", captured.Name);
        Assert.Equal(0.300m, captured.EmptyWeightKg);
        Assert.False(captured.IsActive);
    }
}
