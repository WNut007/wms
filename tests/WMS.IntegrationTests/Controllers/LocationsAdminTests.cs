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

// Phase 30A.3 Block 2.2 admin-side tests for LocationsController.
// Coverage:
//   * Composite-key uniqueness (WarehouseId, Code).
//   * Cascade picker: Warehouse → ZonesForWarehouse JSON endpoint.
//   * 3 orthogonal states (Status enum / IsActive bool / IsPickface).
//   * Edit keyed by Guid (Code not tenant-unique).
public class LocationsAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid WhId    = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid ZoneId  = Guid.Parse("00000000-0000-0000-0000-000000000020");

    private record AdminBuild(
        LocationsController Controller,
        Mock<ILocationRepository> Repo,
        Mock<IWarehouseRepository> WarehouseRepo,
        Mock<IValidator<LocationCreateViewModel>> CreateValidator,
        Mock<IValidator<LocationEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<ILocationRepository>();
        var factory = new Mock<ILocationRepositoryFactory>();
        var whRepo  = new Mock<IWarehouseRepository>();
        var whFactory = new Mock<IWarehouseRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        whFactory.Setup(x => x.For(It.IsAny<Guid>())).Returns(whRepo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        whRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<WarehouseInfo>());
        repo.Setup(r => r.GetZonesForWarehouseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>());

        var createValidator = new Mock<IValidator<LocationCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<LocationCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<LocationEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<LocationEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new LocationsController(
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
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<LocationFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<LocationListRow>
            {
                Items = new List<LocationListRow>
                {
                    new(Guid.NewGuid(), WhId, "WH-MAIN", ZoneId, "PICK-A", "Picking",
                        "A-12-3", "Aisle 12 Bin 3", "NoLimit", 50, true, "Active", true,
                        0, DateTime.UtcNow, null)
                },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationStatusCounts(
                All: 1, Active: 1, Inactive: 0,
                StatusActive: 1, StatusBlocked: 0, StatusMaintenance: 0, Pickfaces: 1));

        var result = await b.Controller.GetData(ct: default);
        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Theory]
    [InlineData("Active",      "Active")]
    [InlineData("Blocked",     "Blocked")]
    [InlineData("Maintenance", "Maintenance")]
    [InlineData("all",  null)]
    [InlineData("",     null)]
    [InlineData(null,   null)]
    [InlineData("bogus", null)]
    public async Task GetData_StatusFilter_NormalisesToValidEnumOrNull(string? wireStatus, string? expectedDb)
    {
        var b = BuildAdmin();
        LocationFilter? captured = null;
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<LocationFilter>(), It.IsAny<CancellationToken>()))
            .Callback<LocationFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<LocationListRow>
            {
                Items = new List<LocationListRow>(),
                Total = 0, Page = 1, PageSize = 20, TotalPages = 0
            });
        b.Repo.Setup(r => r.GetStatusCountsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationStatusCounts(0, 0, 0, 0, 0, 0, 0));

        await b.Controller.GetData(status: wireStatus, ct: default);
        Assert.NotNull(captured);
        Assert.Equal(expectedDb, captured!.Status);
    }

    [Fact]
    public async Task ZonesForWarehouse_Endpoint_ReturnsJson()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetZonesForWarehouseAsync(WhId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(ZoneId, "PICK-A", "Picking Aisle A"),
            });

        var result = await b.Controller.ZonesForWarehouse(WhId, default);
        Assert.IsType<JsonResult>(result);
    }

    [Fact]
    public async Task Create_Get_PopulatesWarehousePicker_EmptyZones()
    {
        var b = BuildAdmin();
        b.WarehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo> { new(WhId, "WH-MAIN", "Main") });

        var result = await b.Controller.Create(default);
        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<LocationCreateViewModel>(view.Model);
        var warehouses = Assert.IsAssignableFrom<IReadOnlyList<WarehouseInfo>>(view.ViewData["Warehouses"]);
        Assert.Single(warehouses);
        // Zones start empty — user picks warehouse first, JS fetches zones.
        var zones = Assert.IsAssignableFrom<IReadOnlyCollection<LookupItem>>(view.ViewData["Zones"]);
        Assert.Empty(zones);
    }

    [Fact]
    public async Task Create_Post_Happy_CallsInsert_RedirectsToEditWithId()
    {
        var b = BuildAdmin();
        Location? captured = null;
        var newId = Guid.NewGuid();
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Location>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Location, Guid?, CancellationToken>((l, _, _) => captured = l)
            .ReturnsAsync(newId);

        var vm = new LocationCreateViewModel
        {
            WarehouseId = WhId,
            ZoneId = ZoneId,
            Code = "  A-12-3  ",
            Description = "  Aisle 12 Bin 3  ",
            CapacityPolicy = "NoLimit",
            BinRank = 50,
            IsPickface = true,
            Status = "Active",
            IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal(newId, redirect.RouteValues!["id"]);

        Assert.NotNull(captured);
        Assert.Equal(WhId, captured!.WarehouseId);
        Assert.Equal(ZoneId, captured.ZoneId);
        Assert.Equal("A-12-3", captured.Code);
        Assert.Equal("Aisle 12 Bin 3", captured.Description);
        Assert.Equal("NoLimit", captured.CapacityPolicy);
        Assert.Equal(50, captured.BinRank);
        Assert.True(captured.IsPickface);
        Assert.Equal("Active", captured.Status);
    }

    [Fact]
    public async Task Create_Post_BlankNullableStrings_StoreAsNull()
    {
        var b = BuildAdmin();
        Location? captured = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Location>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Location, Guid?, CancellationToken>((l, _, _) => captured = l)
            .ReturnsAsync(Guid.NewGuid());

        var vm = new LocationCreateViewModel
        {
            WarehouseId = WhId, ZoneId = ZoneId, Code = "MIN",
            Description = "  ", Aisle = "", DisplayColor = " ",
            CapacityPolicy = "NoLimit", Status = "Active", IsActive = true,
        };
        await b.Controller.Create(vm, default);

        Assert.NotNull(captured);
        Assert.Null(captured!.Description);
        Assert.Null(captured.Aisle);
        Assert.Null(captured.DisplayColor);
    }

    [Fact]
    public async Task Create_Post_ValidatorFails_RepopulatesPickers()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<LocationCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("Code", "Code 'A-12-3' already exists in this warehouse."),
            }));
        b.WarehouseRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WarehouseInfo> { new(WhId, "WH-MAIN", "Main") });
        b.Repo.Setup(r => r.GetZonesForWarehouseAsync(WhId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(ZoneId, "PICK-A", "Picking A") });

        var vm = new LocationCreateViewModel
        {
            WarehouseId = WhId, ZoneId = ZoneId, Code = "A-12-3",
            CapacityPolicy = "NoLimit", Status = "Active", IsActive = true,
        };
        var result = await b.Controller.Create(vm, default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains(b.Controller.ModelState["Code"]!.Errors,
            e => e.ErrorMessage.Contains("already exists"));
        var warehouses = Assert.IsAssignableFrom<IReadOnlyList<WarehouseInfo>>(view.ViewData["Warehouses"]);
        Assert.Single(warehouses);
        var zones = Assert.IsAssignableFrom<IReadOnlyList<LookupItem>>(view.ViewData["Zones"]);
        Assert.Single(zones);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Location?)null);

        var result = await b.Controller.Edit(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesZonesForCurrentWarehouse()
    {
        var b = BuildAdmin();
        var id = Guid.NewGuid();
        var entity = new Location
        {
            Id = id, WarehouseId = WhId, ZoneId = ZoneId, Code = "A-12-3",
            CapacityPolicy = "NoLimit", BinRank = 50, Status = "Active", IsActive = true,
        };
        b.Repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        b.WarehouseRepo.Setup(r => r.GetByIdAsync(WhId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Warehouse { Id = WhId, Code = "WH-MAIN", Name = "Main" });
        b.Repo.Setup(r => r.GetZonesForWarehouseAsync(WhId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem> { new(ZoneId, "PICK-A", "Picking A") });

        var result = await b.Controller.Edit(id, default);
        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<LocationEditViewModel>(view.Model);
        Assert.Equal(WhId, vm.WarehouseId);
        Assert.Equal("WH-MAIN", vm.WarehouseCode);
        Assert.Equal(ZoneId, vm.ZoneId);
        Assert.Equal("A-12-3", vm.Code);
        // Zones picker pre-populated with current-warehouse zones.
        var zones = Assert.IsAssignableFrom<IReadOnlyList<LookupItem>>(view.ViewData["Zones"]);
        Assert.Single(zones);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Location>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new LocationEditViewModel
        {
            Id = Guid.NewGuid(), WarehouseId = WhId, ZoneId = ZoneId, Code = "X",
            CapacityPolicy = "NoLimit", Status = "Active", IsActive = true,
        };
        var result = await b.Controller.Edit(Guid.NewGuid(), vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_Happy_PreservesWarehouseAndCode_StatusFlipsTo_Blocked()
    {
        // Critical invariant — WarehouseId and Code are locked on Edit.
        // Status IS editable. Verify the entity sent to UpdateAsync
        // carries WarehouseId + Code from hidden inputs and the new
        // Status value.
        var b = BuildAdmin();
        Location? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Location>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Location, Guid?, CancellationToken>((l, _, _) => captured = l)
            .ReturnsAsync(true);

        var id = Guid.NewGuid();
        var vm = new LocationEditViewModel
        {
            Id = id,
            WarehouseId = WhId,
            ZoneId = ZoneId,
            Code = "A-12-3",
            Description = "Updated description",
            CapacityPolicy = "ByWeight",
            BinRank = 75,
            Status = "Blocked",
            IsPickface = false,
            IsActive = true,
        };
        var result = await b.Controller.Edit(id, vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Edit", redirect.ActionName);
        Assert.Equal(id, redirect.RouteValues!["id"]);

        Assert.NotNull(captured);
        Assert.Equal(id, captured!.Id);
        Assert.Equal(WhId, captured.WarehouseId);
        Assert.Equal("A-12-3", captured.Code);
        Assert.Equal("ByWeight", captured.CapacityPolicy);
        Assert.Equal(75, captured.BinRank);
        Assert.Equal("Blocked", captured.Status);
        Assert.False(captured.IsPickface);
    }

    [Theory]
    [InlineData("Active",      "success")]
    [InlineData("Blocked",     "danger")]
    [InlineData("Maintenance", "warning")]
    [InlineData("Bogus",       "neutral")]
    public void StatusMapper_Variant_ReturnsCorrectCss(string status, string expected)
    {
        Assert.Equal(expected, LocationStatusMapper.Variant(status));
    }
}
