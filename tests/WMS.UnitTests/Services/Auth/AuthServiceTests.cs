using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WMS.BLL.Services.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Security;

namespace WMS.UnitTests.Services.Auth;

// Covers the BCrypt-side primitives — the only AuthService methods that
// don't need a live SQL Server. PreAuthToken / LoginAttempts paths talk
// to master.* and belong in WMS.IntegrationTests once that suite stands
// up.
//
// Cost factor 4 throughout: keeps each round-trip well under a second
// while still producing genuine BCrypt MCF strings.
public class AuthServiceTests
{
    private const int TestCostFactor = 4;

    private static AuthService NewService() => new(
        Mock.Of<IUserRepositoryFactory>(),
        Mock.Of<IUserTenantMapRepository>(),
        Mock.Of<IMasterConnectionFactory>(),
        NullLogger<AuthService>.Instance,
        bcryptCostFactor: TestCostFactor);

    [Fact]
    public void HashPassword_VerifyPassword_RoundTrip()
    {
        var sut = NewService();
        const string password = "correct horse battery staple";

        var hash = sut.HashPassword(password);

        Assert.True(BCrypt.Net.BCrypt.Verify(password, hash));
    }

    [Fact]
    public void HashPassword_WrongPassword_FailsVerification()
    {
        var sut = NewService();
        var hash = sut.HashPassword("right-password");

        Assert.False(BCrypt.Net.BCrypt.Verify("wrong-password", hash));
    }

    [Fact]
    public void HashPassword_SamePassword_DifferentHashesEachCall()
    {
        // BCrypt embeds a random salt per call — two hashes of the same
        // password must differ but both must verify.
        var sut = NewService();
        const string password = "p@ssw0rd";

        var hashA = sut.HashPassword(password);
        var hashB = sut.HashPassword(password);

        Assert.NotEqual(hashA, hashB);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, hashA));
        Assert.True(BCrypt.Net.BCrypt.Verify(password, hashB));
    }

    [Fact]
    public void Constructor_RejectsCostFactorOutsideAllowedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthService(
            Mock.Of<IUserRepositoryFactory>(),
            Mock.Of<IUserTenantMapRepository>(),
            Mock.Of<IMasterConnectionFactory>(),
            NullLogger<AuthService>.Instance,
            bcryptCostFactor: 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthService(
            Mock.Of<IUserRepositoryFactory>(),
            Mock.Of<IUserTenantMapRepository>(),
            Mock.Of<IMasterConnectionFactory>(),
            NullLogger<AuthService>.Instance,
            bcryptCostFactor: 15));
    }
}
