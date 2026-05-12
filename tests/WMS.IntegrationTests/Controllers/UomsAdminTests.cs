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

// Phase 30A.3 Block 1.2 admin-side tests for UomsController.
// Mirrors BoxTypesAdminTests + adds Type-locked-on-edit + IsBase
// invariant verifications.
public class UomsAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        UomsController Controller,
        Mock<IUomRepository> Repo,
        Mock<IValidator<UomCreateViewModel>> CreateValidator,
        Mock<IValidator<UomEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<IUomRepository>();
        var factory = new Mock<IUomRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var createValidator = new Mock<IValidator<UomCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<UomCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<UomEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<UomEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new UomsController(
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
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<UomFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<UomListRow>
            {
                Items = new List<UomListRow>
                {
                    new(Guid.NewGuid(), "EA", "Each", "Count", true, true, DateTime.UtcNow, null)
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UomStatusCounts(All: 1, Active: 1, Inactive: 0,
                                              Count: 1, Weight: 0, Volume: 0, Length: 0));

        var result = await b.Controller.GetData(ct: default);
        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Theory]
    [InlineData("Count", "Count")]
    [InlineData("Weight", "Weight")]
    [InlineData("Volume", "Volume")]
    [InlineData("Length", "Length")]
    [InlineData("all", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("bogus", null)]
    public async Task GetData_TypeFilter_NormalisesToValidEnumOrNull(string? wireType, string? expectedDb)
    {
        var b = BuildAdmin();
        UomFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<UomFilter>(), It.IsAny<CancellationToken>()))
            .Callback<UomFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<UomListRow>
            {
                Items = new List<UomListRow>(),
                Total = 0, Page = 1, PageSize = 20, TotalPages = 0
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UomStatusCounts(0, 0, 0, 0, 0, 0, 0));

        await b.Controller.GetData(type: wireType, ct: default);
        Assert.NotNull(captured);
        Assert.Equal(expectedDb, captured!.Type);
    }

    [Fact]
    public void Create_Get_ReturnsView()
    {
        var b = BuildAdmin();
        var result = b.Controller.Create();
        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<UomCreateViewModel>(view.Model);
    }

    [Fact]
    public async Task Create_Post_Happy_CallsInsert_RedirectsToEdit()
    {
        var b = BuildAdmin();
        Uom? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Uom>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Uom, Guid?, CancellationToken>((u, uid, _) => { captured = u; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var vm = new UomCreateViewModel
        {
            Code = "  KG  ",
            Name = "  Kilogram  ",
            Type = "Weight",
            IsBase = true,
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal("KG", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("KG", captured!.Code);
        Assert.Equal("Kilogram", captured.Name);
        Assert.Equal("Weight", captured.Type);
        Assert.True(captured.IsBase);
        Assert.True(captured.IsActive);
        Assert.Equal(b.UserId, capturedUserId);
    }

    [Fact]
    public async Task Create_Post_ValidatorFails_AddsErrors_ReturnsView()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<UomCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("IsBase", "Type 'Weight' already has a base unit."),
            }));

        var vm = new UomCreateViewModel { Code = "G", Name = "Gram", Type = "Weight", IsBase = true };
        var result = await b.Controller.Create(vm, default);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(b.Controller.ModelState["IsBase"]!.Errors,
            e => e.ErrorMessage.Contains("already has a base unit"));
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uom?)null);

        var result = await b.Controller.Edit("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesVm()
    {
        var b = BuildAdmin();
        var entity = new Uom
        {
            Id = Guid.NewGuid(), Code = "EA", Name = "Each",
            Type = "Count", IsBase = true, IsActive = true,
        };
        b.Repo.Setup(r => r.GetByCodeAsync("EA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("EA", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<UomEditViewModel>(view.Model);
        Assert.Equal(entity.Id, vm.Id);
        Assert.Equal("EA", vm.Code);
        Assert.Equal("Count", vm.Type);
        Assert.True(vm.IsBase);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Uom>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new UomEditViewModel
        {
            Id = Guid.NewGuid(), Code = "X", Name = "X", Type = "Count", IsActive = true,
        };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_Happy_CallsUpdate_RedirectsToEdit()
    {
        var b = BuildAdmin();
        Uom? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Uom>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Uom, Guid?, CancellationToken>((u, _, _) => captured = u)
            .ReturnsAsync(true);

        var vm = new UomEditViewModel
        {
            Id = Guid.NewGuid(),
            Code = "KG",
            Name = "Kilogram (renamed)",
            Type = "Weight",
            IsBase = true,
            IsActive = false,
        };
        var result = await b.Controller.Edit("KG", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal("KG", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("KG", captured!.Code);
        Assert.Equal("Kilogram (renamed)", captured.Name);
        Assert.True(captured.IsBase);
        Assert.False(captured.IsActive);
    }
}
