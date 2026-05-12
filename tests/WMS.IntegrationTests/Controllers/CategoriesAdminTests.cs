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

// Phase 30A.3 Block 1.4 admin-side tests for CategoriesController.
// Most complex of the 4 Block 1 CRUDs due to hierarchy. Tests cover:
//   * Standard CRUD shape (Index / GetData / Create / Edit)
//   * Parent picker plumbing (ViewBag.Parents populated on both forms)
//   * Path read-only invariant (Edit POST must NOT pass Path to repo)
//   * RootsOnly filter flow-through
public class CategoriesAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        CategoriesController Controller,
        Mock<IProductCategoryRepository> Repo,
        Mock<IValidator<CategoryCreateViewModel>> CreateValidator,
        Mock<IValidator<CategoryEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<IProductCategoryRepository>();
        var factory = new Mock<IProductCategoryRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        // Default: empty parent picker so Create/Edit don't blow up
        // when not explicitly set up by individual tests.
        repo.Setup(r => r.GetPickerItemsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductCategoryPickerItem>());

        var createValidator = new Mock<IValidator<CategoryCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CategoryCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<CategoryEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CategoryEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new CategoriesController(
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
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductCategoryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProductCategoryListRow>
            {
                Items = new List<ProductCategoryListRow>
                {
                    new(Guid.NewGuid(), "CAT-SKIN", "Skincare", null, null, "/Skincare", null, 10, true,
                        DateTime.UtcNow, null)
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductCategoryStatusCounts(All: 1, Active: 1, Inactive: 0, Roots: 1));

        var result = await b.Controller.GetData(ct: default);
        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task GetData_RootsOnly_FlowsToFilter()
    {
        var b = BuildAdmin();
        ProductCategoryFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ProductCategoryFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ProductCategoryFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ProductCategoryListRow>
            {
                Items = new List<ProductCategoryListRow>(),
                Total = 0, Page = 1, PageSize = 20, TotalPages = 0
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductCategoryStatusCounts(0, 0, 0, 0));

        await b.Controller.GetData(rootsOnly: true, ct: default);
        Assert.NotNull(captured);
        Assert.True(captured!.RootsOnly);
    }

    [Fact]
    public async Task Create_Get_PopulatesParentPicker()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetPickerItemsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductCategoryPickerItem>
            {
                new(Guid.NewGuid(), "CAT-MAKEUP", "Makeup", "/Makeup"),
            });

        var result = await b.Controller.Create(default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<CategoryCreateViewModel>(view.Model);
        var parents = Assert.IsAssignableFrom<IReadOnlyList<ProductCategoryPickerItem>>(
            view.ViewData["Parents"]);
        Assert.Single(parents);
    }

    [Fact]
    public async Task Create_Post_Happy_CallsInsert_RedirectsToEdit()
    {
        var b = BuildAdmin();
        ProductCategory? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<ProductCategory>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<ProductCategory, Guid?, CancellationToken>((c, uid, _) => { captured = c; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var parentId = Guid.NewGuid();
        var vm = new CategoryCreateViewModel
        {
            Code = "  CAT-CREAM  ",
            Name = "  Face Cream  ",
            ParentId = parentId,
            Description = " Hydrating creams ",
            DisplayOrder = 10,
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal("CAT-CREAM", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("CAT-CREAM", captured!.Code);
        Assert.Equal("Face Cream", captured.Name);
        Assert.Equal(parentId, captured.ParentId);
        Assert.Equal("Hydrating creams", captured.Description);
        Assert.Equal(10, captured.DisplayOrder);
        Assert.True(captured.IsActive);
        Assert.Equal(b.UserId, capturedUserId);
        // Path must NOT be set from BLL — trigger maintains it.
        Assert.Null(captured.Path);
    }

    [Fact]
    public async Task Create_Post_BlankNullableStrings_StoreAsNull()
    {
        var b = BuildAdmin();
        ProductCategory? captured = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<ProductCategory>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<ProductCategory, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(Guid.NewGuid());

        var vm = new CategoryCreateViewModel
        {
            Code = "CAT-MIN", Name = "Minimal",
            ParentId = null, Description = "   ", IsActive = true,
        };
        await b.Controller.Create(vm, default);

        Assert.NotNull(captured);
        Assert.Null(captured!.ParentId);
        Assert.Null(captured.Description);
    }

    [Fact]
    public async Task Create_Post_ValidatorFails_RepopulatesParentPicker()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CategoryCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Code", "Code 'CAT-DUP' is already in use."),
            }));
        b.Repo.Setup(r => r.GetPickerItemsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductCategoryPickerItem>
            {
                new(Guid.NewGuid(), "CAT-OTHER", "Other", "/Other"),
            });

        var vm = new CategoryCreateViewModel { Code = "CAT-DUP", Name = "Dup" };
        var result = await b.Controller.Create(vm, default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(b.Controller.ModelState["Code"]!.Errors,
            e => e.ErrorMessage.Contains("already in use"));
        // Parent picker must be repopulated for the re-render.
        var parents = Assert.IsAssignableFrom<IReadOnlyList<ProductCategoryPickerItem>>(
            view.ViewData["Parents"]);
        Assert.Single(parents);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);

        var result = await b.Controller.Edit("MISSING", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_ExcludesSelfFromPicker()
    {
        var b = BuildAdmin();
        var id = Guid.NewGuid();
        var entity = new ProductCategory
        {
            Id = id, Code = "CAT-SKIN", Name = "Skincare",
            Path = "/Skincare", IsActive = true,
        };
        b.Repo.Setup(r => r.GetByCodeAsync("CAT-SKIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("CAT-SKIN", default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CategoryEditViewModel>(view.Model);
        Assert.Equal("CAT-SKIN", vm.Code);
        Assert.Equal("/Skincare", vm.Path);

        // Picker must be called with excludeId=current row.
        b.Repo.Verify(r => r.GetPickerItemsAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<ProductCategory>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new CategoryEditViewModel
        {
            Id = Guid.NewGuid(), Code = "X", Name = "X", IsActive = true,
        };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_Happy_DoesNotSetPath()
    {
        // Critical invariant — Path is trigger-maintained, not BLL-set.
        // Verify the entity sent to UpdateAsync has Path=null even
        // though the VM may carry the current Path for display.
        var b = BuildAdmin();
        ProductCategory? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<ProductCategory>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<ProductCategory, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(true);

        var vm = new CategoryEditViewModel
        {
            Id = Guid.NewGuid(),
            Code = "CAT-SKIN",
            Path = "/Skincare",  // VM carries Path for display only
            Name = "Skincare (renamed)",
            ParentId = null,
            IsActive = true,
        };
        var result = await b.Controller.Edit("CAT-SKIN", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);

        Assert.NotNull(captured);
        Assert.Equal("Skincare (renamed)", captured!.Name);
        // Repo received Path=null — controller stripped it.
        Assert.Null(captured.Path);
    }

    [Fact]
    public async Task Edit_Post_ParentChange_FlowsToRepo()
    {
        var b = BuildAdmin();
        ProductCategory? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<ProductCategory>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<ProductCategory, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(true);

        var newParent = Guid.NewGuid();
        var vm = new CategoryEditViewModel
        {
            Id = Guid.NewGuid(), Code = "CAT-CREAM", Name = "Face Cream",
            ParentId = newParent, IsActive = true,
        };
        await b.Controller.Edit("CAT-CREAM", vm, default);

        Assert.NotNull(captured);
        Assert.Equal(newParent, captured!.ParentId);
    }
}
