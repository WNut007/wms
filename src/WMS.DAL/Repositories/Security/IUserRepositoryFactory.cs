namespace WMS.DAL.Repositories.Security;

// Resolves an IUserRepository bound to the named tenant's DB connection.
// Lets callers (AuthService, etc.) request "the user repo for tenant X"
// without juggling IDbConnection lifetimes themselves.
//
// The returned instance owns its connection; dispose the repo (or the
// surrounding scope) when done.
public interface IUserRepositoryFactory
{
    IUserRepository For(Guid tenantId);
}
