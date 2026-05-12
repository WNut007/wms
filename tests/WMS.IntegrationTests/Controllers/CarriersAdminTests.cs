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
using WMS.Web.Services.Mappers;

namespace WMS.IntegrationTests.Controllers;

// Phase 30A.3 Block 1.3 admin-side tests for CarriersController.
// Mirrors BoxTypes/UoMs tests + adds Status-flip verification (the
// flagship use-case per Mod #2: operator promotes Inactive → Production
// via the Edit form rather than via a data migration).
public class CarriersAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        CarriersController Controller,
        Mock<ICarrierRepository> Repo,
        Mock<IValidator<CarrierCreateViewModel>> CreateValidator,
        Mock<IValidator<CarrierEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<ICarrierRepository>();
        var factory = new Mock<ICarrierRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var createValidator = new Mock<IValidator<CarrierCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CarrierCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<CarrierEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CarrierEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new CarriersController(
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
        Assert.IsType<ViewResult>(b.Controller.Index());
    }

    [Fact]
    public async Task GetData_Happy_ReturnsItemsAndCounts()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<CarrierFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CarrierListRow>
            {
                Items = new List<CarrierListRow>
                {
                    new(Guid.NewGuid(), "FLASH", "Flash Express", "Express", "TH",
                        "Inactive", 10, true, DateTime.UtcNow, null)
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierStatusCounts(
                All: 4, Inactive: 4, Configured: 0, Tested: 0, Production: 0));

        var result = await b.Controller.GetData(ct: default);
        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Theory]
    [InlineData("Inactive",   "Inactive")]
    [InlineData("Configured", "Configured")]
    [InlineData("Tested",     "Tested")]
    [InlineData("Production", "Production")]
    [InlineData("all",  null)]
    [InlineData("",     null)]
    [InlineData(null,   null)]
    [InlineData("bogus", null)]
    public async Task GetData_StatusFilter_NormalisesToValidEnumOrNull(string? wireStatus, string? expectedDb)
    {
        var b = BuildAdmin();
        CarrierFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<CarrierFilter>(), It.IsAny<CancellationToken>()))
            .Callback<CarrierFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<CarrierListRow>
            {
                Items = new List<CarrierListRow>(),
                Total = 0, Page = 1, PageSize = 20, TotalPages = 0
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierStatusCounts(0, 0, 0, 0, 0));

        await b.Controller.GetData(status: wireStatus, ct: default);
        Assert.NotNull(captured);
        Assert.Equal(expectedDb, captured!.Status);
    }

    [Fact]
    public void Create_Get_ReturnsView()
    {
        var b = BuildAdmin();
        var view = Assert.IsType<ViewResult>(b.Controller.Create());
        Assert.IsType<CarrierCreateViewModel>(view.Model);
    }

    [Fact]
    public async Task Create_Post_Happy_CallsInsert_RedirectsToEdit()
    {
        var b = BuildAdmin();
        Carrier? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Carrier>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Carrier, Guid?, CancellationToken>((c, uid, _) => { captured = c; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var vm = new CarrierCreateViewModel
        {
            Code = "  TEST  ",
            Name = "  Test Carrier  ",
            Type = "Express",
            Country = "TH",
            Status = "Inactive",
            SupportedServiceLevels = " [\"Standard\"] ",
            DisplayOrder = 10,
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal("TEST", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("TEST", captured!.Code);
        Assert.Equal("Test Carrier", captured.Name);
        Assert.Equal("Express", captured.Type);
        Assert.Equal("TH", captured.Country);
        Assert.Equal("Inactive", captured.Status);
        Assert.Equal("[\"Standard\"]", captured.SupportedServiceLevels);
        Assert.Equal(10, captured.DisplayOrder);
        Assert.True(captured.IsActive);
        Assert.Equal(b.UserId, capturedUserId);
    }

    [Fact]
    public async Task Create_Post_BlankNullableStrings_StoreAsNull()
    {
        var b = BuildAdmin();
        Carrier? captured = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Carrier>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Carrier, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(Guid.NewGuid());

        var vm = new CarrierCreateViewModel
        {
            Code = "MIN", Name = "Minimal",
            Type = "", Country = "  ", SupportedServiceLevels = " ",
            WebsiteUrl = null, LogoUrl = "",
            Status = "Inactive", IsActive = true,
        };
        await b.Controller.Create(vm, default);

        Assert.NotNull(captured);
        Assert.Null(captured!.Type);
        Assert.Null(captured.Country);
        Assert.Null(captured.SupportedServiceLevels);
        Assert.Null(captured.WebsiteUrl);
        Assert.Null(captured.LogoUrl);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Carrier?)null);

        var result = await b.Controller.Edit("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesVm()
    {
        var b = BuildAdmin();
        var entity = new Carrier
        {
            Id = Guid.NewGuid(), Code = "FLASH", Name = "Flash Express",
            Type = "Express", Country = "TH", Status = "Inactive",
            DisplayOrder = 10, IsActive = true,
        };
        b.Repo.Setup(r => r.GetByCodeAsync("FLASH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("FLASH", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CarrierEditViewModel>(view.Model);
        Assert.Equal("FLASH", vm.Code);
        Assert.Equal("Inactive", vm.Status);
        Assert.Equal("Express", vm.Type);
    }

    [Fact]
    public async Task Edit_Post_FlipInactiveToProduction_PersistsNewStatus()
    {
        // Flagship test — Mod #2: the operator promotes Inactive → Production
        // via this form (the alternative to a data migration). Verify the
        // entity sent to UpdateAsync carries the new Status verbatim.
        var b = BuildAdmin();
        Carrier? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Carrier>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Carrier, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(true);

        var vm = new CarrierEditViewModel
        {
            Id = Guid.NewGuid(),
            Code = "FLASH",
            Name = "Flash Express",
            Type = "Express",
            Country = "TH",
            Status = "Production",  // ← the flip
            IsActive = true,
        };
        var result = await b.Controller.Edit("FLASH", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);

        Assert.NotNull(captured);
        Assert.Equal("FLASH", captured!.Code);
        Assert.Equal("Production", captured.Status);
        Assert.True(captured.IsActive);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Carrier>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new CarrierEditViewModel
        {
            Id = Guid.NewGuid(), Code = "X", Name = "X", Status = "Inactive", IsActive = true,
        };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("Inactive", "neutral")]
    [InlineData("Configured", "info")]
    [InlineData("Tested", "warning")]
    [InlineData("Production", "success")]
    [InlineData("Bogus", "neutral")]
    public void StatusMapper_Variant_ReturnsCorrectCssVariant(string status, string expected)
    {
        Assert.Equal(expected, CarrierStatusMapper.Variant(status));
    }
}
