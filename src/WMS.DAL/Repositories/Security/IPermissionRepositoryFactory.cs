namespace WMS.DAL.Repositories.Security;

// Resolves an IPermissionRepository bound to a specific tenant's DB
// connection. Mirrors IUserRepositoryFactory's shape.
public interface IPermissionRepositoryFactory
{
    IPermissionRepository For(Guid tenantId);
}
