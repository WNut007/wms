using Microsoft.Extensions.Caching.Memory;
using Moq;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.DAL.Repositories.Security;

namespace WMS.UnitTests.Services.Auth;

public class PermissionServiceTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();

    private static readonly UserPermission StockView =
        new("INVENTORY.STOCK", PermissionAction.View);
    private static readonly UserPermission StockEdit =
        new("INVENTORY.STOCK", PermissionAction.Edit);

    [Fact]
    public async Task HasPermission_WhenGranted_ReturnsTrue()
    {
        var sut = NewService(out _, new[] { StockView });

        var has = await sut.HasPermissionAsync(
            TestUserId, TestTenantId, "INVENTORY.STOCK", PermissionAction.View);

        Assert.True(has);
    }

    [Fact]
    public async Task HasPermission_WhenActionDiffers_ReturnsFalse()
    {
        // Function matches, action doesn't — no false positive across actions.
        var sut = NewService(out _, new[] { StockView });

        var has = await sut.HasPermissionAsync(
            TestUserId, TestTenantId, "INVENTORY.STOCK", PermissionAction.Edit);

        Assert.False(has);
    }

    [Fact]
    public async Task HasPermission_WhenFunctionMissing_ReturnsFalse()
    {
        var sut = NewService(out _, Array.Empty<UserPermission>());

        var has = await sut.HasPermissionAsync(
            TestUserId, TestTenantId, "INVENTORY.STOCK", PermissionAction.View);

        Assert.False(has);
    }

    [Fact]
    public async Task GetForUser_SecondCall_ServesFromCache()
    {
        // Strict factory-mock: a second call to For() would throw.
        var repo = new Mock<IPermissionRepository>();
        repo.Setup(r => r.GetForUserAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { StockView, StockEdit });

        var factory = new Mock<IPermissionRepositoryFactory>(MockBehavior.Strict);
        factory.Setup(f => f.For(TestTenantId)).Returns(repo.Object);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionService(factory.Object, cache);

        var first = await sut.GetForUserAsync(TestUserId, TestTenantId);
        var second = await sut.GetForUserAsync(TestUserId, TestTenantId);

        Assert.Equal(2, first.Count);
        Assert.Same(first, second);
        factory.Verify(f => f.For(TestTenantId), Times.Once);
        repo.Verify(r => r.GetForUserAsync(TestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetForUser_DifferentTenant_DoesNotCollideOnCache()
    {
        var otherTenant = Guid.NewGuid();
        var repo = new Mock<IPermissionRepository>();
        repo.Setup(r => r.GetForUserAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { StockView });

        var factory = new Mock<IPermissionRepositoryFactory>();
        factory.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        var sut = new PermissionService(factory.Object, new MemoryCache(new MemoryCacheOptions()));

        await sut.GetForUserAsync(TestUserId, TestTenantId);
        await sut.GetForUserAsync(TestUserId, otherTenant);

        // Same userId, different tenant — both cache misses, so the
        // factory is asked for a tenant-bound repo twice.
        factory.Verify(f => f.For(TestTenantId), Times.Once);
        factory.Verify(f => f.For(otherTenant), Times.Once);
    }

    private static PermissionService NewService(
        out Mock<IPermissionRepositoryFactory> factoryMock,
        IReadOnlyList<UserPermission> permsForUser)
    {
        var repo = new Mock<IPermissionRepository>();
        repo.Setup(r => r.GetForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permsForUser);

        factoryMock = new Mock<IPermissionRepositoryFactory>();
        factoryMock.Setup(f => f.For(It.IsAny<Guid>())).Returns(repo.Object);

        return new PermissionService(
            factoryMock.Object,
            new MemoryCache(new MemoryCacheOptions()));
    }
}
