using Moq;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.Security;
using WMS.DAL.Repositories.Security;
using WMS.Domain.Entities.Security;

namespace WMS.UnitTests.Services.Security;

// Phase 24 — Security invariants. The headline tests are:
//  - Last-admin guard refuses deactivation
//  - Self-deactivation refused
//  - System-role permission edit refused
//  - Email collision refused on Create + Update
//  - Audit row written on every successful mutation
public class SecurityServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorId  = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private record Build(
        SecurityService Service,
        Mock<IUserRepository> UserRepo,
        Mock<IUserRoleRepository> UserRoleRepo,
        Mock<IRoleRepository> RoleRepo,
        Mock<IAuditLogRepository> AuditRepo,
        Mock<IAuthService> Auth);

    private static Build BuildService()
    {
        var userRepo = new Mock<IUserRepository>();
        var userRoleRepo = new Mock<IUserRoleRepository>();
        var roleRepo = new Mock<IRoleRepository>();
        var auditRepo = new Mock<IAuditLogRepository>();

        var userFactory = new Mock<IUserRepositoryFactory>();
        userFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(userRepo.Object);
        var userRoleFactory = new Mock<IUserRoleRepositoryFactory>();
        userRoleFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(userRoleRepo.Object);
        var roleFactory = new Mock<IRoleRepositoryFactory>();
        roleFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(roleRepo.Object);
        var auditFactory = new Mock<IAuditLogRepositoryFactory>();
        auditFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(auditRepo.Object);

        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("hashed");

        var svc = new SecurityService(
            userFactory.Object, userRoleFactory.Object, roleFactory.Object,
            auditFactory.Object, auth.Object);

        return new Build(svc, userRepo, userRoleRepo, roleRepo, auditRepo, auth);
    }

    // ── CreateUser ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_HappyPath_HashesPasswordInsertsAssignsRolesAndAudits()
    {
        var b = BuildService();
        var roleId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.EmailExistsAsync("new@x.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        b.UserRoleRepo.Setup(r => r.ReplaceForUserAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 0));

        var newId = await b.Service.CreateUserAsync(
            TenantId,
            new CreateUserRequest("new@x.com", "pass1234", "New User", null, new[] { roleId }),
            ActorId, null, null);

        Assert.NotEqual(Guid.Empty, newId);
        b.Auth.Verify(a => a.HashPassword("pass1234"), Times.Once);
        b.UserRepo.Verify(r => r.InsertAsync(
            It.Is<User>(u => u.Email == "new@x.com" && u.PasswordHash == "hashed" && u.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
        b.UserRoleRepo.Verify(r => r.ReplaceForUserAsync(
            newId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(roleId)),
            ActorId, It.IsAny<CancellationToken>()), Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.UserCreated
                && e.EntityType == AuditEventTypes.EntityUser),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateUser_EmailExists_Throws()
    {
        var b = BuildService();
        b.UserRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.CreateUserAsync(
                TenantId,
                new CreateUserRequest("dup@x.com", "pass1234", null, null, Array.Empty<Guid>()),
                ActorId, null, null));

        b.UserRepo.Verify(r => r.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateUser_ShortPassword_ThrowsArgumentException()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            b.Service.CreateUserAsync(
                TenantId,
                new CreateUserRequest("a@x.com", "short", null, null, Array.Empty<Guid>()),
                ActorId, null, null));
    }

    // ── UpdateUser ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_HappyPath_UpdatesAndAudits()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, Email = "old@x.com" });
        b.UserRepo.Setup(r => r.EmailExistsAsync(
                "new@x.com", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        b.UserRoleRepo.Setup(r => r.ReplaceForUserAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0));

        await b.Service.UpdateUserAsync(
            TenantId,
            new UpdateUserRequest(userId, "new@x.com", "Renamed", null, Array.Empty<Guid>()),
            ActorId, null, null);

        b.UserRepo.Verify(r => r.UpdateAsync(
            It.Is<User>(u => u.Email == "new@x.com" && u.FullName == "Renamed"),
            It.IsAny<CancellationToken>()), Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.UserUpdated),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUser_EmailCollidesWithOtherUser_Throws()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, Email = "old@x.com" });
        b.UserRepo.Setup(r => r.EmailExistsAsync(
                "taken@x.com", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.UpdateUserAsync(
                TenantId,
                new UpdateUserRequest(userId, "taken@x.com", null, null, Array.Empty<Guid>()),
                ActorId, null, null));
    }

    // ── ToggleUserActive — last-admin + self guards ────────────────────

    [Fact]
    public async Task ToggleActive_DeactivateSelf_Throws()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ToggleUserActiveAsync(
                TenantId, ActorId, isActive: false, ActorId, null, null));
        b.UserRepo.Verify(r => r.SetActiveAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ToggleActive_DeactivateLastAdmin_Throws()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, IsActive = true });
        b.UserRoleRepo.Setup(r => r.GetRoleIdsByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { adminRoleId });
        b.RoleRepo.Setup(r => r.GetByIdAsync(adminRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = adminRoleId, Code = "ADMIN", IsSystemRole = true });
        b.UserRepo.Setup(r => r.CountActiveAdminsAsync("ADMIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);  // this user IS the last admin

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ToggleUserActiveAsync(
                TenantId, userId, isActive: false, ActorId, null, null));

        b.UserRepo.Verify(r => r.SetActiveAsync(
            It.IsAny<Guid>(), false, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ToggleActive_DeactivateNonLastAdmin_Succeeds()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, IsActive = true });
        b.UserRoleRepo.Setup(r => r.GetRoleIdsByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { adminRoleId });
        b.RoleRepo.Setup(r => r.GetByIdAsync(adminRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = adminRoleId, Code = "ADMIN" });
        b.UserRepo.Setup(r => r.CountActiveAdminsAsync("ADMIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);  // two admins; safe to deactivate this one
        b.UserRepo.Setup(r => r.SetActiveAsync(
                userId, false, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.ToggleUserActiveAsync(
            TenantId, userId, isActive: false, ActorId, null, null);

        b.UserRepo.Verify(r => r.SetActiveAsync(userId, false, ActorId, It.IsAny<CancellationToken>()),
            Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.UserDeactivated),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToggleActive_NonAdminUser_NoCountCheckNeeded()
    {
        // User holds only PICKER → deactivating them isn't gated by the
        // last-admin guard, even if CountActiveAdmins returned 0.
        var b = BuildService();
        var userId = Guid.NewGuid();
        var pickerRoleId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, IsActive = true });
        b.UserRoleRepo.Setup(r => r.GetRoleIdsByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pickerRoleId });
        b.RoleRepo.Setup(r => r.GetByIdAsync(pickerRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = pickerRoleId, Code = "PICKER" });
        b.UserRepo.Setup(r => r.SetActiveAsync(
                userId, false, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.ToggleUserActiveAsync(
            TenantId, userId, isActive: false, ActorId, null, null);

        b.UserRepo.Verify(r => r.CountActiveAdminsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ToggleActive_AlreadyAtTargetState_Idempotent_NoAudit()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, IsActive = false });  // already inactive

        await b.Service.ToggleUserActiveAsync(
            TenantId, userId, isActive: false, ActorId, null, null);

        // No audit emitted — no state change occurred.
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── SetPermission — system-role guard ──────────────────────────────

    [Fact]
    public async Task SetPermission_SystemRole_Throws()
    {
        var b = BuildService();
        var systemRoleId = Guid.NewGuid();
        b.RoleRepo.Setup(r => r.GetByIdAsync(systemRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = systemRoleId, Code = "ADMIN", IsSystemRole = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.SetPermissionAsync(
                TenantId,
                new SetPermissionRequest(systemRoleId, Guid.NewGuid(),
                    CanView: true, CanAdd: false, CanEdit: false, CanDelete: false, CanApprove: false),
                ActorId, null, null));

        b.RoleRepo.Verify(r => r.UpsertPermissionAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetPermission_CustomRole_UpsertsAndAudits()
    {
        var b = BuildService();
        var customRoleId = Guid.NewGuid();
        var functionId = Guid.NewGuid();
        b.RoleRepo.Setup(r => r.GetByIdAsync(customRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = customRoleId, Code = "AUDITOR", IsSystemRole = false });

        await b.Service.SetPermissionAsync(
            TenantId,
            new SetPermissionRequest(customRoleId, functionId,
                CanView: true, CanAdd: false, CanEdit: false, CanDelete: false, CanApprove: false),
            ActorId, null, null);

        b.RoleRepo.Verify(r => r.UpsertPermissionAsync(
            customRoleId, functionId, true, false, false, false, false,
            ActorId, It.IsAny<CancellationToken>()), Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.RolePermissionChanged),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Unlock ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockUser_AlreadyUnlocked_NoOp_NoAudit()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, FailedLoginAttempts = 0, LockedUntil = null });

        await b.Service.UnlockUserAsync(TenantId, userId, ActorId, null, null);

        b.UserRepo.Verify(r => r.ResetFailedLoginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnlockUser_WhenLocked_ResetsAndAudits()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                FailedLoginAttempts = 5,
                LockedUntil = DateTime.UtcNow.AddMinutes(10),
            });

        await b.Service.UnlockUserAsync(TenantId, userId, ActorId, null, null);

        b.UserRepo.Verify(r => r.ResetFailedLoginAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.UserUnlocked),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Password change (self) — Phase 25 ──────────────────────────────

    [Fact]
    public async Task ChangePassword_HappyPath_HashesAndAudits()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        var existingHash = BCrypt.Net.BCrypt.HashPassword("OldPass123", workFactor: 4);
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId, Email = "u@x.com",
                PasswordHash = existingHash, IsActive = true,
            });

        await b.Service.ChangePasswordAsync(
            TenantId, userId, "OldPass123", "NewPass456", "127.0.0.1", "test");

        b.UserRepo.Verify(r => r.UpdatePasswordHashAsync(
            userId, "hashed", userId, It.IsAny<CancellationToken>()), Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.PasswordChangedSelf
                && e.UserId == userId && e.EntityId == userId
                && e.IpAddress == "127.0.0.1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Throws()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        var existingHash = BCrypt.Net.BCrypt.HashPassword("OldPass123", workFactor: 4);
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, PasswordHash = existingHash, IsActive = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ChangePasswordAsync(TenantId, userId, "wrong", "NewPass456", null, null));
        b.UserRepo.Verify(r => r.UpdatePasswordHashAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChangePassword_PolicyViolation_ThrowsArgumentException()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            b.Service.ChangePasswordAsync(
                TenantId, Guid.NewGuid(), "OldPass123", "weak", null, null));
    }

    [Fact]
    public async Task ChangePassword_NewEqualsCurrent_Throws()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ChangePasswordAsync(
                TenantId, Guid.NewGuid(), "SamePass1", "SamePass1", null, null));
    }

    [Fact]
    public async Task ChangePassword_InactiveUser_Throws()
    {
        var b = BuildService();
        var userId = Guid.NewGuid();
        var existingHash = BCrypt.Net.BCrypt.HashPassword("OldPass123", workFactor: 4);
        b.UserRepo.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, PasswordHash = existingHash, IsActive = false });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ChangePasswordAsync(
                TenantId, userId, "OldPass123", "NewPass456", null, null));
    }

    // ── Password reset (admin) — Phase 25 ──────────────────────────────

    [Fact]
    public async Task ResetPassword_HappyPath_HashesAndAudits()
    {
        var b = BuildService();
        var targetId = Guid.NewGuid();
        b.UserRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = targetId });

        await b.Service.ResetPasswordAsync(
            TenantId, targetId, "NewPass456", ActorId, "127.0.0.1", "test");

        b.UserRepo.Verify(r => r.UpdatePasswordHashAsync(
            targetId, "hashed", ActorId, It.IsAny<CancellationToken>()), Times.Once);
        b.AuditRepo.Verify(a => a.AppendAsync(
            It.Is<AuditLogEntry>(e => e.EventType == AuditEventTypes.PasswordResetAdmin
                && e.UserId == ActorId && e.EntityId == targetId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_RefusesSelfReset()
    {
        var b = BuildService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ResetPasswordAsync(TenantId, ActorId, "NewPass456", ActorId, null, null));
        Assert.Contains("ChangePassword", ex.Message);

        b.UserRepo.Verify(r => r.UpdatePasswordHashAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResetPassword_PolicyViolation_ThrowsArgumentException()
    {
        var b = BuildService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            b.Service.ResetPasswordAsync(
                TenantId, Guid.NewGuid(), "weak", ActorId, null, null));
    }

    [Fact]
    public async Task ResetPassword_TargetUserNotFound_Throws()
    {
        var b = BuildService();
        b.UserRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            b.Service.ResetPasswordAsync(
                TenantId, Guid.NewGuid(), "NewPass456", ActorId, null, null));
    }
}
