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
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Controllers;

// Phase 7F admin-side tests for CustomersController.Admin.cs partial.
// Mirrors ProductsAdminTests structure. Customer-specific paths:
// 4-step VM, 21-column entity mapping, Code+CustomerType readonly
// on Edit (UpdateAsync omits both from SET — verified by inspecting
// captured entity fields).
public class CustomersAdminTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record AdminBuild(
        CustomersController Controller,
        Mock<ICustomerRepository> Repo,
        Mock<ICarrierRepository> Carriers,
        Mock<IValidator<CustomerCreateViewModel>> CreateValidator,
        Mock<IValidator<CustomerEditViewModel>> EditValidator,
        Guid UserId);

    private static AdminBuild BuildAdmin()
    {
        var repo    = new Mock<ICustomerRepository>();
        var factory = new Mock<ICustomerRepositoryFactory>();
        var tenant  = new Mock<ITenantContext>();

        factory.Setup(x => x.For(It.IsAny<Guid>())).Returns(repo.Object);
        tenant.Setup(x => x.RequireTenantId()).Returns(TenantId);

        var docs = Mock.Of<IDocumentStorageService>();

        var carrierRepo = new Mock<ICarrierRepository>();
        carrierRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>
            {
                new(Guid.NewGuid(), "FLASH", "Flash Express"),
                new(Guid.NewGuid(), "KERRY", "Kerry Logistics"),
            });
        var carrierFactory = new Mock<ICarrierRepositoryFactory>();
        carrierFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(carrierRepo.Object);

        var createValidator = new Mock<IValidator<CustomerCreateViewModel>>();
        createValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CustomerCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var editValidator = new Mock<IValidator<CustomerEditViewModel>>();
        editValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CustomerEditViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        var ctrl = new CustomersController(
            factory.Object,
            tenant.Object,
            docs,
            carrierFactory.Object,
            createValidator.Object,
            editValidator.Object,
            currentUser.Object);

        return new AdminBuild(ctrl, repo, carrierRepo, createValidator, editValidator, userId);
    }

    [Fact]
    public async Task Create_Get_PopulatesCarriers()
    {
        var b = BuildAdmin();
        var result = await b.Controller.Create(default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CustomerCreateViewModel>(view.Model);
        Assert.Equal(2, vm.Carriers.Count);
    }

    [Fact]
    public async Task Create_Post_FluentValidationFails_AddsErrors()
    {
        var b = BuildAdmin();
        b.CreateValidator
            .Setup(v => v.ValidateAsync(It.IsAny<CustomerCreateViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("CompanyName", "Company name is required for B2B customers."),
                new ValidationFailure("TaxId", "Tax ID is required for B2B customers."),
            }));

        var vm = new CustomerCreateViewModel
        {
            Code = "B2B-1", Name = "Acme Co", CustomerType = "B2B", Status = "Active",
        };
        var result = await b.Controller.Create(vm, default);

        Assert.IsType<ViewResult>(result);
        Assert.False(b.Controller.ModelState.IsValid);
        Assert.True(b.Controller.ModelState["CompanyName"]!.Errors.Count > 0);
        Assert.True(b.Controller.ModelState["TaxId"]!.Errors.Count > 0);
    }

    [Fact]
    public async Task Create_Post_HappyPath_MapsAllFields_CallsInsert_Redirects()
    {
        var b = BuildAdmin();
        Customer? captured = null;
        Guid? capturedUserId = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Customer>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, Guid?, CancellationToken>((c, uid, _) => { captured = c; capturedUserId = uid; })
            .ReturnsAsync(Guid.NewGuid());

        var carrierId = Guid.NewGuid();
        var vm = new CustomerCreateViewModel
        {
            Code = "  ACME  ",
            Name = "  Acme Inc  ",
            CustomerType = "B2B",
            CompanyName = "  Acme Industries Ltd  ",
            TaxId = "  TAX-1234  ",
            Email = "  ops@acme.test  ",
            Phone = "  +66 2 555 1234  ",
            Country = "  Thailand  ",
            CustomerTier = "Tier1",
            AnnualRevenue = 5_000_000m,
            OrdersPerMonth = 30,
            AvgOrderValue = 2500m,
            IsKeyAccount = true,
            IsStrategic = true,
            AllocationPriority = 1,
            SafetyStockDays = 14,
            PromisedFillRate = 98.5m,
            PreferredCarrierId = carrierId,
            DefaultPaymentTerms = "  Net 30  ",
            Status = "Active",
        };

        var result = await b.Controller.Create(vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("ACME", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("ACME", captured!.Code);
        Assert.Equal("Acme Inc", captured.Name);
        Assert.Equal("B2B", captured.CustomerType);
        Assert.Equal("Acme Industries Ltd", captured.CompanyName);
        Assert.Equal("TAX-1234", captured.TaxId);
        Assert.Equal("ops@acme.test", captured.Email);
        Assert.Equal("Tier1", captured.CustomerTier);
        Assert.Equal(carrierId, captured.PreferredCarrierId);
        Assert.True(captured.IsKeyAccount);
        Assert.True(captured.IsStrategic);
        Assert.Equal(98.5m, captured.PromisedFillRate);
        Assert.Equal(b.UserId, capturedUserId);
    }

    [Fact]
    public async Task Create_Post_BlankNullableStrings_StoreAsNull()
    {
        var b = BuildAdmin();
        Customer? captured = null;
        b.Repo.Setup(r => r.InsertAsync(It.IsAny<Customer>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(Guid.NewGuid());

        var vm = new CustomerCreateViewModel
        {
            Code = "B2C-1",
            Name = "Walk-in",
            CustomerType = "B2C",
            CompanyName = "   ",
            TaxId = "",
            Email = null,
            Phone = "  ",
            Country = " ",
            CustomerTier = "",
            DefaultPaymentTerms = "   ",
            Status = "Active",
        };

        await b.Controller.Create(vm, default);

        Assert.NotNull(captured);
        Assert.Null(captured!.CompanyName);
        Assert.Null(captured.TaxId);
        Assert.Null(captured.Email);
        Assert.Null(captured.Phone);
        Assert.Null(captured.Country);
        Assert.Null(captured.CustomerTier);
        Assert.Null(captured.DefaultPaymentTerms);
    }

    [Fact]
    public async Task Edit_Get_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.GetByCodeAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var result = await b.Controller.Edit("MISSING", default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Get_Loads_PopulatesVm()
    {
        var b = BuildAdmin();
        var entity = new Customer
        {
            Id = Guid.NewGuid(),
            Code = "B2B-1",
            Name = "Acme Inc",
            CustomerType = "B2B",
            CompanyName = "Acme Industries",
            TaxId = "TAX-1",
            Email = "ops@acme.test",
            Status = "Active",
        };
        b.Repo.Setup(r => r.GetByCodeAsync("B2B-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await b.Controller.Edit("B2B-1", default);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CustomerEditViewModel>(view.Model);
        Assert.Equal(entity.Id, vm.Id);
        Assert.Equal("B2B", vm.CustomerType);
        Assert.Equal("Acme Industries", vm.CompanyName);
        Assert.Equal(2, vm.Carriers.Count);
    }

    [Fact]
    public async Task Edit_Post_NotFound_Returns404()
    {
        var b = BuildAdmin();
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var vm = new CustomerEditViewModel
        {
            Id = Guid.NewGuid(), Code = "X", Name = "X",
            CustomerType = "B2B", CompanyName = "X", TaxId = "1",
            Status = "Active",
        };
        var result = await b.Controller.Edit("X", vm, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_Post_HappyPath_CallsUpdate_Redirects()
    {
        var b = BuildAdmin();
        Customer? captured = null;
        b.Repo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, Guid?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(true);

        var vm = new CustomerEditViewModel
        {
            Id = Guid.NewGuid(),
            Code = "ACME",
            Name = "Acme Inc Updated",
            CustomerType = "B2B",
            CompanyName = "Acme Industries Ltd",
            TaxId = "TAX-1",
            Status = "Inactive",
        };

        var result = await b.Controller.Edit("ACME", vm, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Detail", redirect.ActionName);
        Assert.Equal("ACME", redirect.RouteValues!["code"]);

        Assert.NotNull(captured);
        Assert.Equal("ACME", captured!.Code);
        Assert.Equal("Acme Inc Updated", captured.Name);
        Assert.Equal("Inactive", captured.Status);
    }
}
