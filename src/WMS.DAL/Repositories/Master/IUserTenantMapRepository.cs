using WMS.Common.Auth;

namespace WMS.DAL.Repositories.Master;

// Master-DB lookup: which (Active) tenants is this email allowed to
// enter? Used by AuthService.AuthenticateAsync to know where to verify
// the password and which tenants to surface in Step 2's picker.
public interface IUserTenantMapRepository
{
    Task<IReadOnlyList<UserTenantInfo>> GetByEmailAsync(
        string email,
        CancellationToken ct = default);
}
