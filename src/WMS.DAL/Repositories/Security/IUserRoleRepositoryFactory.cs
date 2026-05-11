namespace WMS.DAL.Repositories.Security;

public interface IUserRoleRepositoryFactory
{
    IUserRoleRepository For(Guid tenantId);
}
