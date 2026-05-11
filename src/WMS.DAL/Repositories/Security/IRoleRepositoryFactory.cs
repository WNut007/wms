namespace WMS.DAL.Repositories.Security;

public interface IRoleRepositoryFactory
{
    IRoleRepository For(Guid tenantId);
}
