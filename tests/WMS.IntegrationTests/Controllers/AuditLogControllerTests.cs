using Microsoft.AspNetCore.Mvc;
using Moq;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Security;
using WMS.Web.Controllers;

namespace WMS.IntegrationTests.Controllers;

public class AuditLogControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private record Build(AuditLogController Controller, Mock<IAuditLogRepository> Repo);

    private static Build BuildController()
    {
        var repo = new Mock<IAuditLogRepository>();
        var factory = new Mock<IAuditLogRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        repo.Setup(r => r.GetPagedAsync(It.IsAny<AuditLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<AuditLogListRow>());
        repo.Setup(r => r.GetDistinctEventTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "UserCreated", "UserUpdated" });

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        return new Build(new AuditLogController(factory.Object, tenant.Object), repo);
    }

    [Fact]
    public async Task Index_ReturnsView_PopulatesEventTypes()
    {
        var b = BuildController();
        var view = Assert.IsType<ViewResult>(await b.Controller.Index());
        var eventTypes = Assert.IsAssignableFrom<IReadOnlyList<string>>(view.ViewData["EventTypes"]);
        Assert.Equal(2, eventTypes.Count);
    }

    [Fact]
    public async Task GetData_ReturnsJsonEnvelope()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetPagedAsync(It.IsAny<AuditLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<AuditLogListRow>
            {
                Items = new List<AuditLogListRow>
                {
                    new(Guid.NewGuid(), null, null, null,
                        "UserCreated", "User", Guid.NewGuid(),
                        "127.0.0.1", "test", null, DateTime.UtcNow),
                },
                Total = 1, Page = 1, PageSize = 50, TotalPages = 1,
            });

        var result = await b.Controller.GetData();
        Assert.IsType<JsonResult>(result);
    }

    [Fact]
    public async Task Detail_NotFound_Returns404()
    {
        var b = BuildController();
        b.Repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLogListRow?)null);

        var result = await b.Controller.Detail(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_Found_ReturnsViewWithRow()
    {
        var b = BuildController();
        var id = Guid.NewGuid();
        var row = new AuditLogListRow(id, null, null, null,
            "UserCreated", "User", Guid.NewGuid(),
            "127.0.0.1", "test", "{\"test\":true}", DateTime.UtcNow);
        b.Repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var view = Assert.IsType<ViewResult>(await b.Controller.Detail(id));
        Assert.Equal(row, view.Model);
    }
}
