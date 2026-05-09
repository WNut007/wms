using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Inventory;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.Web.Controllers;
using WMS.Web.Models.Master;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

public class WarehousesControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static (WarehousesController Controller,
                    Mock<IWarehouseRepository> Repo,
                    Mock<IReceivingHeaderRepository> Receiving,
                    Mock<IStockMovementRepository> Movements)
        Build()
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

        // Defaults: empty activity feeds — Detail tests that don't
        // care about activity see the empty-state path.
        receiving.Setup(r => r.GetActivityByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ReceivingActivityRow>());
        movements.Setup(m => m.GetByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockMovementListRow>());

        var docs = Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        // Phase 7 admin write-side dependencies — defaulted for read-side
        // tests; Phase 7F admin tests will introduce richer setup.
        var createValidator = new Mock<IValidator<WarehouseCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<WarehouseCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult());

        var editValidator = new Mock<IValidator<WarehouseEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<WarehouseEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());

        return (new WarehousesController(
                    factory.Object,
                    receivingFactory.Object,
                    movementFactory.Object,
                    tenant.Object,
                    docs,
                    createValidator.Object,
                    editValidator.Object,
                    currentUser.Object),
                repo, receiving, movements);
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-NOPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WarehouseListRow?)null);

        var result = await ctrl.Detail("WH-NOPE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_KnownCode_LocationCountIsReal()
    {
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
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
        var (ctrl, repo, _, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM06", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM06", isActive: false));

        var result = await ctrl.Detail("WH-DM06", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Equal("Inactive", vm.StatusLabel);
        Assert.Equal("neutral", vm.StatusVariant);
    }

    // ---- Activity tab — Phase 6E (closes TD-014 Warehouse half) ----

    [Fact]
    public async Task Detail_NoActivity_ActivitiesEmpty_TD014WarehouseHalfClosed()
    {
        // Phase 6E removed the 4 hardcoded mock activities. Empty
        // feed renders the panel's existing "No activity yet." state.
        var (ctrl, repo, _, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01"));

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Empty(vm.Activities);
    }

    [Fact]
    public async Task Detail_FetchesBothFeeds_WithWarehouseIdFromListRow()
    {
        // Pin the contract: both repos look up by the resolved
        // Warehouse Id (from GetListRowByCodeAsync), NOT by code.
        // Catches accidental code-string passing.
        var (ctrl, repo, receiving, movements) = Build();
        var warehouseId = Guid.NewGuid();
        Guid? capturedReceiving = null;
        Guid? capturedMovements = null;

        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01") with { Id = warehouseId });
        receiving.Setup(r => r.GetActivityByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, int, CancellationToken>((id, _, _) => capturedReceiving = id)
            .ReturnsAsync(Array.Empty<ReceivingActivityRow>());
        movements.Setup(m => m.GetByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime?, int, CancellationToken>((id, _, _, _) => capturedMovements = id)
            .ReturnsAsync(Array.Empty<StockMovementListRow>());

        await ctrl.Detail("WH-DM01", CancellationToken.None);

        Assert.Equal(warehouseId, capturedReceiving);
        Assert.Equal(warehouseId, capturedMovements);
    }

    [Fact]
    public async Task Detail_WithReceiptsOnly_ActivitiesMappedFromReceivingRepo()
    {
        var (ctrl, repo, receiving, _) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01"));
        receiving.Setup(r => r.GetActivityByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ReceivingActivityRow(
                    Id:               Guid.NewGuid(),
                    ReceivingNumber:  "RC-2026-0001",
                    ReceivedAt:       DateTime.UtcNow.AddHours(-2),
                    Status:           "Posted",
                    PerformedByName:  "Maya Rodriguez",
                    LineCount:        3),
            });

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        var only = Assert.Single(vm.Activities);
        Assert.Contains("posted receipt RC-2026-0001", only.Title);
        Assert.Equal("3 lines", only.Description);
        Assert.Equal("ti-truck-delivery", only.IconClass);
    }

    [Fact]
    public async Task Detail_WithMovementsOnly_ActivitiesMappedFromMovementRepo()
    {
        var (ctrl, repo, _, movements) = Build();
        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01"));
        movements.Setup(m => m.GetByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StockMovementListRow(
                    Id:               Guid.NewGuid(),
                    MovementType:     StockMovementType.Receive,
                    QuantityDelta:    5m,
                    FromLocationCode: null,
                    ToLocationCode:   "RECV-01",
                    ReferenceType:    "ReceivingLine",
                    Notes:            null,
                    PerformedByName:  "Maya Rodriguez",
                    PerformedAt:      DateTime.UtcNow.AddHours(-1)),
            });

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        var only = Assert.Single(vm.Activities);
        Assert.Contains("received 5 units", only.Title);
        Assert.Contains(" at RECV-01", only.Title);
        Assert.Equal("ti-package-import", only.IconClass);
    }

    [Fact]
    public async Task Detail_WithBothSources_MergedAndSortedByTimestampDesc()
    {
        var (ctrl, repo, receiving, movements) = Build();
        var now = DateTime.UtcNow;

        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01"));
        receiving.Setup(r => r.GetActivityByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                // Older receipt
                new ReceivingActivityRow(
                    Guid.NewGuid(), "RC-A", now.AddHours(-5),
                    "Posted", "Maya", 1),
                // Newer receipt
                new ReceivingActivityRow(
                    Guid.NewGuid(), "RC-B", now.AddHours(-1),
                    "Posted", "Maya", 1),
            });
        movements.Setup(m => m.GetByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                // Between the two receipts
                new StockMovementListRow(
                    Guid.NewGuid(), StockMovementType.Receive, 5m,
                    null, "RECV-01", "ReceivingLine", null,
                    "Maya", now.AddHours(-3)),
            });

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Equal(3, vm.Activities.Count);
        // Newest first — interleaving works.
        Assert.Contains("RC-B",     vm.Activities[0].Title);
        Assert.Contains("received", vm.Activities[1].Title);
        Assert.Contains("RC-A",     vm.Activities[2].Title);
    }

    [Fact]
    public async Task Detail_FeedCappedAtTwenty_AcrossBothSources()
    {
        // 15 receipts + 15 movements all arriving roughly together —
        // merged feed must clamp to 20 entries (ActivityFeedLimit).
        var (ctrl, repo, receiving, movements) = Build();
        var now = DateTime.UtcNow;

        repo.Setup(r => r.GetListRowByCodeAsync("WH-DM01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row("WH-DM01"));
        receiving.Setup(r => r.GetActivityByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 15).Select(i =>
                new ReceivingActivityRow(
                    Guid.NewGuid(), $"RC-{i:D4}", now.AddMinutes(-i),
                    "Posted", "Maya", 1)).ToArray());
        movements.Setup(m => m.GetByWarehouseAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 15).Select(i =>
                new StockMovementListRow(
                    Guid.NewGuid(), StockMovementType.Receive, 1m,
                    null, "RECV-01", null, null,
                    "Maya", now.AddSeconds(-i * 30))).ToArray());

        var result = await ctrl.Detail("WH-DM01", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        Assert.Equal(20, vm.Activities.Count);
        // Order is timestamp DESC; first entry is whichever source had
        // the newest timestamp.
        Assert.Equal(vm.Activities.OrderByDescending(a => a.Timestamp).Select(a => a.Timestamp),
                     vm.Activities.Select(a => a.Timestamp));
    }
}
