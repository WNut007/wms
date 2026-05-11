using WMS.BLL.Services.Auth;
using WMS.DAL.Repositories.Master;
using WMS.Domain.Entities.Master;

namespace WMS.Web.Infrastructure;

// Phase 27 — first-run seeder for the initial SuperAdmin. Runs once
// at app startup; idempotent (UpsertByEmail). Config-driven so the
// repo isn't seeded with a magic email + hardcoded password.
//
// appsettings.json convention:
//   "InitialSuperAdmin": {
//     "Email": "superadmin@example.com",
//     "FullName": "Platform Administrator",
//     "InitialPassword": "ChangeMe!2026"
//   }
//
// The InitialPassword is ONLY used on first-run (when the SuperAdmins
// table is empty for this Email). Subsequent runs upsert FullName +
// IsActive but never touch PasswordHash — the operator changes their
// password through the /SuperAdmin/ChangePassword flow.
//
// SuperAdmin row is created with MustChangePassword=true so first
// login forces a rotation.
public static class SuperAdminBootstrap
{
    public const string SectionName = "InitialSuperAdmin";

    public static async Task EnsureAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var section = config.GetSection(SectionName);
        var email = section["Email"]?.Trim();
        var fullName = section["FullName"]?.Trim();
        var initialPassword = section["InitialPassword"];

        if (string.IsNullOrWhiteSpace(email))
        {
            // No bootstrap config — assume SuperAdmins exist already
            // (production deploy) or that the operator wants to seed
            // via a different path. Log + skip.
            logger.LogInformation(
                "InitialSuperAdmin:Email not configured — skipping SuperAdmin bootstrap.");
            return;
        }

        if (string.IsNullOrWhiteSpace(initialPassword))
        {
            logger.LogWarning(
                "InitialSuperAdmin:Email configured ('{Email}') but InitialPassword missing — skipping.",
                email);
            return;
        }

        var repo = services.GetRequiredService<ISuperAdminRepository>();
        var auth = services.GetRequiredService<IAuthService>();

        var existing = await repo.GetByEmailAsync(email, ct);
        if (existing is not null)
        {
            // Idempotent — refresh non-credential fields only. NEVER
            // overwrite PasswordHash from config (would let anyone with
            // appsettings access reset the SuperAdmin's password).
            logger.LogInformation(
                "SuperAdmin '{Email}' already exists — bootstrap skipped (PasswordHash preserved).",
                email);
            return;
        }

        var newAdmin = new SuperAdmin
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = auth.HashPassword(initialPassword),
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
            IsActive = true,
            MustChangePassword = true,    // force rotate on first login
        };

        await repo.UpsertByEmailAsync(newAdmin, ct);
        logger.LogWarning(
            "SuperAdmin '{Email}' bootstrapped from InitialSuperAdmin config. " +
            "MustChangePassword=true — operator MUST rotate on first login.",
            email);
    }
}
