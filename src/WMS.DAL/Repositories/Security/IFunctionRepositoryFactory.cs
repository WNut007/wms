namespace WMS.DAL.Repositories.Security;

public interface IFunctionRepositoryFactory
{
    IFunctionRepository For(Guid tenantId);
}
