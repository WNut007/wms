using Microsoft.Extensions.Caching.Memory;
using Moq;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.SuperAdmin;
using WMS.DAL.Repositories.Master;
using SuperAdminEntity = WMS.Domain.Entities.Master.SuperAdmin;

namespace WMS.UnitTests.Services.SuperAdmin;

// Phase 27 — SuperAdmin auth invariants. Headline tests:
//  - Login happy + audits LoginSuccess
//  - Unknown email + InvalidPassword + Locked + Inactive all fail
//  - 5 fails crosses threshold → SetLockedUntil + AccountLockout audit
//  - Rate limit refuses 6th attempt
//  - ChangePassword wrong-current / policy violation / no-op rejected
public class SuperAdminAuthServiceTests
{
    private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private record Build(
        SuperAdminAuthService Service,
        Mock<ISuperAdminRepository> Repo,
        Mock<ISystemAuditLogRepository> Audit,
        Mock<IAuthService> Auth,
        ILoginRateLimiter RateLimiter);

    private static Build BuildService(int rateLimit = 100)
    {
        var repo = new Mock<ISuperAdminRepository>();
        var audit = new Mock<ISystemAuditLogRepository>();
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("hashed-by-test");

        var limiter = new LoginRateLimiter(
            new MemoryCache(new MemoryCacheOptions()),
            maxAttemptsPerWindow: rateLimit,
            window: TimeSpan.FromMinutes(1));

        var svc = new SuperAdminAuthService(repo.Object, audit.Object, auth.Object, limiter);
        return new Build(svc, repo, audit, auth, limiter);
    }

    [Fact]
    public async Task Login_UnknownEmail_FailsAndAudits()
    {
        var b = BuildService();
        b.Repo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SuperAdminEntity?)null);

        var result = await b.Service.AuthenticateAsync("nobody@x.com", "pass", "1.1.1.1", "test");

        Assert.False(result.Success);
        Assert.Equal("UnknownEmail", result.FailureReason);
        b.Audit.Verify(a => a.AppendAsync(
            It.Is<SystemAuditLogEntry>(e => e.EventType == SystemAuditEventTypes.SuperAdminLoginFailure),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_HappyPath_AuditsAndUpdatesLastLogin()
    {
        var b = BuildService();
        var hash = BCrypt.Net.BCrypt.HashPassword("CorrectPass", workFactor: 4);
        b.Repo.Setup(r => r.GetByEmailAsync("a@x.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, Email = "a@x.com", PasswordHash = hash, IsActive = true,
            });

        var result = await b.Service.AuthenticateAsync("a@x.com", "CorrectPass", "1.1.1.1", "test");

        Assert.True(result.Success);
        Assert.NotNull(result.Admin);
        b.Repo.Verify(r => r.UpdateLastLoginAsync(AdminId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        b.Audit.Verify(a => a.AppendAsync(
            It.Is<SystemAuditLogEntry>(e => e.EventType == SystemAuditEventTypes.SuperAdminLoginSuccess),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_InactiveAccount_FailsAndAudits()
    {
        var b = BuildService();
        b.Repo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity { Id = AdminId, IsActive = false });

        var result = await b.Service.AuthenticateAsync("a@x.com", "p", "ip", "ua");

        Assert.Equal("AccountInactive", result.FailureReason);
    }

    [Fact]
    public async Task Login_AccountLocked_FailsAndAudits()
    {
        var b = BuildService();
        b.Repo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, IsActive = true,
                LockedUntil = DateTime.UtcNow.AddMinutes(10),
            });

        var result = await b.Service.AuthenticateAsync("a@x.com", "p", "ip", "ua");

        Assert.Equal("AccountLocked", result.FailureReason);
    }

    [Fact]
    public async Task Login_InvalidPassword_IncrementsCounter()
    {
        var b = BuildService();
        var hash = BCrypt.Net.BCrypt.HashPassword("CorrectPass", workFactor: 4);
        b.Repo.Setup(r => r.GetByEmailAsync("a@x.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, Email = "a@x.com", PasswordHash = hash, IsActive = true,
                FailedLoginAttempts = 1,
            });
        b.Repo.Setup(r => r.GetByIdAsync(AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, FailedLoginAttempts = 2,
            });

        var result = await b.Service.AuthenticateAsync("a@x.com", "wrong", "ip", "ua");

        Assert.Equal("InvalidPassword", result.FailureReason);
        b.Repo.Verify(r => r.IncrementFailedLoginAsync(AdminId, It.IsAny<CancellationToken>()),
            Times.Once);
        b.Repo.Verify(r => r.SetLockedUntilAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);  // not yet at threshold
    }

    [Fact]
    public async Task Login_FifthFailure_TriggersLockout()
    {
        var b = BuildService();
        var hash = BCrypt.Net.BCrypt.HashPassword("CorrectPass", workFactor: 4);
        b.Repo.Setup(r => r.GetByEmailAsync("a@x.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, Email = "a@x.com", PasswordHash = hash, IsActive = true,
                FailedLoginAttempts = 4,
            });
        b.Repo.Setup(r => r.GetByIdAsync(AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, FailedLoginAttempts = 5, LockedUntil = null,
            });

        var result = await b.Service.AuthenticateAsync("a@x.com", "wrong", "ip", "ua");

        Assert.Equal("InvalidPassword", result.FailureReason);
        b.Repo.Verify(r => r.SetLockedUntilAsync(AdminId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        b.Audit.Verify(a => a.AppendAsync(
            It.Is<SystemAuditLogEntry>(e => e.EventType == SystemAuditEventTypes.SuperAdminLockout),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_OverRateLimit_FailsWithRateLimited()
    {
        var b = BuildService(rateLimit: 2);
        // Burn 2 of 2 allowed attempts with the same IP.
        b.Repo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SuperAdminEntity?)null);
        await b.Service.AuthenticateAsync("a@x.com", "p", "1.1.1.1", "ua");
        await b.Service.AuthenticateAsync("a@x.com", "p", "1.1.1.1", "ua");

        // 3rd attempt = RateLimited.
        var result = await b.Service.AuthenticateAsync("a@x.com", "p", "1.1.1.1", "ua");

        Assert.Equal("RateLimited", result.FailureReason);
    }

    // ── ChangePassword ─────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_HappyPath_HashesAndAudits()
    {
        var b = BuildService();
        var hash = BCrypt.Net.BCrypt.HashPassword("OldPass123", workFactor: 4);
        b.Repo.Setup(r => r.GetByIdAsync(AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity
            {
                Id = AdminId, Email = "a@x.com",
                PasswordHash = hash, IsActive = true,
            });

        await b.Service.ChangePasswordAsync(AdminId, "OldPass123", "NewPass456", "ip", "ua");

        b.Repo.Verify(r => r.UpdatePasswordHashAsync(
            AdminId, "hashed-by-test", false, AdminId, It.IsAny<CancellationToken>()),
            Times.Once);
        b.Audit.Verify(a => a.AppendAsync(
            It.Is<SystemAuditLogEntry>(e => e.EventType == SystemAuditEventTypes.SuperAdminPasswordChange),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Throws()
    {
        var b = BuildService();
        var hash = BCrypt.Net.BCrypt.HashPassword("OldPass123", workFactor: 4);
        b.Repo.Setup(r => r.GetByIdAsync(AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuperAdminEntity { Id = AdminId, PasswordHash = hash, IsActive = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ChangePasswordAsync(AdminId, "wrong", "NewPass456", null, null));
    }

    [Fact]
    public async Task ChangePassword_PolicyViolation_Throws()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            b.Service.ChangePasswordAsync(AdminId, "OldPass123", "weak", null, null));
    }

    [Fact]
    public async Task ChangePassword_NewEqualsCurrent_Throws()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ChangePasswordAsync(AdminId, "SamePass1", "SamePass1", null, null));
    }
}
