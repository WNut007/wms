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

// Phase 30A.3 Block 2.1 admin-side tests for ZonesController.
// First CRUD with:
//   * Composite-key uniqueness (WarehouseId, Code).
//   * Edit route keyed by Guid (not Code) because Code is not tenant-
//     wide unique.
//   * Warehouse picker dropdown plumbed via ViewBag.Warehouses.
public class ZonesAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid WhId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private record AdminBuild(
        ZonesController Controller,
        Mock<IZoneRepository> Repo,
        Mock<IWarehouseRepository> WarehouseRepo,
        Mock<IValidator<ZoneCreateViewModel>> CreateValidator,
        Mock<IValidator<ZoneEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<IZoneRepository>();
        var factory = new Mock<IZoneRepositoryFactory>();
        var whRepo  = new Mock<IWarehouseRepository>();
        var whFactory = new Mock<IWarehouseRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        whFactory.Setup(x => x.For(It.IsAny<Guid>())).Returns(whRepo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        // Defaults so Create/Edit don't blow up when not explicitly set.
        whRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<WarehouseInfo>());

        var createValidator = new Mock<IValidator<ZoneCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ZoneCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<ZoneEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ZoneEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new ZonesController(
            factory.Object,
            whFactory.Object,
            tenant.Object,
            createValidator.Object,
            editValidator.Object,
            currentUser.Object);

        return new AdminBuild(ctrl, repo, whRepo, createValidator, editValidator, userId);
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
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ZoneFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ZoneListRow>
            {
                Items = new List<ZoneListRow>
                {
                    new(Guid.NewGuid(), WhId, "WH-MAIN", "Main Warehouse",
                        "RECV", "Receiving Zone", "Receiving", null,
                        true, true, 1, DateTime.UtcNow, null)
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZoneStatusCounts(
                All: 1, Active: 1, Inactive: 0,
                Receiving: 1, Storage: 0, Picking: 0, Packing: 0,
                Shipping: 0, Staging: 0, Quarantine: 0, Returns: 0));

        var result = await b.Controller.GetData(ct: default);
        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Theory]
    [InlineData("Receiving", "Receiving")]
    [InlineData("Storage", "Storage")]
    [InlineData("Picking", "Picking")]
    [InlineData("all", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("bogus", null)]
    public async Task GetData_TypeFilter_NormalisesToValidEnumOrNull(string? wireType, string? expectedDb)
    {
        var b = BuildAdmin();
        ZoneFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ZoneFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ZoneFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ZoneListRow>
            {
                Items = new List<ZoneListRow>(),
                Total = 0, Page = 1, PageSize = 20, TotalPages = 0
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZoneStatusCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        await b.Controller.GetData(type: wireType, ct: default);
        Assert.NotNull(captured);
        Assert.Equal(expectedDb, captured!.Type);
    }

    [Fact]
    public async Task GetData_EmptyGuid_WarehouseId_NormalisedToNull()
    {
        var b = BuildAdmin();
        ZoneFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<ZoneFilter>(), It.IsAny<CancellationToken>()))
            .Callback<ZoneFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<ZoneListRow>
            {
                Items = new List<ZoneListRow>(),
                Total = 0, Page = 1, PageSize = 20, TotalPages = 0
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZoneStatusCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        await b.Controller.GetData(warehouseId: Guid.Empty, ct: default);
        Assert.NotNull(captured);
        Assert.Null(captured!.WarehouseId);
    }

    [Fact]
    public async Task Create_Get_PopulatesWarehousePicker()
    {
        var b = BuildAdmin();
        b.WarehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo>
            {
                new(WhId, "WH-MAIN", "Main"),
            });

        var result = await b.Controller.Create(default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<ZoneCreateViewModel>(view.Model);
        var warehouses = Assert.IsAssignableFrom<IReadOnlyList<WarehouseInfo>>(view.ViewData["Warehouses"]);
        Assert.Single(warehouses);
    }

    [Fact]
    public async Task Create_Post_Happy_CallsInsert_RedirectsToEditWithId()
    {
        var b = BuildAdmin();
        Zone? captured = null;
        var newId = Guid.NewGuid();
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Zone>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Zone, Guid?, CancellationToken>((z, _, _) => captured = z)
            .ReturnsAsync(newId);

        var vm = new ZoneCreateViewModel
        {
            WarehouseId = WhId,
            Code = "  PICK-A  ",
            Name = "  Picking Aisle A  ",
            Type = "Picking",
            Temperature = "Ambient",
            LotCommingleAllowed = false,
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        // Edit route is keyed by Id (Guid), not Code.
        Assert.NotEqual(Guid.Empty, (Guid)redirect.RouteValues!["id"]!);

        Assert.NotNull(captured);
        Assert.Equal(WhId, captured!.WarehouseId);
        Assert.Equal("PICK-A", captured.Code);
        Assert.Equal("Picking Aisle A", captured.Name);
        Assert.Equal("Picking", captured.Type);
        Assert.Equal("Ambient", captured.Temperature);
        Assert.False(captured.LotCommingleAllowed);
    }

    [Fact]
    public async Task Create_Post_BlankNullableStrings_StoreAsNull()
    {
        var b = BuildAdmin();
        Zone? captured = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Zone>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Zone, Guid?, CancellationToken>((z, _, _) => captured = z)
            .ReturnsAsync(Guid.NewGuid());

        var vm = new ZoneCreateViewModel
        {
            WarehouseId = WhId, Code = "TEST", Name = "Test", Type = "Storage",
            Temperature = "  ", AllowedProductTypes = "",
            IsActive = true,
        };
        await b.Controller.Create(vm, default);

        Assert.NotNull(captured);
        Assert.Null(captured!.Temperature);
        Assert.Null(captured.AllowedProductTypes);
    }

    [Fact]
    public async Task Create_Post_ValidatorFails_RepopulatesPicker()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ZoneCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Code", "Code 'RECV' already exists in this warehouse."),
            }));
        b.WarehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo> { new(WhId, "WH-MAIN", "Main") });

        var vm = new ZoneCreateViewModel { WarehouseId = WhId, Code = "RECV", Name = "Dup", Type = "Receiving" };
        var result = await b.Controller.Create(vm, default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(b.Controller.ModelState["Code"]!.Errors,
            e => e.ErrorMessage.Contains("already exists"));
        // Picker repopulated for re-render.
        var warehouses = Assert.IsAssignableFrom<IReadOnlyList<WarehouseInfo>>(view.ViewData["Warehouses"]);
        Assert.Single(warehouses);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Zone?)null);

        var result = await b.Controller.Edit(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesWarehouseDisplay()
    {
        var b = BuildAdmin();
        var id = Guid.NewGuid();
        var entity = new Zone
        {
            Id = id, WarehouseId = WhId, Code = "PICK-A", Name = "Aisle A",
            Type = "Picking", IsActive = true,
        };
        b.Repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        b.WarehouseRepo.Setup(r => r.GetByIdAsync(WhId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Warehouse { Id = WhId, Code = "WH-MAIN", Name = "Main Warehouse" });

        var result = await b.Controller.Edit(id, default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<ZoneEditViewModel>(view.Model);
        Assert.Equal(id, vm.Id);
        Assert.Equal(WhId, vm.WarehouseId);
        Assert.Equal("WH-MAIN", vm.WarehouseCode);
        Assert.Equal("Main Warehouse", vm.WarehouseName);
        Assert.Equal("PICK-A", vm.Code);
        Assert.Equal("Picking", vm.Type);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Zone>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new ZoneEditViewModel
        {
            Id = Guid.NewGuid(), WarehouseId = WhId, Code = "X", Name = "X", Type = "Storage", IsActive = true,
        };
        var result = await b.Controller.Edit(Guid.NewGuid(), vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_Happy_PreservesWarehouseAndCode()
    {
        // Critical invariant — WarehouseId and Code are locked on Edit.
        // Verify the entity sent to UpdateAsync carries them verbatim
        // from the route + locked VM hidden fields.
        var b = BuildAdmin();
        Zone? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Zone>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Zone, Guid?, CancellationToken>((z, _, _) => captured = z)
            .ReturnsAsync(true);

        var id = Guid.NewGuid();
        var vm = new ZoneEditViewModel
        {
            Id = id,
            WarehouseId = WhId,
            Code = "PICK-A",
            Name = "Aisle A (renamed)",
            Type = "Picking",
            Temperature = "Chilled",
            LotCommingleAllowed = false,
            IsActive = true,
        };
        var result = await b.Controller.Edit(id, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal(id, redirect.RouteValues!["id"]);

        Assert.NotNull(captured);
        Assert.Equal(id, captured!.Id);
        Assert.Equal(WhId, captured.WarehouseId);
        Assert.Equal("PICK-A", captured.Code);
        Assert.Equal("Aisle A (renamed)", captured.Name);
        Assert.Equal("Chilled", captured.Temperature);
        Assert.False(captured.LotCommingleAllowed);
    }

    [Fact]
    public async Task Warehouses_Endpoint_ReturnsJson()
    {
        var b = BuildAdmin();
        b.WarehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo>
            {
                new(WhId, "WH-MAIN", "Main"),
            });

        var result = await b.Controller.Warehouses(default);
        Assert.IsType<JsonResult>(result);
    }
}
