using FluentValidation.TestHelper;
using Moq;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;
using WMS.Web.Models.Master;
using WMS.Web.Validators.Master;

namespace WMS.IntegrationTests.Validators;

// Phase 7F focused unit tests for CustomerCreateValidator's
// distinguishing rule — the B2B cross-field requirement that
// CompanyName + TaxId are required when CustomerType=='B2B'.
// Migration 018 explicitly defers this from DB CHECK to BLL —
// FV is the enforcement point.
public class CustomerCreateValidatorTests
{
    private static CustomerCreateValidator BuildValidator(bool codeAlreadyExists = false)
    {
        var repo = new Mock<ICustomerRepository>();
        repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codeAlreadyExists ? new Customer { Code = "TAKEN" } : null);
        var factory = new Mock<ICustomerRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(Guid.NewGuid());
        return new CustomerCreateValidator(factory.Object, tenant.Object);
    }

    [Fact]
    public async Task B2B_MissingCompanyName_FailsValidation()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "B2B-1", Name = "Acme",
            CustomerType = "B2B",
            CompanyName = null,
            TaxId = "TAX-1",
            Status = "Active",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldHaveValidationErrorFor(x => x.CompanyName);
    }

    [Fact]
    public async Task B2B_MissingTaxId_FailsValidation()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "B2B-1", Name = "Acme",
            CustomerType = "B2B",
            CompanyName = "Acme Inc",
            TaxId = null,
            Status = "Active",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldHaveValidationErrorFor(x => x.TaxId);
    }

    [Fact]
    public async Task B2C_NoCompanyOrTaxId_PassesValidation()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "B2C-1", Name = "Walk-in",
            CustomerType = "B2C",
            CompanyName = null,
            TaxId = null,
            Status = "Active",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldNotHaveValidationErrorFor(x => x.CompanyName);
        result.ShouldNotHaveValidationErrorFor(x => x.TaxId);
    }

    [Fact]
    public async Task B2B_BothFieldsPresent_PassesValidation()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "B2B-1", Name = "Acme",
            CustomerType = "B2B",
            CompanyName = "Acme Inc",
            TaxId = "TAX-1",
            Status = "Active",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldNotHaveValidationErrorFor(x => x.CompanyName);
        result.ShouldNotHaveValidationErrorFor(x => x.TaxId);
    }

    [Fact]
    public async Task DuplicateCode_FailsValidation()
    {
        var validator = BuildValidator(codeAlreadyExists: true);
        var vm = new CustomerCreateViewModel
        {
            Code = "TAKEN", Name = "Acme",
            CustomerType = "B2C",
            Status = "Active",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public async Task UniqueCode_PassesValidation()
    {
        var validator = BuildValidator(codeAlreadyExists: false);
        var vm = new CustomerCreateViewModel
        {
            Code = "FRESH", Name = "Acme",
            CustomerType = "B2C",
            Status = "Active",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Inactive")]
    [InlineData("Suspended")]
    [InlineData("Draft")]
    public async Task ValidStatusValues_Pass(string status)
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "X", Name = "X", CustomerType = "B2C", Status = status,
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public async Task InvalidStatusValue_Fails()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "X", Name = "X", CustomerType = "B2C",
            Status = "Bogus",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public async Task NullTier_Passes()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "X", Name = "X", CustomerType = "B2C", Status = "Active",
            CustomerTier = null,
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomerTier);
    }

    [Fact]
    public async Task UnknownTier_Fails()
    {
        var validator = BuildValidator();
        var vm = new CustomerCreateViewModel
        {
            Code = "X", Name = "X", CustomerType = "B2C", Status = "Active",
            CustomerTier = "PlatinumElite",
        };

        var result = await validator.TestValidateAsync(vm);
        result.ShouldHaveValidationErrorFor(x => x.CustomerTier);
    }
}
