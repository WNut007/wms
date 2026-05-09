using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;
using WMS.Web.Controllers;
using WMS.Web.Models.Master;
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Controllers;

// Phase 7F admin-side tests for ProductsController.Admin.cs partial.
// Uses a richer Build() that exposes all 9 mocks — separate from the
// read-side ProductsControllerTests.Build() so existing tuple
// destructuring doesn't break.
//
// Coverage targets:
//   - GET Create populates lookup lists from repos.
//   - POST Create with ModelState invalid re-populates lookups + returns View.
//   - POST Create with FluentValidation failure merges errors into ModelState.
//   - POST Create happy path calls InsertAsync with mapped entity + redirects.
//   - GET Edit returns 404 for unknown code.
//   - GET Edit populates VM from entity + lookups.
//   - POST Edit returns 404 when UpdateAsync reports 0 rows.
//   - POST Edit happy path calls UpdateAsync + redirects to Detail.
public class ProductsAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        ProductsController Controller,
        Mock<IProductRepository> Repo,
        Mock<IProductCategoryRepository> Categories,
        Mock<IUomRepository> Uoms,
        Mock<IValidator<ProductCreateViewModel>> CreateValidator,
        Mock<IValidator<ProductEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<IProductRepository>();
        var factory = new Mock<IProductRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        var movements = new Mock<IStockMovementRepository>();
        var movementFactory = new Mock<IStockMovementRepositoryFactory>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        movementFactory.Setup(x => x.For(It.IsAny<Guid>())).Returns(movements.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>();

        var categoryRepo = new Mock<IProductCategoryRepository>();
        categoryRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "ELEC", "Electronics"),
                new(Guid.NewGuid(), "FOOD", "Food"),
            });
        var categoryFactory = new Mock<IProductCategoryRepositoryFactory>();
        categoryFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(categoryRepo.Object);

        var uomRepo = new Mock<IUomRepository>();
        uomRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "EA", "Each"),
                new(Guid.NewGuid(), "KG", "Kilogram"),
            });
        var uomFactory = new Mock<IUomRepositoryFactory>();
        uomFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(uomRepo.Object);

        var createValidator = new Mock<IValidator<ProductCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ProductCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<ProductEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ProductEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new ProductsController(
            factory.Object,
            movementFactory.Object,
            tenant.Object,
            docs,
            categoryFactory.Object,
            uomFactory.Object,
            createValidator.Object,
            editValidator.Object,
            currentUser.Object);

        return new AdminBuild(ctrl, repo, categoryRepo, uomRepo, createValidator, editValidator, userId);
    }

    // SqlException-by-Number catch paths (UQ 2627, FK 547) are not
    // covered here — SqlException can't be constructed cleanly without
    // reflection that breaks across Microsoft.Data.SqlClient versions.
    // The catch logic is structurally simple (a `when (ex.Number ==
    // X)` filter); meaningful coverage needs a real SQL fixture, same
    // bucket as TD-006. Visual review + runtime smoke cover it for now.

    [Fact]
    public async Task Create_Get_PopulatesLookups()
    {
        var b = BuildAdmin();
        var result = await b.Controller.Create(default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<ProductCreateViewModel>(view.Model);
        Assert.Equal(2, vm.Categories.Count);
        Assert.Equal(2, vm.BaseUoms.Count);
    }

    [Fact]
    public async Task Create_Post_ModelStateInvalid_RepopulatesLookups()
    {
        var b = BuildAdmin();
        b.Controller.ModelState.AddModelError("Code", "required");

        var result = await b.Controller.Create(new ProductCreateViewModel(), default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<ProductCreateViewModel>(view.Model);
        Assert.Equal(2, vm.Categories.Count);
        b.Repo.Verify(r => r.InsertAsync(It.IsAny<Product>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_FluentValidationFails_AddsErrorsToModelState()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ProductCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Code", "Code 'X' is already in use.")
            }));

        var result = await b.Controller.Create(
            new ProductCreateViewModel { Code = "X", Name = "X", TrackingMethod = "None", Status = "Active" },
            default);

        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
        Assert.Contains(b.Controller.ModelState["Code"]!.Errors, e => e.ErrorMessage.Contains("already in use"));
    }

    [Fact]
    public async Task Create_Post_HappyPath_CallsInsertAndRedirects()
    {
        var b = BuildAdmin();
        var catId = Guid.NewGuid();
        var uomId = Guid.NewGuid();
        Product? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Product>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Product, Guid?, CancellationToken>((p, uid, _) => { captured = p; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var vm = new ProductCreateViewModel
        {
            Code = "  WIDGET-1  ",
            Name = "  Widget  ",
            CategoryId = catId,
            BaseUomId = uomId,
            TrackingMethod = "Lot",
            VelocityClass = " A ",
            UseCatchWeight = true,
            Status = "Active",
            Brand = "  Acme  ",
        };

        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("WIDGET-1", redirect.RouteValues!["sku"]);

        Assert.NotNull(captured);
        Assert.Equal("WIDGET-1", captured!.Code);
        Assert.Equal("Widget", captured.Name);
        Assert.Equal("A", captured.VelocityClass);
        Assert.Equal("Acme", captured.Brand);
        Assert.True(captured.UseCatchWeight);
        Assert.Equal(b.UserId, capturedUserId);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var result = await b.Controller.Edit("MISSING", default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesVm()
    {
        var b = BuildAdmin();
        var entity = new Product
        {
            Id = Guid.NewGuid(),
            Code = "WIDGET-1",
            Name = "Widget",
            CategoryId = Guid.NewGuid(),
            BaseUomId = Guid.NewGuid(),
            TrackingMethod = "Lot",
            Status = "Active",
            Brand = "Acme",
            UseCatchWeight = true,
        };
        b.Repo.Setup(r => r.GetByCodeAsync("WIDGET-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("WIDGET-1", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<ProductEditViewModel>(view.Model);

        Assert.Equal(entity.Id, vm.Id);
        Assert.Equal("WIDGET-1", vm.Code);
        Assert.Equal("Widget", vm.Name);
        Assert.Equal("Lot", vm.TrackingMethod);
        Assert.True(vm.UseCatchWeight);
        Assert.Equal(2, vm.Categories.Count);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new ProductEditViewModel
        {
            Id = Guid.NewGuid(), Code = "X", Name = "X",
            CategoryId = Guid.NewGuid(), BaseUomId = Guid.NewGuid(),
            TrackingMethod = "None", Status = "Active",
        };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_HappyPath_CallsUpdateAndRedirects()
    {
        var b = BuildAdmin();
        Product? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Product, Guid?, CancellationToken>((p, _, _) => captured = p)
            .ReturnsAsync(true);

        var vm = new ProductEditViewModel
        {
            Id = Guid.NewGuid(), Code = "WIDGET-1", Name = "Updated Name",
            CategoryId = Guid.NewGuid(), BaseUomId = Guid.NewGuid(),
            TrackingMethod = "None", Status = "Inactive",
        };
        var result = await b.Controller.Edit("WIDGET-1", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("WIDGET-1", redirect.RouteValues!["sku"]);

        Assert.NotNull(captured);
        Assert.Equal("WIDGET-1", captured!.Code);
        Assert.Equal("Updated Name", captured.Name);
        Assert.Equal("Inactive", captured.Status);
    }
}
