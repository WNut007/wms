using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;
using WMS.Web.Controllers;
using WMS.Web.Models.Master;
using WMS.Web.Services.Storage;
using WMS.Web.ViewModels.Detail;

namespace WMS.IntegrationTests.Controllers;

public class CustomersControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static (CustomersController Controller, Mock<ICustomerRepository> Repo)
        Build()
    {
        var repo    = new Mock<ICustomerRepository>();
        var factory = new Mock<ICustomerRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>(d =>
            d.ListByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<DocumentMetadata>()));

        // Phase 7 admin write-side dependencies — defaulted for read-side
        // tests; Phase 7F admin tests will introduce richer setup.
        var carrierRepo = new Mock<ICarrierRepository>();
        carrierRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LookupItem>());
        var carrierFactory = new Mock<ICarrierRepositoryFactory>();
        carrierFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(carrierRepo.Object);

        var createValidator = new Mock<IValidator<CustomerCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CustomerCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult());

        var editValidator = new Mock<IValidator<CustomerEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CustomerEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FluentValidation.Results.ValidationResult());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(Guid.NewGuid());

        return (new CustomersController(
                    factory.Object,
                    tenant.Object,
                    docs,
                    carrierFactory.Object,
                    createValidator.Object,
                    editValidator.Object,
                    currentUser.Object),
                repo);
    }

    private static CustomerListRow Row(string code, string status = "Active",
        string type = "B2C", string country = "Thailand") =>
        new(
            Id:           Guid.NewGuid(),
            Code:         code,
            Name:         $"Customer {code}",
            CustomerType: type,
            Country:      country,
            Email:        $"{code.ToLowerInvariant()}@example.com",
            Status:       status,
            UpdatedAt:    DateTime.UtcNow.AddHours(-5));

    private static Customer Entity(string code, string status = "Active",
        string type = "B2C", string? country = "Thailand", string? email = null) =>
        new()
        {
            Id           = Guid.NewGuid(),
            Code         = code,
            Name         = $"Customer {code}",
            CustomerType = type,
            Country      = country,
            Email        = email ?? $"{code.ToLowerInvariant()}@example.com",
            Phone        = "+66 2 555 0100",
            CompanyName  = type == "B2B" ? $"{code} Co Ltd" : null,
            TaxId        = type == "B2B" ? "0107537000000" : null,
            Status       = status,
            CreatedAt    = DateTime.UtcNow.AddDays(-90),
        };

    [Fact]
    public async Task GetData_DefaultParams_PassesPageOneAndDefaults()
    {
        var (ctrl, repo) = Build();
        CustomerFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<CustomerFilter>(), It.IsAny<CancellationToken>()))
            .Callback<CustomerFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<CustomerListRow> { Items = new() });

        await ctrl.GetData();

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Page);
        Assert.Equal(20, captured.PageSize);
        Assert.Null(captured.Status);
        Assert.Null(captured.CustomerType);
        Assert.Null(captured.Country);
    }

    [Theory]
    [InlineData("active",    "Active")]
    [InlineData("inactive",  "Inactive")]
    [InlineData("suspended", "Suspended")]
    [InlineData("pending",   "Draft")]      // mock vocabulary
    public async Task GetData_StatusFilter_MappedToPascalCase(string wire, string expected)
    {
        var (ctrl, repo) = Build();
        CustomerFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<CustomerFilter>(), It.IsAny<CancellationToken>()))
            .Callback<CustomerFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<CustomerListRow> { Items = new() });

        await ctrl.GetData(status: wire);

        Assert.Equal(expected, captured!.Status);
    }

    [Theory]
    [InlineData("any",  null)]    // customer view's country sentinel
    [InlineData("all",  null)]    // generic sentinel
    [InlineData(null,   null)]
    [InlineData("",     null)]
    [InlineData("Thailand", "Thailand")]
    public async Task GetData_CountryFilter_HandlesBothSentinels(string? wire, string? expected)
    {
        var (ctrl, repo) = Build();
        CustomerFilter? captured = null;
        repo.Setup(r => r.GetPagedAsync(It.IsAny<CustomerFilter>(), It.IsAny<CancellationToken>()))
            .Callback<CustomerFilter, CancellationToken>((f, _) => captured = f)
            .ReturnsAsync(new PagedResult<CustomerListRow> { Items = new() });

        await ctrl.GetData(country: wire);

        Assert.Equal(expected, captured!.Country);
    }

    [Fact]
    public async Task GetData_JsonShape_HasExpectedKeys()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<CustomerFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CustomerListRow>
            {
                Items = new() { Row("CUST-0001") },
                Total = 1,
                Page = 1,
                PageSize = 20,
                TotalPages = 1,
            });

        var result = (JsonResult)await ctrl.GetData();
        var json   = System.Text.Json.JsonSerializer.Serialize(result.Value);

        // Every key the JS list view reads (Index.cshtml lines 178-193)
        foreach (var key in new[] { "code", "name", "type", "country",
                                    "email", "initials", "avatarColor",
                                    "totalOrders", "lastOrderDate",
                                    "lastOrderRelative", "status" })
        {
            Assert.Contains($"\"{key}\":", json);
        }
        // TD-011 stubs flow through unchanged.
        Assert.Contains("\"totalOrders\":null", json);
        Assert.Contains("\"lastOrderDate\":null", json);
        Assert.Contains("\"lastOrderRelative\":\"\\u2014\"", json);    // em-dash is —
    }

    [Fact]
    public async Task GetData_StatusInResponse_UsesMockVocabulary()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<CustomerFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CustomerListRow>
            {
                // DB 'Draft' surfaces as wire 'pending' to keep mock-era
                // UI chips working.
                Items = new() { Row("CUST-0025", status: "Draft") },
            });

        var result = (JsonResult)await ctrl.GetData();
        var json   = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.Contains("\"status\":\"pending\"", json);
        Assert.DoesNotContain("\"status\":\"Draft\"", json);
    }

    [Fact]
    public async Task GetData_AvatarFields_ComputedFromName()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetPagedAsync(It.IsAny<CustomerFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CustomerListRow>
            {
                Items = new()
                {
                    Row("CUST-0001") with { Name = "Ananya Srisuk" },
                },
            });

        var result = (JsonResult)await ctrl.GetData();
        var json   = System.Text.Json.JsonSerializer.Serialize(result.Value);

        // CustomerAvatar.Initials("Ananya Srisuk") → "AS"
        Assert.Contains("\"initials\":\"AS\"", json);
        // Color comes from the 8-entry palette; just check format.
        Assert.Matches("\"avatarColor\":\"#[0-9A-F]{6}\"", json);
    }

    [Fact]
    public async Task Detail_UnknownCode_ReturnsNotFound()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetByCodeAsync("CUST-9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var result = await ctrl.Detail("CUST-9999", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_B2BCustomer_ShowsRealTaxIdAndPhone()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetByCodeAsync("CUST-0018", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity("CUST-0018", type: "B2B"));

        var result = await ctrl.Detail("CUST-0018", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        // Phone + Tax ID now come from real entity, not hardcoded
        // strings like the mock used.
        Assert.Contains(vm.OverviewFields, f => f.Key == "Phone" && f.Value.Contains("+66 2 555 0100"));
        Assert.Contains(vm.OverviewFields, f => f.Key == "Tax ID" && f.Value.Contains("0107537000000"));
    }

    [Fact]
    public async Task Detail_B2CCustomer_TaxIdAndCompanyAreEmDash()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetByCodeAsync("CUST-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity("CUST-0001", type: "B2C"));

        var result = await ctrl.Detail("CUST-0001", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        // B2C customers have no TaxId on the schema (NULL).
        Assert.Contains(vm.OverviewFields, f => f.Key == "Tax ID" && f.Value == "—");
    }

    [Fact]
    public async Task Detail_StatTiles_AllStubbed_TD011()
    {
        var (ctrl, repo) = Build();
        repo.Setup(r => r.GetByCodeAsync("CUST-0001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entity("CUST-0001"));

        var result = await ctrl.Detail("CUST-0001", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<DetailPageViewModel>(view.Model);
        // No orders schema yet — every stat tile is "—" (TD-011).
        // When orders land, this regression will fail and force a
        // deliberate update.
        Assert.All(vm.Stats, s => Assert.Equal("—", s.Value));
    }
}
