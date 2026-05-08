using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.Web.Controllers;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

public class WarehousesControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static (WarehousesController Controller, Mock<IWarehouseRepository> Repo)
        Build()
    {
        var repo    = new Mock<IWarehouseRepository>();
        var factory = new Mock<IWarehouseRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        return (new WarehousesController(factory.Object, tenant.Object, docs), repo);
    }

    private static WarehouseListRow Row(
        string code,
        bool isActive = true,
        int locationCount = 42,
        string? address = "Bangkok, TH",
        string? managerName = null,
        string? phoneNumber = null,
        string type = "Main") =>
        new(
            Id:            Guid.NewGuid(),
            Code:          code,
            Name:          $"Warehouse {code}",
            Type:          type,
            IsActive:      isActive,
            LocationCount: locationCount,
            Address:       address,
            ManagerName:   managerName,
            PhoneNumber:   phoneNumber,
            CreatedAt:     DateTime.UtcNow.AddDays(-180),
            UpdatedAt:     DateTime.UtcNow.AddHours(-3));

    [Fact]
    public async Task GetData_DefaultParams_PassesPageOneAndDefaults()
    {
        var (ctrl, repo) = Build();
        WarehouseFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .Callback<WarehouseFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<WarehouseListRow> { Items = new() });

        await ctrl.GetData();

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Page);
        Assert.Equal(20, captured.PageSize);
        Assert.Null(captured.IsActive);
        Assert.Null(captured.Type);
    }

    [Theory]
    [InlineData("active",   true)]
    [InlineData("inactive", false)]
    public async Task GetData_StatusFilter_MapsToBool(string wire, bool expected)
    {
        var (ctrl, repo) = Build();
        WarehouseFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .Callback<WarehouseFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<WarehouseListRow> { Items = new() });

        await ctrl.GetData(status: wire);

        Assert.Equal(expected, captured!.IsActive);
    }

    [Theory]
    [InlineData("maintenance")]    // TD-009: dropped silently
    [InlineData("all")]
    [InlineData(null)]
    public async Task GetData_UnknownStatus_DropsFilter_TD009(string? wire)
    {
        var (ctrl, repo) = Build();
        WarehouseFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .Callback<WarehouseFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<WarehouseListRow> { Items = new() });

        await ctrl.GetData(status: wire);

        Assert.Null(captured!.IsActive);
    }

    [Fact]
    public async Task GetData_RegionFilter_IgnoredByController()
    {
        // Region has no schema column — controller absorbs the param
        // silently rather than passing it to the repo. Search field
        // can be used to narrow by Address/city if the user wants.
        var (ctrl, repo) = Build();
        WarehouseFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .Callback<WarehouseFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<WarehouseListRow> { Items = new() });

        await ctrl.GetData(region: "Bangkok, TH");

        // No Region field on the filter; repo just sees whatever else
        // was passed (nothing here).
        Assert.Null(captured!.Search);
        Assert.Null(captured.IsActive);
        Assert.Null(captured.Type);
    }

    [Theory]
    [InlineData("Main",   "Main")]
    [InlineData("all",    null)]
    [InlineData("",       null)]
    [InlineData(null,     null)]
    public async Task GetData_TypeFilter_NormaliseSentinels(string? wire, string? expected)
    {
        var (ctrl, repo) = Build();
        WarehouseFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .Callback<WarehouseFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<WarehouseListRow> { Items = new() });

        await ctrl.GetData(type: wire);

        Assert.Equal(expected, captured!.Type);
    }

    [Fact]
    public async Task GetData_JsonShape_HasExpectedKeys()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<WarehouseListRow>
            {
                Items = new() { Row("WH-DM01") },
                Total = 1, Page = 1, PageSize = 20, TotalPages = 1,
            });

        var result = (JsonResult)await ctrl.GetData();
        var json   = System.Text.Json.JsonSerializer.Serialize(result.Value);

        // Every key the JS list view reads (Index.cshtml).
        foreach (var key in new[] { "code", "name", "subtitle", "region",
                                    "type", "status", "locationCount",
                                    "updatedAt", "updatedAtRelative" })
        {
            Assert.Contains($"\"{key}\":", json);
        }
        // Subtitle dropped in real (no schema column).
        Assert.Contains("\"subtitle\":null", json);
        // Address surfaces as the wire 'region' field.
        Assert.Contains("\"region\":\"Bangkok, TH\"", json);
    }

    [Theory]
    [InlineData(true,  "active")]
    [InlineData(false, "inactive")]
    public async Task GetData_StatusInResponse_RoundTrips(bool isActive, string expected)
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<WarehouseFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<WarehouseListRow>
            {
                Items = new() { Row("WH-DM01", isActive: isActive) },
            });

        var result = (JsonResult)await ctrl.GetData();
        var json   = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.Contains($"\"status\":\"{expected}\"", json);
    }

    [Fact]
    public async Task Detail_UnknownCode_ReturnsNotFound()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-NOPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WarehouseListRow?)null);

        var result = await ctrl.Detail("WH-NOPE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_KnownCode_LocationCountIsReal()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01", locationCount: 142));

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        var locTile = vm.Stats.Single(s => s.Label == "Locations");
        Assert.Equal("142", locTile.Value);
        // Other 3 stat tiles still stubbed (no analytics yet).
        Assert.Equal("—", vm.Stats.Single(s => s.Label == "Capacity").Value);
        Assert.Equal("—", vm.Stats.Single(s => s.Label == "Active SKUs").Value);
        Assert.Equal("—", vm.Stats.Single(s => s.Label == "Avg dwell").Value);
    }

    [Fact]
    public async Task Detail_KnownCode_RealManagerAndPhoneFromEntity()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01",
                managerName: "Maya Rodriguez",
                phoneNumber: "+66 2 555 0123"));

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Contains(vm.OverviewFields, f => f.Key == "Manager" && f.Value.Contains("Maya Rodriguez"));
        Assert.Contains(vm.OverviewFields, f => f.Key == "Phone" && f.Value.Contains("+66 2 555 0123"));
    }

    [Fact]
    public async Task Detail_NullManagerAndPhone_RenderEmDash()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM06", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM06"));    // both null in defaults

        var result = await ctrl.Detail("WH-DM06", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Contains(vm.OverviewFields, f => f.Key == "Manager" && f.Value == "—");
        Assert.Contains(vm.OverviewFields, f => f.Key == "Phone" && f.Value == "—");
    }

    [Fact]
    public async Task Detail_InactiveWarehouse_StatusVariantNeutral()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM06", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM06", isActive: false));

        var result = await ctrl.Detail("WH-DM06", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Equal("Inactive", vm.StatusLabel);
        Assert.Equal("neutral", vm.StatusVariant);
    }
}
